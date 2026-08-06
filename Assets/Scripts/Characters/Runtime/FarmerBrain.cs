using System.Collections.Generic;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    /// <summary>Что фермер решил делать. Само действие выполняет <see cref="FarmerAgent"/>.</summary>
    public enum FarmerIntent
    {
        /// <summary>Ничего подходящего — постоять и осмотреться.</summary>
        Idle = 0,
        Sleep = 1,
        /// <summary>Поесть или попить у постройки.</summary>
        Refresh = 2,
        /// <summary>Отнести груз домой.</summary>
        Deliver = 3,
        /// <summary>Сходить на рынок: продать излишки, докупить грядок.</summary>
        Trade = 4,
        /// <summary>Собрать конкретную грядку.</summary>
        Harvest = 5,
        /// <summary>Подождать у грядки, которой осталось несколько секунд.</summary>
        Await = 6,
        /// <summary>Быт: посидеть у костра, постоять дома, поглазеть по сторонам.</summary>
        Relax = 7,
        /// <summary>Навести порядок: перенести грядку к своим.</summary>
        Tidy = 8,
        /// <summary>Построить задуманное: клумбу, скамейку, костёр у крыльца.</summary>
        Improve = 9,
        /// <summary>Встать к станку и вести партии — работа мастерового (этап 2 колонии).</summary>
        Craft = 10,
        /// <summary>Отвезти короб сданного заказа к рынку — театр возчика (этап 2 колонии).</summary>
        CarryOrder = 11,
        /// <summary>Встать к стройплощадке и строить — работа строителя (этап 3 колонии).</summary>
        Build = 12
    }

    /// <summary>Одно взвешенное намерение: что делать, насколько хочется и с чем именно.</summary>
    public readonly struct FarmerDecision
    {
        public readonly FarmerIntent Intent;

        /// <summary>Насколько это сейчас уместно. Ноль — невозможно.</summary>
        public readonly float Score;

        /// <summary>Грядка для <see cref="FarmerIntent.Harvest"/> и <see cref="FarmerIntent.Await"/>.</summary>
        public readonly Growable Plot;

        /// <summary>Постройка для <see cref="FarmerIntent.Refresh"/> и <see cref="FarmerIntent.Trade"/>.</summary>
        public readonly Building Place;

        /// <summary>Точка для <see cref="FarmerIntent.Relax"/> и <see cref="FarmerIntent.Improve"/>.</summary>
        public readonly Vector3 Spot;

        /// <summary>Замысел для <see cref="FarmerIntent.Improve"/>.</summary>
        public readonly ImprovementDefinition Improvement;

        /// <summary>Стройплощадка для <see cref="FarmerIntent.Build"/>.</summary>
        public readonly ConstructionSite Site;

        /// <summary>Одна строка от первого лица — её показывает пузырь над головой.</summary>
        public readonly string Thought;

        public FarmerDecision(FarmerIntent intent, float score, string thought,
                              Growable plot = null, Building place = null, Vector3 spot = default,
                              ImprovementDefinition improvement = null, ConstructionSite site = null)
        {
            Intent = intent;
            Score = score;
            Thought = thought;
            Plot = plot;
            Place = place;
            Spot = spot;
            Improvement = improvement;
            Site = site;
        }

        public static readonly FarmerDecision None = new FarmerDecision(FarmerIntent.Idle, 0f, null);

        public bool IsSomething => Score > 0f;
    }

    /// <summary>
    /// Решает, чем фермеру заняться. Отдельно от <see cref="FarmerAgent"/> намеренно: агент —
    /// это «что я делаю сейчас», мозг — «что делать дальше», и раньше обе половины жили одной
    /// лестницей <c>if</c>.
    /// <para>
    /// Лестница и была причиной, по которой персонаж не выглядел решающим: порядок всегда один,
    /// ничто ни с чем не взвешивается, передумать нельзя. Зритель видел не выбор, а исполнение
    /// чеклиста. Здесь каждое намерение само считает себе оценку из обстановки, и побеждает
    /// лучшее — поэтому его поведение меняется вместе с фермой, а не следует списку.
    /// </para>
    /// <para>
    /// Заодно это лечит голодание дальних грядок. Раньше цель выбиралась как «ближайшая спелая»,
    /// и рядом с домом всегда что-то поспевало — до руды на краю фермы очередь не доходила
    /// никогда. Теперь в оценку входят и ценность, и время ожидания, поэтому забытое дорогое
    /// само всплывает наверх.
    /// </para>
    /// <para>
    /// Оценки нарочно написаны руками и с объяснениями, а не выведены из общей формулы: система,
    /// чьё решение нельзя объяснить словами, читается игроком не как умная, а как случайная.
    /// </para>
    /// </summary>
    public sealed class FarmerBrain
    {
        /// <summary>
        /// Полосы оценок, чтобы намерения можно было сравнивать не гадая:
        /// 8+ — «иначе всё встанет», 5–8 — нужды и полный рюкзак, 3–5 — работа, 1–3 — быт.
        /// </summary>
        private const float ScoreCritical = 8f;
        private const float ScoreWork = 4f;
        private const float ScoreLiving = 1.2f;

        /// <summary>Ценность урожая, дающая половину шкалы <see cref="Value01"/> (мягкая сатурация).</summary>
        private const float RichHarvest = 40f;

        /// <summary>Сколько секунд ожидания считаются «забыт совсем».</summary>
        private const float LongWait = 90f;

        /// <summary>
        /// Насколько скоро грядка должна поспеть, чтобы имело смысл её подождать.
        /// Internal: агент сверяется с этим же числом, решая, что ожидание протухло, —
        /// два разных порога в двух файлах разошлись бы при первой же правке баланса.
        /// </summary>
        internal const float WorthWaiting = 14f;

        /// <summary>
        /// Потолок оценки сбора наружу: справедливость между грядками не должна отменять
        /// распорядок. Число живёт в узкой щели между двумя объявленными правилами —
        /// выше предела замысла (5.1), чтобы работа оставалась важнее обустройства, и ниже
        /// пола еды (5.4), чтобы голод обгонял работу, как обещано в <see cref="ScoreRefresh"/>.
        /// <para>
        /// Стояло 7.5 — в середине полосы нужд, и это молча отменяло всю иерархию: голодный
        /// продолжал собирать, пока сытость не падала ниже 8%, а полный склад не мог перебить
        /// сбор даже клапаном срочной торговли.
        /// </para>
        /// </summary>
        private const float HarvestCeiling = ScoreWork + 1.2f;

        /// <summary>
        /// Потолок оценки порядка, когда беспорядок надоел до предела. Как и
        /// <see cref="HarvestCeiling"/>, число живёт в объявленной щели: выше потолка
        /// ожидания (<c>ScoreWork - 0.2</c> = 3.8), чтобы надоевший бардак перебивал
        /// стояние у почти спелой грядки, и ниже базы сбора (4.0), чтобы никогда не
        /// отменять работу. Спелое собирают, а не обходят с грядкой в руках.
        /// <para>
        /// Без накопления порядок не наводился <b>никогда</b>: измерено 04.08.2026 —
        /// 24 окна «руки пусты и есть что перенести», ноль побед, все 24 забрал сбор.
        /// Потолок в покое (3.6 у идеально аккуратного) просто ниже базы сбора.
        /// </para>
        /// </summary>
        private const float TidyCeiling = ScoreWork - 0.1f;

        /// <summary>
        /// За сколько секунд неубранная ферма надоедает фермеру средней аккуратности.
        /// Сопоставимо с третью суток (600 с): пара уборок за день, а не запой.
        /// </summary>
        private const float MessPatience = 180f;

        /// <summary>
        /// Сколько секунд <b>настоящей работы</b> подряд отделяют один перерыв от другого.
        /// Считается только время в делах сбора: стоял без дела — счётчик не рос, и перерыв
        /// не наступит у того, кто и так ничего не делал.
        /// <para>
        /// 120 с при светлом дне примерно в 288 с — это два перерыва за рабочий день.
        /// </para>
        /// </summary>
        private const float WorkBeforeRest = 120f;

        /// <summary>
        /// Сколько длится перерыв, секунд. Заметно больше одной переноски: смысл перерыва
        /// в том, чтобы фермер успел заняться чем-то другим, а не постоял и вернулся.
        /// </summary>
        private const float RestSeconds = 45f;

        private readonly FarmerAgent _agent;

        /// <summary>
        /// С какого момента на ферме есть что переносить. Отрицательное — всё на местах.
        /// Живёт в мозге, а не в оценке: раздражение — состояние фермера, и обнуляет его
        /// только доведённая до конца уборка.
        /// </summary>
        private double _messSince = -1.0;

        /// <summary>Накопленные секунды работы с прошлого перерыва.</summary>
        private float _worked;

        /// <summary>Когда перерыв кончится. Отрицательное — сейчас не перерыв.</summary>
        private double _restUntil = -1.0;

        /// <summary>Когда мозг смотрел на мир в прошлый раз — чтобы мерить работу временем.</summary>
        private double _lastLook = -1.0;

        public FarmerBrain(FarmerAgent agent) => _agent = agent;

        /// <summary>
        /// Перенёс — и отпустило: копим заново. Без сброса одна уборка тянула бы за собой
        /// все оставшиеся пары подряд, и вместо живого «дай приберусь» выходил бы запой.
        /// </summary>
        internal void MessTended() => _messSince = -1.0;

        /// <summary>Ночь ли сейчас по часам фермы.</summary>
        private static bool Night => DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;

        /// <summary>Какая доля светлого дня прошла, 0..1. Вне дня прижата к краям.</summary>
        private static float DayProgress =>
            DayNightCycle.Instance != null ? DayNightCycle.Instance.DayProgress01 : 0.5f;

        // ---- выбор ----

        /// <summary>Лучшее, чем сейчас можно заняться. Возвращает пустое решение, если ничего не подошло.</summary>
        public FarmerDecision Choose()
        {
            TickRest();

            var best = FarmerDecision.None;

            Consider(ScoreSleep(), ref best);
            Consider(ScoreRefresh(), ref best);
            Consider(ScoreDeliver(), ref best);
            Consider(ScoreHarvest(), ref best);
            Consider(ScoreAwait(), ref best);
            Consider(ScoreTrade(), ref best);
            Consider(ScoreCraft(), ref best);
            Consider(ScoreCarryOrder(), ref best);
            Consider(ScoreBuild(), ref best);
            Consider(ScoreTidy(), ref best);
            Consider(ScoreImprove(), ref best);
            Consider(ScoreRelax(), ref best);

            return best;
        }

        private static void Consider(in FarmerDecision candidate, ref FarmerDecision best)
        {
            if (candidate.Score > best.Score) best = candidate;
        }

        /// <summary>Идёт ли сейчас перерыв. На это время сбор, станок и ожидание закрыты наглухо.</summary>
        private bool Resting => _restUntil > 0.0;

        /// <summary>
        /// Перерыв — наружу: агенту у станка нужен явный выход, потому что партии, в
        /// отличие от грядки, не кончаются сами и «доведу до конца» там не наступает.
        /// </summary>
        internal bool IsResting => Resting;

        /// <summary>
        /// Распорядок: наработал <see cref="WorkBeforeRest"/> секунд — иди отдохни
        /// <see cref="RestSeconds"/> секунд, независимо от того, сколько спелого на ферме.
        /// <para>
        /// Здесь нет ни одной случайности, и это осознанно. Раньше на этом месте стояла
        /// блажь с броском костей: она давала ту же нелинейность, но за счёт правила
        /// «случайность в деле читается как поломка». Распорядок даёт то же самое честно —
        /// игрок, посмотрев минуту, поймёт закономерность, а не решит, что ИИ сломался.
        /// </para>
        /// <para>
        /// Три вещи, которые делают перерыв перерывом, а не сбоем. <b>Копится только
        /// работа</b>: часы стоят, пока фермер стоит, поэтому бездельник до перерыва не
        /// доживёт. <b>Начатое не бросается</b>: перерыв не обрывает дело — <c>Rethink</c>
        /// меняет занятие только на более высокую оценку, а во время перерыва все оставшиеся
        /// оценки ниже сбора, так что текущая грядка доводится до конца. <b>Нужды не
        /// участвуют</b>: закрыты ровно сбор и ожидание, а еда, вода и сон идут своим
        /// чередом — в перерыв он как раз и поест.
        /// </para>
        /// </summary>
        private void TickRest()
        {
            double now = FarmingRuntime.Now;

            // Меряем временем, а не числом вызовов: интервал раздумий не постоянен, и
            // счёт «по решениям» разошёлся бы с секундами при первой же его правке.
            float dt = _lastLook < 0.0 ? 0f : Mathf.Clamp((float)(now - _lastLook), 0f, 5f);
            _lastLook = now;

            if (Resting)
            {
                if (now < _restUntil) return;

                _restUntil = -1.0;
                _worked = 0f;
                return;
            }

            if (!AtWork()) return;

            _worked += dt;
            if (_worked < WorkBeforeRest) return;

            _restUntil = now + RestSeconds;
        }

        /// <summary>
        /// Занят ли фермер прямо сейчас работой. Только это и копит часы до перерыва:
        /// уборка, обустройство и безделье отдыхом не оплачиваются. Станок — работа
        /// наравне со сбором: жалование не отменяет распорядка.
        /// </summary>
        private bool AtWork()
        {
            switch (_agent.State)
            {
                case FarmerState.GoingToHarvest:
                case FarmerState.Harvesting:
                case FarmerState.Awaiting:
                case FarmerState.ReturningHome:
                case FarmerState.Depositing:
                case FarmerState.GoingToCraft:
                case FarmerState.Crafting:
                case FarmerState.GoingToLoad:
                case FarmerState.DeliveringOrder:
                case FarmerState.GoingToBuild:
                case FarmerState.Constructing:
                    return true;

                default:
                    return false;
            }
        }

        // ---- намерения ----

        /// <summary>
        /// Сон. Ночью — по распорядку, среди дня — только когда вымотался: работать без сил
        /// значит работать медленно и всё равно лечь.
        /// </summary>
        private FarmerDecision ScoreSleep()
        {
            var needs = _agent.Needs;
            if (needs == null) return FarmerDecision.None;

            float score = _agent.SleepsAtNight && Night ? ScoreCritical - 0.5f : 0f;
            if (needs.IsTired) score = Mathf.Max(score, ScoreCritical + 0.2f);
            if (score <= 0f) return FarmerDecision.None;

            // С урожаем на руках спать нельзя — донести дороже. Пусть выигрывает доставка.
            if (!_agent.Pack.IsEmpty) return FarmerDecision.None;

            return new FarmerDecision(FarmerIntent.Sleep, score,
                Night ? "пора на боковую" : "с ног валюсь");
        }

        /// <summary>
        /// Еда и питьё. Обгоняют работу не из милосердия: голодный работает медленнее, и чем
        /// дольше он терпит, тем меньше успевает. Сходить поесть — это вложение, а не пауза.
        /// </summary>
        private FarmerDecision ScoreRefresh()
        {
            var needs = _agent.Needs;
            if (needs == null || (!needs.IsHungry && !needs.IsThirsty)) return FarmerDecision.None;

            // Просело одно — идём к нужной постройке; просели оба — берём что ближе.
            BuildingService? want = null;
            if (needs.IsThirsty && !needs.IsHungry) want = BuildingService.Well;
            else if (needs.IsHungry && !needs.IsThirsty) want = BuildingService.Kitchen;

            var place = BuildingRegistry.FindNearestUseful(
                _agent.Pos, needs.Satiety01, needs.Hydration01, want);
            if (place == null) return FarmerDecision.None;

            float worst = Mathf.Min(needs.Satiety01, needs.Hydration01);
            float urgency = 1f - Mathf.Clamp01(worst / 0.25f);

            float score = 5.4f + urgency * 3f - Travel(place.transform.position) * 0.5f;

            return new FarmerDecision(FarmerIntent.Refresh, score,
                place.Service == BuildingService.Well ? "надо попить" : "перекусить бы",
                place: place);
        }

        /// <summary>
        /// Отнести груз. Полный рюкзак означает, что работать больше нечем, а перед сном и
        /// в темноте донести важнее всего: уснуть с урожаем на руках — потерять день.
        /// </summary>
        private FarmerDecision ScoreDeliver()
        {
            var pack = _agent.Pack;
            if (pack == null || pack.IsEmpty) return FarmerDecision.None;

            // Склад не примет ни единицы из рюкзака — идти некуда. Раньше он шёл, «сдавал»,
            // и силовая перегрузка уничтожала весь груз под звук награды. Теперь ноша ждёт
            // в рюкзаке, пока игрок не освободит место, — потеря стала невозможной.
            if (!StorageAccepts(pack)) return FarmerDecision.None;

            float score = 2.8f + Fill01(pack) * 1.6f;

            if (pack.IsFull) score = Mathf.Max(score, 7.2f);
            if (Night || (_agent.Needs != null && _agent.Needs.IsTired))
                score = Mathf.Max(score, ScoreCritical + 0.6f);

            score -= Travel(_agent.HomePosition) * 0.4f;

            return new FarmerDecision(FarmerIntent.Deliver, score, "отнесу домой");
        }

        /// <summary>
        /// Что собрать. Три слагаемых вместо прежнего «ближайшее»: ценность вытаскивает наверх
        /// дорогое, ожидание — забытое, дорога всё ещё имеет вес, чтобы он не носился через всю
        /// ферму за каждой мелочью.
        /// </summary>
        private FarmerDecision ScoreHarvest()
        {
            // Онлайн-правило: урожай принадлежит игроку. Фермеру сбор закрыт тем же флагом,
            // что и ожидание, — наглухо, а не оценкой: «иногда соберёт» читалось бы поломкой.
            if (!_agent.MayHarvest) return FarmerDecision.None;

            // Перерыв закрывает сбор наглухо, а не придерживает оценкой. Полумера здесь
            // читалась бы хуже: «иногда собирает, иногда нет» выглядит поломкой, а «в
            // перерыв не собирает вообще» — распорядком, который игрок видит и понимает.
            if (Resting) return FarmerDecision.None;

            var pack = _agent.Pack;
            if (pack == null || pack.IsFull) return FarmerDecision.None;

            var ready = GrowableRegistry.Ready;
            if (ready.Count == 0) return FarmerDecision.None;

            Growable best = null;
            float bestPick = 0f;

            for (int i = 0; i < ready.Count; i++)
            {
                var plot = ready[i];
                if (plot == null) continue;
                // Ready-список не единственная точка правды: пересадка спелой грядки могла
                // оставить в нём призрак, и ходить к нему — значит ходить и разворачиваться.
                if (!plot.IsReady) continue;
                if (_agent.OnlyOwnCategory && plot.Category != _agent.Category) continue;

                // Наёмному — только рутина: дорогие многочасовые ступени принадлежат рукам
                // игрока (правило 1). Фильтр здесь, а не в оценке: «иногда берёт дорогое»
                // читалось бы как поломка, а «высокое не трогает вовсе» — как уговор.
                if (TierOf(plot) > FarmerAgent.HelperMaxTier) continue;

                float ripe01 = Mathf.Clamp01((float)plot.RipeSeconds / LongWait);

                // Видимость меряется сырой дистанцией, без черт: тултип радиуса обещает
                // «замечает в 25 метрах», и домосед должен ЗАМЕЧАТЬ так же — а вот идти ему
                // дороже, и это учтено ниже штрафом. Раньше вес черты сидел прямо в отсеве,
                // и робкий домосед ночью был слеп на два трети радиуса — дальние грядки
                // не собирались никогда, тихо. Давно спелое чуть расширяет обзор.
                float raw = TravelRaw(plot.transform.position);
                if (raw > 1f + ripe01 * 0.5f) continue;

                float travel = raw * TravelWeight();

                float score = ScoreWork
                            + Value01(plot) * 2.6f
                            + ripe01 * 2.2f
                            - travel * 1.8f;

                // Справедливость: потолок члена забытости (2.2) меньше разброса ценности (2.6),
                // и дерево 1-го уровня проигрывало любой свежеспелой дорогой грядке вечно.
                // Поэтому ВЫБОР цели ведётся по pick с неограниченным членом, растущим после
                // LongWait: у свежих конкурентов он ноль, у забытого — сколько заслужил.
                float pick = score + Mathf.Max(0f, (float)plot.RipeSeconds - LongWait) / LongWait * 1.5f;

                if (pick <= bestPick) continue;

                bestPick = pick;
                best = plot;
            }

            if (best == null) return FarmerDecision.None;

            // Наружу — с потолком: голодание чинится честной очередью между грядками,
            // а не правом сбора перебивать сон и еду. Придерж блажи снимается ПОСЛЕ
            // потолка: он про то, кто сегодня важнее, а не про справедливость между грядками.
            return new FarmerDecision(FarmerIntent.Harvest, Mathf.Min(bestPick, HarvestCeiling),
                HarvestThought(best), plot: best);
        }

        /// <summary>Ступень того, что вырастет на грядке. Без ресурса — первая: рутина по умолчанию.</summary>
        private static int TierOf(Growable plot)
        {
            var resource = plot.Definition != null ? plot.Definition.YieldResource : null;
            return resource != null ? resource.Tier : 1;
        }

        /// <summary>
        /// Подождать у грядки, которой осталось несколько секунд. Стоять без дела в двух шагах
        /// от почти созревшего — ровно та мелочь, из-за которой персонаж выглядит бездумным.
        /// </summary>
        private FarmerDecision ScoreAwait()
        {
            // Ожидание — это тот же сбор, только стоя: без права сбора ждать у грядки нечего.
            if (!_agent.MayHarvest) return FarmerDecision.None;

            // В перерыв оно закрыто вместе со сбором,
            // иначе фермер «отдыхал» бы, торча над почти спелой грядкой.
            if (Resting) return FarmerDecision.None;

            var pack = _agent.Pack;
            if (pack == null || pack.IsFull) return FarmerDecision.None;

            var all = GrowableRegistry.All;
            Growable best = null;
            float bestScore = 0f;

            for (int i = 0; i < all.Count; i++)
            {
                var plot = all[i];
                if (plot == null || plot.Phase != GrowthPhase.Growing) continue;
                if (_agent.OnlyOwnCategory && plot.Category != _agent.Category) continue;

                // Ждать имеет смысл только то, что потом можно собрать, — тот же фильтр ступеней.
                if (TierOf(plot) > FarmerAgent.HelperMaxTier) continue;

                double left = plot.TimeUntilReady;
                if (left < 0.0 || left > WorthWaiting) continue;

                // Тот же принцип, что в сборе: видит он сырой дистанцией, дорога — штрафом.
                float raw = TravelRaw(plot.transform.position);
                if (raw > 0.6f) continue;   // ждать имеет смысл только рядом

                float travel = raw * TravelWeight();

                // Чем меньше осталось и чем ближе — тем осмысленнее постоять.
                float soon = 1f - (float)(left / WorthWaiting);
                float score = ScoreWork - 0.8f + soon * 1.4f + Value01(plot) * 1.2f - travel * 1.2f;

                if (score <= bestScore) continue;

                bestScore = score;
                best = plot;
            }

            if (best == null) return FarmerDecision.None;

            // Потолок ниже базы сбора — ПОСЛЕ выбора лучшего кандидата, чтобы кандидаты
            // между собой всё ещё сравнивались честно. Ожидание — заполнитель против
            // безделья, а не работа: до потолка оно вылезало в полосу нужд (5.8) и
            // выигрывало у сбора реально спелого — фермер стоял у дорогой грядки,
            // пока рядом стояло собранное. Дословно жалоба игрока.
            bestScore = Mathf.Min(bestScore, ScoreWork - 0.2f);

            return new FarmerDecision(FarmerIntent.Await, bestScore, "вот-вот поспеет", plot: best);
        }

        /// <summary>
        /// Рынок. Одна ходка закрывает обе половины сделки: продать излишки и потратить выручку —
        /// фермер, который сходил продать, вернулся и снова пошёл покупать, выглядит сломанным.
        /// </summary>
        private FarmerDecision ScoreTrade()
        {
            var skills = _agent.Skills;
            if (skills == null || !skills.CanSell) return FarmerDecision.None;
            if (Shop.Instance == null) return FarmerDecision.None;

            bool selling = HasSurplus(out _, out int spare);
            bool buying = PickRestock() != null;
            if (!selling && !buying) return FarmerDecision.None;

            var market = FindMarket();
            if (market == null) return FarmerDecision.None;

            // Пустые руки — самое время сходить; с грузом сначала домой.
            float score = 3.2f;
            if (!_agent.Pack.IsEmpty) score -= 1.6f;

            // Гора излишков делает ходку срочной: собирать дальше некуда и незачем.
            score += Mathf.Clamp01(spare / (float)Mathf.Max(1, _agent.UrgentSurplus)) * 2.4f;

            // Полный склад — это клапан: продажа единственное, что освобождает место без
            // рук игрока. Не поднять её тут — значит смотреть, как урожай копится в
            // рюкзаке при забитых полках, и называть это работой.
            if (selling && FarmingRuntime.Sink is IInventory storage && storage.IsFull)
                score = Mathf.Max(score, 6.4f);

            score -= Travel(market.transform.position) * 0.9f;

            return new FarmerDecision(FarmerIntent.Trade, score,
                selling ? "снесу излишки на рынок" : "прикуплю грядку", place: market);
        }

        /// <summary>
        /// Замыслы: построить что-то своё — клумбу, скамейку, костёр у крыльца.
        /// <para>
        /// Это и есть «ферма развивается сама»: не по чужому списку задач, а потому что здесь
        /// живёт человек со вкусом и излишками досок. Оценка сидит между порядком и бытом
        /// (~2.5–3.5): выше безделья — замысел интереснее, чем просто посидеть, — но ниже любой
        /// работы и голода: обживание из-за недоенной коровы читалось бы как саботаж.
        /// </para>
        /// <para>
        /// Материал берётся только из излишков сверх <see cref="FarmerAgent.KeepMaterials"/> —
        /// того же буфера, что бережёт запасы игрока от его торговли. Житель строит из того,
        /// что ферме не жалко, и никогда из того, что игрок копит на постройку.
        /// </para>
        /// </summary>
        private FarmerDecision ScoreImprove()
        {
            // Ночь — не время стучать молотком, а вечер пусть остаётся вечеру.
            if (Night || DayProgress < 0.1f || DayProgress > 0.8f) return FarmerDecision.None;
            if (!_agent.ImprovementQuotaLeft) return FarmerDecision.None;

            // Вымотанный не обживается — сперва он живёт, потом украшает жизнь.
            // <para>
            // Раньше здесь стоял порог самочувствия 0.4, и он оказался вечным замком: пока на
            // ферме нет кухни и колодца, голод с жаждой падают в ноль и обратно не поднимаются
            // никогда — а с ними навсегда закрывается обустройство, единственный доступный
            // источник опыта Смекалки, а через него и торговля. Одна ненайденная постройка
            // выключала половину поведения. Усталость — честный гейт: она проходит сама, сном.
            // </para>
            var needs = _agent.Needs;
            if (needs != null && needs.IsTired) return FarmerDecision.None;

            var project = PickImprovement(out Building anchor);
            if (project == null) return FarmerDecision.None;

            if (!TryFindImprovementSpot(project, anchor, out Vector3 spot)) return FarmerDecision.None;

            float score = ScoreLiving + 1.3f;

            // Дозревшее желание отодвигает дешёвую рутину — тот же приём, что у забытых грядок:
            // свободной минуты на занятой ферме не бывает, её надо уметь взять. Потолок ~4.6
            // осознанный: дешёвая пшеница подождёт лишний цикл, голод и полный рюкзак — нет.
            score += _agent.ImprovementUrge01 * 1.8f;

            // Аккуратист обживает охотнее — та же черта, что тянет его наводить порядок.
            var traits = _agent.Traits;
            if (traits != null) score += (traits.Tidiness - 0.5f) * 1.6f;

            score -= Travel(spot) * 0.4f;

            // Place несёт постройку-якорь: агенту нужно знать, чей дворик пополнился.
            return new FarmerDecision(FarmerIntent.Improve, score, project.Thought,
                place: anchor, spot: spot, improvement: project);
        }

        // Переиспользуемые буферы выбора замысла: PickImprovement зовётся из каждого пересмотра
        // решений, и собирать мусор четыре раза в секунду ради дворика было бы расточительно.
        private static readonly List<(ImprovementDefinition project, Building anchor)> _candidates =
            new List<(ImprovementDefinition, Building)>(16);
        private static readonly Dictionary<object, int> _yardOrder = new Dictionary<object, int>(8);

        /// <summary>
        /// Случайный замысел из всех созревших, взвешенный по <see cref="ImprovementDefinition.Weight"/> —
        /// но случайность живёт только внутри осознанности:
        /// <para>
        /// 1. Замыслы с якорем-постройкой существуют лишь там, где стоит их постройка, — по
        /// кандидату на каждый её экземпляр. Стог не появится в чистом поле; у каждого хлева
        /// свой дворик со своим счётом.
        /// </para>
        /// <para>
        /// 2. В одном дворике доступна только младшая недостроенная ступень
        /// (<see cref="ImprovementDefinition.Order"/>): сперва вся ограда, потом сено, потом
        /// свет. Фонарь посреди пустого двора — ровно та бессмыслица, о которую разбивается
        /// вера в персонажа.
        /// </para>
        /// <para>
        /// Созревший — значит: открыт прогрессом фермы, не выстроен до предела и посилен складу
        /// с сохранением буфера игрока (<see cref="FarmerAgent.KeepMaterials"/>).
        /// </para>
        /// </summary>
        /// <summary>
        /// Замысел, до которого фермер дозрел, но которому не хватило материала. Нужен, чтобы
        /// отказ был слышен: «хочу, но нечем» и «не хочу» иначе выглядят одинаково — тишиной.
        /// </summary>
        private ImprovementDefinition _blockedProject;

        /// <summary>Чего не хватает на задуманное, или null. Читает агент, чтобы сказать это вслух.</summary>
        public ResourceDefinition MissingForImprovement =>
            _blockedProject != null && _blockedProject.Cost.IsValid ? _blockedProject.Cost.Resource : null;

        private ImprovementDefinition PickImprovement(out Building anchor)
        {
            anchor = null;
            var list = _agent.Improvements;
            if (list == null) return null;

            var storage = FarmingRuntime.Sink as IInventory;
            _candidates.Clear();
            _yardOrder.Clear();
            _blockedProject = null;

            for (int i = 0; i < list.Count; i++)
            {
                var project = list[i];
                if (project == null || project.Prefab == null) continue;
                if (!project.IsUnlocked) continue;

                var cost = project.Cost;
                if (cost.IsValid)
                {
                    if (storage == null) continue;

                    // Свой маленький буфер, а не торговый: KeepMaterials бережёт запасы игрока
                    // от распродажи, он в десятки раз больше цены замысла (1-4 единицы), и,
                    // применённый сюда, он молча выключал обустройство целиком.
                    if (storage.GetAmount(cost.Resource) < cost.Amount + _agent.KeepForImprovements)
                    {
                        _blockedProject = project;
                        continue;
                    }
                }

                if (project.Anchor == ImprovementAnchor.Building)
                {
                    var kind = project.AnchorBuilding;
                    if (kind == null) continue;

                    var all = BuildingRegistry.All;
                    for (int b = 0; b < all.Count; b++)
                    {
                        var building = all[b];
                        if (building == null || building.Definition != kind) continue;
                        if (_agent.BuiltCount(project, building) >= project.MaxCount) continue;
                        Offer(project, building);
                    }
                }
                else
                {
                    if (_agent.BuiltCount(project) >= project.MaxCount) continue;
                    Offer(project, null);
                }
            }

            if (_candidates.Count == 0) return null;

            // Взвешенный выбор среди кандидатов младших ступеней своих двориков.
            ImprovementDefinition chosen = null;
            Building chosenAnchor = null;
            float totalWeight = 0f;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var (project, building) = _candidates[i];
                if (project.Order != _yardOrder[YardKey(project, building)]) continue;

                float weight = Mathf.Max(0.05f, project.Weight);
                totalWeight += weight;
                if (UnityEngine.Random.value <= weight / totalWeight)
                {
                    chosen = project;
                    chosenAnchor = building;
                }
            }

            anchor = chosenAnchor;
            return chosen;
        }

        /// <summary>Записать кандидата и обновить младшую доступную ступень его дворика.</summary>
        private static void Offer(ImprovementDefinition project, Building building)
        {
            _candidates.Add((project, building));

            object key = YardKey(project, building);
            if (!_yardOrder.TryGetValue(key, out int min) || project.Order < min)
                _yardOrder[key] = project.Order;
        }

        // Заранее упакованные ключи домашних двориков — enum в object-ключе паковался бы
        // на каждый вызов, а буферы выше как раз затем, чтобы выбор не сорил.
        private static readonly object _homeYard = ImprovementAnchor.Home;
        private static readonly object _favouriteYard = ImprovementAnchor.FavouriteSpot;

        /// <summary>
        /// Дворик, в котором соревнуются ступени: экземпляр постройки — или сам якорь
        /// (дом и любимое место — два отдельных «дворика» фермы).
        /// </summary>
        private static object YardKey(ImprovementDefinition project, Building building)
        {
            if (building != null) return building;
            return project.Anchor == ImprovementAnchor.Home ? _homeYard : _favouriteYard;
        }

        /// <summary>
        /// Свободное место у якоря замысла. Чуть в стороне от грядок и вещей: построенное
        /// не должно мешать игроку раскладывать грядки под слияние.
        /// </summary>
        private bool TryFindImprovementSpot(ImprovementDefinition project, Building anchorBuilding, out Vector3 spot)
        {
            Vector3 anchor;
            if (anchorBuilding != null) anchor = anchorBuilding.transform.position;
            else if (project.Anchor == ImprovementAnchor.FavouriteSpot) anchor = _agent.FavouriteSpot;
            else anchor = _agent.HomePosition;

            float min = Mathf.Min(project.AnchorRadius.x, project.AnchorRadius.y);
            float max = Mathf.Max(project.AnchorRadius.x, project.AnchorRadius.y);

            for (int attempt = 0; attempt < 10; attempt++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = UnityEngine.Random.Range(min, max);
                var candidate = anchor + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                var clamped = FarmBounds.ClampToFarm(candidate);
                if ((clamped - candidate).sqrMagnitude > 0.01f) continue;   // упёрлись в границу фермы

                if (!IsSpotClear(candidate, 1.2f)) continue;

                spot = candidate;
                return true;
            }

            spot = default;
            return false;
        }

        private static bool IsSpotClear(Vector3 candidate, float spacing)
        {
            float sqr = spacing * spacing;

            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
            {
                var g = plots[i];
                if (g == null) continue;
                if ((g.transform.position - candidate).sqrMagnitude < sqr) return false;
            }

            var movables = MovableRegistry.All;
            for (int i = 0; i < movables.Count; i++)
            {
                var m = movables[i];
                if (m == null) continue;
                if ((m.transform.position - candidate).sqrMagnitude < sqr) return false;
            }

            return true;
        }

        /// <summary>
        /// Быт. Оценка низкая нарочно — за него он берётся, только когда работы нет. Но без него
        /// «нет работы» выглядит как поломка: персонаж, который в свободную минуту просто бредёт
        /// в случайную точку, читается как не решающий ничего.
        /// </summary>
        private FarmerDecision ScoreRelax()
        {
            float score = ScoreLiving;
            var needs = _agent.Needs;
            var traits = _agent.Traits;

            string thought;
            Vector3 spot;

            if (Night)
            {
                if (_agent.Role == ResidentRole.Watchman)
                {
                    // Дозор сторожа: точка медленно ползёт по кругу — фермер догоняет её,
                    // останавливается «подумать» и идёт дальше: выходит обход с остановками.
                    // Никакого нового состояния: это Relax с маршрутом, случайности в
                    // безделье — жизнь (правило 3), а полоса быта не покидается — та же
                    // надбавка, что у огня.
                    score += 1.1f;

                    const double LegSeconds = 40.0;      // одна «нога» обхода
                    const double FullRound = LegSeconds * 6.0;
                    float angle = (float)(FarmingRuntime.Now % FullRound / FullRound) * Mathf.PI * 2f
                                  + (_agent.name.GetHashCode() & 0xFF) * 0.02f;
                    spot = FarmBounds.ClampToFarm(
                        new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 10f);

                    int leg = (int)(FarmingRuntime.Now / LegSeconds) % 4;
                    thought = leg == 0 ? "всё спокойно"
                            : leg == 1 ? "обойду ещё разок"
                            : leg == 2 ? "тихо на дворе"
                            : "фонари горят ровно";
                }
                else
                {
                    // Ночь ему не принадлежит: работы нет, и посидеть у огня — самое осмысленное.
                    score += 1.1f;
                    spot = FindFireside(out bool found);
                    thought = found ? "посижу у огня" : "тихая ночь";
                    if (!found) spot = _agent.HomePosition;
                }
            }
            else if (DayProgress > 0.85f)
            {
                // Вечер — время возвращаться, даже если ещё светло.
                score += 0.5f;
                spot = _agent.HomePosition;
                thought = "день к концу";
            }
            else if (DayProgress < 0.15f && traits != null && traits.EarlyRiser < 0.4f)
            {
                // Тяжело встаёт: полутра раскачивается у дома вместо работы.
                score += 0.9f;
                spot = _agent.HomePosition;
                thought = "надо раскачаться";
            }
            else if (Resting)
            {
                // Мысль обязана назвать причину. Иначе вставший посреди спелого поля фермер
                // читается как сломавшийся, а не как человек, который решил передохнуть.
                spot = _agent.FavouriteSpot;
                thought = "передохну";
            }
            else if (!_agent.MayHarvest && GrowableRegistry.ReadyCount > 0)
            {
                // Стоять посреди спелого поля без объяснения — читаться сломанным.
                // Названная причина превращает то же безделье в уговор с хозяином.
                spot = _agent.FavouriteSpot;
                thought = "урожай хозяйский — без найма не трону";
            }
            else
            {
                spot = _agent.FavouriteSpot;
                thought = "осмотрюсь";
            }

            // Усталый охотнее присядет.
            if (needs != null) score += (1f - needs.Energy01) * 0.8f;

            return new FarmerDecision(FarmerIntent.Relax, score, thought, spot: spot);
        }

        /// <summary>
        /// Работа мастерового: встать к станку и вести партии, пока они идут.
        /// <para>
        /// Только по найму — неоплаченная роль живёт бытом (демаркация ролей, CLAUDE.md):
        /// это предохранитель от идл-автомата, а не жадность. И только к работающему
        /// станку: стоять над пустым — поза, а не работа; станок сам начнёт партию при
        /// сырье, и мастеровой придёт следом.
        /// </para>
        /// </summary>
        private FarmerDecision ScoreCraft()
        {
            if (_agent.Role != ResidentRole.Craftsman || !_agent.IsHired) return FarmerDecision.None;

            // Перерыв закрывает станок наравне со сбором: распорядок один на все работы.
            if (Resting) return FarmerDecision.None;

            // Ночь — сну, с грузом — сперва доставка: как у всякой работы.
            if (Night || !_agent.Pack.IsEmpty) return FarmerDecision.None;

            Building place = null;
            float bestTravel = float.MaxValue;

            var all = BuildingRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var building = all[i];
                if (building == null) continue;

                var workshop = building.GetComponent<Workshop>();
                if (workshop == null || !workshop.IsWorking) continue;

                float travel = Travel(building.transform.position);
                if (travel >= bestTravel) continue;

                bestTravel = travel;
                place = building;
            }

            if (place == null) return FarmerDecision.None;

            // База 4.3 сидит в полосе работы: выше надоевшего порядка (потолок 3.9) —
            // жалование не тратят на перестановку грядок — и ниже потолка сбора (5.2)
            // с полом еды (5.4): нужды перебивают станок, как и всякую работу.
            float score = ScoreWork + 0.3f - bestTravel * 0.4f;
            if (score <= 0f) return FarmerDecision.None;

            return new FarmerDecision(FarmerIntent.Craft, score, "постругаем", place: place);
        }

        /// <summary>
        /// Работа строителя: встать к стройплощадке. Стройка идёт и без него — он лишь
        /// ускоряет её и даёт что смотреть, тем же приёмом, что мастеровой у станка.
        /// Только по найму: неоплаченная роль живёт бытом.
        /// </summary>
        private FarmerDecision ScoreBuild()
        {
            if (_agent.Role != ResidentRole.Builder || !_agent.IsHired) return FarmerDecision.None;

            // Распорядок один на все работы: перерыв, ночь и груз закрывают стройку.
            if (Resting || Night || !_agent.Pack.IsEmpty) return FarmerDecision.None;

            ConstructionSite best = null;
            float bestTravel = float.MaxValue;

            var all = ConstructionSite.All;
            for (int i = 0; i < all.Count; i++)
            {
                var site = all[i];
                if (site == null) continue;

                float travel = Travel(site.transform.position);
                if (travel >= bestTravel) continue;

                bestTravel = travel;
                best = site;
            }

            if (best == null) return FarmerDecision.None;

            // Та же полоса, что у станка: 4.3 — выше быта, ниже потолка сбора и еды.
            float score = ScoreWork + 0.3f - bestTravel * 0.4f;
            if (score <= 0f) return FarmerDecision.None;

            return new FarmerDecision(FarmerIntent.Build, score, "пойду достраивать", site: best);
        }

        /// <summary>
        /// Ходка возчика: свезти короб сданного заказа к рынку. Чистый театр труда — слот
        /// доски держит сама метка найма, а ходка лишь показывает, за что платится
        /// жалование. Шум вокруг решения игрока (сдачи), никогда вместо него.
        /// </summary>
        private FarmerDecision ScoreCarryOrder()
        {
            if (_agent.Role != ResidentRole.Carter || !_agent.IsHired) return FarmerDecision.None;
            if (_agent.PendingDeliveries <= 0) return FarmerDecision.None;

            // Распорядок и нужды — как у всякой работы.
            if (Resting || Night || !_agent.Pack.IsEmpty) return FarmerDecision.None;

            // Без рынка короб везти некуда — театр молчит, слот доски живёт наймом.
            bool hasMarket = false;
            var all = BuildingRegistry.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Service == BuildingService.Market) { hasMarket = true; break; }
            if (!hasMarket) return FarmerDecision.None;

            // 4.4 в полосе работы: выше быта и порядка, ниже потолка сбора (5.2) и еды (5.4).
            return new FarmerDecision(FarmerIntent.CarryOrder, ScoreWork + 0.4f, "заказ собран — свезу");
        }

        /// <summary>
        /// Навести порядок: поднести грядку к её паре.
        /// <para>
        /// Это про характер, а не про пользу: у фермера есть вкус, и ферма, разложенная его
        /// руками, выглядит обжитой, а не насыпанной случайно. В покое оценка сидит в полосе
        /// быта, но накопленное раздражение тянет её к <see cref="TidyCeiling"/> — выше
        /// ожидания и всё ещё ниже сбора.
        /// </para>
        /// <para>
        /// Он сводит одинаковые уровни вплотную, но сам их не сливает: слияние остаётся ходом
        /// игрока. Он подносит — ты решаешь.
        /// </para>
        /// </summary>
        private FarmerDecision ScoreTidy()
        {
            var traits = _agent.Traits;
            float tidiness = traits != null ? traits.Tidiness : FarmerTraits.Neutral;

            // Неряхе это просто не приходит в голову.
            if (tidiness < 0.15f) return FarmerDecision.None;

            // Копим по самому факту беспорядка, а не по возможности его убрать. Разница
            // не косметическая: спелая грядка и грядка под курсором игрока — это «не
            // сейчас», а не «прибрано», и сброс на каждый такой миг не давал накоплению
            // подняться вообще. Измерено: с памятью 2.96 против 2.90 без неё при потолке 3.9.
            if (!HasMess())
            {
                _messSince = -1.0;   // всё на своих местах — копить нечего
                return FarmerDecision.None;
            }

            double now = FarmingRuntime.Now;
            if (_messSince < 0.0) _messSince = now;

            // Порядок наводят при свете и с пустыми руками — но раздражение копится и пока
            // руки заняты. Бардак не перестаёт быть бардаком оттого, что фермер занят;
            // проверка стоит ПОСЛЕ накопления именно поэтому.
            if (Night || !_agent.Pack.IsEmpty) return FarmerDecision.None;

            // Слот переноски один на ферму (DragFocus), и пока другой житель несёт свою
            // ношу, начинать уборку бессмысленно: дорога кончилась бы молчаливым
            // разворотом у занятого слота (страж в EnterHauling), а молчаливый разворот
            // читается как поломка. Рука игрока сюда не входит: его метка держит только
            // конкретную грядку, и это отдельная проверка в CanTidy.
            if (DragFocus.Current != null && !DragFocus.ByPlayer) return FarmerDecision.None;

            if (!FindTidyJob(out Growable move, out Vector3 spot)) return FarmerDecision.None;

            float score = ScoreLiving + 0.9f + tidiness * 1.5f - Travel(move.transform.position) * 0.8f;
            if (score <= 0f) return FarmerDecision.None;

            // Аккуратный замечает беспорядок вчетверо быстрее неряхи. Характер живёт в том,
            // КОГДА терпение кончится, а не в том, кончится ли: иначе половина фермеров
            // не прибирается никогда, и черта читается как поломка.
            float patience = Mathf.Lerp(MessPatience * 2f, MessPatience * 0.5f, tidiness);

            // Рвение из определения жителя — тем же манером: делит терпение, не двигая
            // потолок. Рьяный дозревает до TidyCeiling раньше, но выше не заберётся.
            patience /= _agent.TidyZeal;
            float nagged = Mathf.Clamp01((float)(now - _messSince) / patience);

            // Тянем к потолку, а не прибавляем к оценке: прибавка сложилась бы со штрафом
            // за дорогу, и надоевший бардак в дальнем углу стал бы важнее ближнего —
            // хотя надоел бардак, а не расстояние.
            score = Mathf.Lerp(score, TidyCeiling, nagged);

            string thought = nagged > 0.75f ? "ну сколько можно" : "приберусь-ка";
            return new FarmerDecision(FarmerIntent.Tidy, score, thought, plot: move, spot: spot);
        }

        /// <summary>
        /// С какого расстояния пара считается стоящей врозь. Одно число на два вопроса —
        /// «есть ли бардак» и «что нести»: разъехавшись, они дали бы вечно копящееся
        /// раздражение при отсутствии работы для него.
        /// </summary>
        private float MessDistance => _agent.TidySpacing * 2.5f;

        /// <summary>
        /// Есть ли на ферме пара одинаковых, стоящая врозь. В отличие от
        /// <see cref="FindTidyJob"/> не спрашивает, можно ли нести прямо сейчас: спелость,
        /// рука игрока и занятое место рядом — это «не сейчас», а не «прибрано».
        /// </summary>
        private bool HasMess()
        {
            var all = GrowableRegistry.All;
            float apart = MessDistance;

            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || a.Phase == GrowthPhase.Empty) continue;

                for (int j = i + 1; j < all.Count; j++)
                {
                    var b = all[j];
                    if (b == null || !b.CanMergeWith(a)) continue;

                    if (Vector3.Distance(a.transform.position, b.transform.position) > apart)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Найти грядку, которую стоит перенести, и куда именно.
        /// Возвращает false, когда всё и так на своих местах.
        /// </summary>
        private bool FindTidyJob(out Growable move, out Vector3 spot)
        {
            move = null;
            spot = default;

            var all = GrowableRegistry.All;
            float bestGain = MessDistance;   // ниже этого возиться не стоит

            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (!CanTidy(candidate)) continue;

                for (int j = 0; j < all.Count; j++)
                {
                    var mate = all[j];
                    if (mate == null || mate == candidate) continue;

                    // Пара — это то, что слилось бы: тот же вид и тот же уровень.
                    if (!mate.CanMergeWith(candidate)) continue;

                    float distance = Vector3.Distance(
                        candidate.transform.position, mate.transform.position);
                    if (distance <= bestGain) continue;

                    if (!FindFreeSpotBeside(mate, candidate, out Vector3 place)) continue;

                    bestGain = distance;
                    move = candidate;
                    spot = place;
                }
            }

            return move != null;
        }

        /// <summary>Можно ли трогать эту грядку.</summary>
        private bool CanTidy(Growable plot)
        {
            if (plot == null || plot.Phase == GrowthPhase.Empty) return false;

            // Спелое сначала собирают, а не носят.
            if (plot.IsReady) return false;

            // Сосед уже идёт за ней или несёт её — вдвоём одну грядку не переставляют.
            if (TidyClaims.HeldByOther(_agent, plot)) return false;

            // Главное правило: не трогать то, что игрок только что поставил сам.
            return !DragFocus.IsPlayerClaimed(plot.transform);
        }

        /// <summary>
        /// Свободное место рядом с <paramref name="mate"/>. Перебираем восемь направлений и берём
        /// первое, где никто не стоит: поставить грядку в другую — значит спрятать одну в другой.
        /// </summary>
        private bool FindFreeSpotBeside(Growable mate, Growable moving, out Vector3 spot)
        {
            float spacing = _agent.TidySpacing;
            Vector3 center = mate.transform.position;

            for (int step = 0; step < 8; step++)
            {
                float angle = step / 8f * Mathf.PI * 2f;
                var candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spacing;
                candidate.y = center.y;
                candidate = FarmBounds.ClampToFarm(candidate);

                if (!IsSpotFree(candidate, spacing * 0.75f, moving, mate)) continue;

                spot = candidate;
                return true;
            }

            spot = default;
            return false;
        }

        private static bool IsSpotFree(Vector3 point, float clearance, Growable ignoreA, Growable ignoreB)
        {
            float sqr = clearance * clearance;

            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                if (plot == null || plot == ignoreA || plot == ignoreB) continue;
                if (Flat(plot.transform.position - point) < sqr) return false;
            }

            var movables = MovableRegistry.All;
            for (int i = 0; i < movables.Count; i++)
            {
                var movable = movables[i];
                if (movable == null) continue;
                if (Flat(movable.transform.position - point) < sqr) return false;
            }

            return true;
        }

        private static float Flat(Vector3 delta)
        {
            delta.y = 0f;
            return delta.sqrMagnitude;
        }

        // ---- вспомогательное ----

        /// <summary>
        /// Доля дистанции от радиуса обзора, без черт и времени суток. Этим меряется
        /// ВИДИМОСТЬ: замечает фермер одинаково при любом характере, дальше — дело цены.
        /// </summary>
        private float TravelRaw(Vector3 target) =>
            Vector3.Distance(_agent.Pos, target) / Mathf.Max(1f, _agent.SearchRadius);

        /// <summary>
        /// Во сколько характер и темнота оценивают каждый метр дороги. Домосед считает её
        /// дороже, а в темноте она растёт у всех, кроме самых смелых. Только вес штрафа:
        /// раньше он сидел и в отсеве видимости, и робкий домосед ночью был слеп на
        /// две трети радиуса — дальние грядки не собирались никогда и незаметно.
        /// </summary>
        private float TravelWeight()
        {
            var traits = _agent.Traits;
            float homebody = traits != null ? traits.Homebody : FarmerTraits.Neutral;
            float courage = traits != null ? traits.NightCourage : FarmerTraits.Neutral;

            float weight = Mathf.Lerp(0.7f, 1.5f, homebody);
            if (Night) weight *= Mathf.Lerp(1.6f, 1f, courage);
            return weight;
        }

        /// <summary>Цена дороги: доля радиуса, взвешенная характером и временем суток.</summary>
        private float Travel(Vector3 target) => TravelRaw(target) * TravelWeight();

        /// <summary>
        /// Насколько ценна грядка, 0..1. Сюда же входит склонность к руде и дереву.
        /// <para>
        /// Сатурация мягкая (gold / (gold + RichHarvest)), а не обрезание по потолку:
        /// жёсткий потолок делал всё дороже 40 золота неразличимым, и с каждым новым
        /// ярусом ресурсов доля «одинаково ценных» грядок росла — лимит, растущий вместе
        /// с контентом. Теперь 40 золота дают 0.5, 400 — 0.9: дорогое различимо всегда.
        /// </para>
        /// </summary>
        private float Value01(Growable plot)
        {
            var definition = plot.Definition;
            var resource = definition != null ? definition.YieldResource : null;
            if (definition == null || resource == null) return 0f;

            float gold = definition.YieldFor(plot.Level) * Mathf.Max(0, resource.SellPrice);

            // Черта обещает «тягу к руде и дереву», но дерево живёт в категории Crop —
            // поэтому склонность меряется съедобностью добычи, а не категорией грядки.
            var traits = _agent.Traits;
            if (traits != null && !resource.IsFood)
                gold *= Mathf.Lerp(0.7f, 1.45f, traits.OreLover);

            return gold / (gold + RichHarvest);
        }

        /// <summary>
        /// Примет ли склад хоть что-то из этого рюкзака. Спрашивается ПЕРЕД дорогой домой:
        /// отказ склада должен останавливать доставку, а не выясняться после неё.
        /// </summary>
        private static bool StorageAccepts(IInventory pack)
        {
            // Отладочный сток не инвентарь и безлимитен — принимает всё.
            if (!(FarmingRuntime.Sink is Inventory storage)) return true;

            var entries = pack.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var resource = entries[i].Resource;
                if (resource != null && storage.FreeUnitsFor(resource) > 0) return true;
            }

            return false;
        }

        private static float Fill01(IInventory pack)
        {
            int free = pack.FreeUnits;
            if (free == int.MaxValue) return 0.5f;

            int capacity = pack.TotalUnits + free;
            return capacity > 0 ? Mathf.Clamp01(pack.TotalUnits / (float)capacity) : 0f;
        }

        private string HarvestThought(Growable plot)
        {
            if (plot.RipeSeconds > LongWait * 0.7f) return "это давно ждёт";
            if (plot.Category == ResourceCategory.Ore) return "загляну в шахту";
            if (plot.Level > 1) return "тут густо уродило";
            return "пойду соберу";
        }

        /// <summary>Ближайший костёр, у которого можно посидеть.</summary>
        private Vector3 FindFireside(out bool found)
        {
            var lamps = MovableRegistry.All;
            Vector3 best = _agent.HomePosition;
            float bestSqr = float.PositiveInfinity;
            found = false;

            for (int i = 0; i < lamps.Count; i++)
            {
                var lamp = lamps[i];
                // Именно в детях: у костра светильник висит на дочернем объекте, и поиск
                // только на самом объекте не нашёл бы ни одного огня.
                if (lamp == null || lamp.GetComponentInChildren<Light>() == null) continue;

                float sqr = (lamp.transform.position - _agent.Pos).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = lamp.transform.position;
                found = true;
            }

            return best;
        }

        private static Building FindMarket()
        {
            var all = BuildingRegistry.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Service == BuildingService.Market) return all[i];
            return null;
        }

        // ---- торговые правила ----

        /// <summary>
        /// Самая крупная стопка, которую стоит продать. У еды запас больше, чем у материалов:
        /// кухня ест из того же склада, и фермер, продавший последнюю пшеницу, только что
        /// сделал себя некормимым.
        /// </summary>
        public bool HasSurplus(out ResourceDefinition resource, out int amount)
        {
            resource = null;
            amount = 0;

            var storage = FarmingRuntime.Sink as IInventory;
            if (storage == null) return false;

            int bestValue = 0;
            var entries = storage.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                var r = entries[i].Resource;
                if (r == null || r.SellPrice <= 0 || !r.FarmerMaySell) continue;

                int keep = r.IsFood ? _agent.KeepFood : _agent.KeepMaterials;
                int spare = entries[i].Amount - keep;
                if (spare < _agent.MinSaleBatch) continue;

                int value = spare * r.SellPrice;
                if (value <= bestValue) continue;

                bestValue = value;
                resource = r;
                amount = spare;
            }

            return resource != null;
        }

        /// <summary>
        /// Лучшая грядка, которую он готов купить себе, или null.
        /// <para>
        /// Два правила не дают его тратам растоптать планы игрока: он никогда не опускается ниже
        /// золотого резерва и берёт только то, что стоит чистое золото — запас материалов игрок
        /// копит на постройки. Постройки он не покупает вовсе: где встанет кухня — решение,
        /// а не рутина.
        /// </para>
        /// </summary>
        public ShopItemDefinition PickRestock()
        {
            var skills = _agent.Skills;
            if (skills == null || !skills.CanRestock) return null;

            // Лимит грядок не запрещает покупки вообще — только новые грядки.
            bool plotsAllowed = GrowableRegistry.Count < _agent.MaxPlots;

            var shop = Shop.Instance;
            var catalog = shop != null ? shop.Catalog : null;
            if (catalog == null) return null;

            int gold = shop.Wallet != null ? shop.Wallet.Gold : 0;

            // Аккуратист, когда ферма уже разведена и денег в достатке, заводит и украшения:
            // обустраивать — это не только переставлять, но и добавлять от себя.
            var traits = _agent.Traits;
            bool wantsDecor = traits != null && traits.Tidiness > 0.6f
                              && gold > _agent.GoldReserve * 2;

            ShopItemDefinition best = null;
            int bestPrice = 0;

            var items = catalog.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;

                bool allowed = (plotsAllowed && item.Kind == ShopItemKind.Plot) ||
                               (wantsDecor && item.Kind == ShopItemKind.Prop);
                if (!allowed) continue;

                var price = item.Price;
                if (price.Resources != null && price.Resources.Length > 0) continue;
                if (gold - price.Gold < _agent.GoldReserve) continue;
                if (!shop.CanBuy(item, out _)) continue;

                // Самое дорогое из посильного: ферма растёт вверх по ступеням, а не вширь
                // одной пшеницей.
                if (best != null && price.Gold <= bestPrice) continue;

                best = item;
                bestPrice = price.Gold;
            }

            return best;
        }

        /// <summary>Осталось ли на рынке хоть какое-то дело.</summary>
        public bool HasMarketErrand() => HasSurplus(out _, out _) || PickRestock() != null;
    }
}

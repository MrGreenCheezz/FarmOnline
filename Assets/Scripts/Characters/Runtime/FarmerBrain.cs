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
        Tidy = 8
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

        /// <summary>Точка для <see cref="FarmerIntent.Relax"/>.</summary>
        public readonly Vector3 Spot;

        /// <summary>Одна строка от первого лица — её показывает пузырь над головой.</summary>
        public readonly string Thought;

        public FarmerDecision(FarmerIntent intent, float score, string thought,
                              Growable plot = null, Building place = null, Vector3 spot = default)
        {
            Intent = intent;
            Score = score;
            Thought = thought;
            Plot = plot;
            Place = place;
            Spot = spot;
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

        /// <summary>Ценность урожая, выше которой прибавка к оценке упирается в потолок.</summary>
        private const float RichHarvest = 40f;

        /// <summary>Сколько секунд ожидания считаются «забыт совсем».</summary>
        private const float LongWait = 90f;

        /// <summary>Насколько скоро грядка должна поспеть, чтобы имело смысл её подождать.</summary>
        private const float WorthWaiting = 14f;

        private readonly FarmerAgent _agent;

        public FarmerBrain(FarmerAgent agent) => _agent = agent;

        /// <summary>Ночь ли сейчас по часам фермы.</summary>
        private static bool Night => DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;

        /// <summary>Какая доля светлого дня прошла, 0..1. Вне дня прижата к краям.</summary>
        private static float DayProgress =>
            DayNightCycle.Instance != null ? DayNightCycle.Instance.DayProgress01 : 0.5f;

        // ---- выбор ----

        /// <summary>Лучшее, чем сейчас можно заняться. Возвращает пустое решение, если ничего не подошло.</summary>
        public FarmerDecision Choose()
        {
            var best = FarmerDecision.None;

            Consider(ScoreSleep(), ref best);
            Consider(ScoreRefresh(), ref best);
            Consider(ScoreDeliver(), ref best);
            Consider(ScoreHarvest(), ref best);
            Consider(ScoreAwait(), ref best);
            Consider(ScoreTrade(), ref best);
            Consider(ScoreTidy(), ref best);
            Consider(ScoreRelax(), ref best);

            return best;
        }

        private static void Consider(in FarmerDecision candidate, ref FarmerDecision best)
        {
            if (candidate.Score > best.Score) best = candidate;
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
            var pack = _agent.Pack;
            if (pack == null || pack.IsFull) return FarmerDecision.None;

            var ready = GrowableRegistry.Ready;
            if (ready.Count == 0) return FarmerDecision.None;

            Growable best = null;
            float bestScore = 0f;

            for (int i = 0; i < ready.Count; i++)
            {
                var plot = ready[i];
                if (plot == null) continue;
                if (_agent.OnlyOwnCategory && plot.Category != _agent.Category) continue;

                float travel = Travel(plot.transform.position);
                if (travel > 1f) continue;   // дальше, чем он вообще смотрит

                float score = ScoreWork
                            + Value01(plot) * 2.6f
                            + Mathf.Clamp01((float)plot.RipeSeconds / LongWait) * 2.2f
                            - travel * 1.8f;

                if (score <= bestScore) continue;

                bestScore = score;
                best = plot;
            }

            if (best == null) return FarmerDecision.None;

            return new FarmerDecision(FarmerIntent.Harvest, bestScore, HarvestThought(best), plot: best);
        }

        /// <summary>
        /// Подождать у грядки, которой осталось несколько секунд. Стоять без дела в двух шагах
        /// от почти созревшего — ровно та мелочь, из-за которой персонаж выглядит бездумным.
        /// </summary>
        private FarmerDecision ScoreAwait()
        {
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

                double left = plot.TimeUntilReady;
                if (left < 0.0 || left > WorthWaiting) continue;

                float travel = Travel(plot.transform.position);
                if (travel > 0.6f) continue;   // ждать имеет смысл только рядом

                // Чем меньше осталось и чем ближе — тем осмысленнее постоять.
                float soon = 1f - (float)(left / WorthWaiting);
                float score = ScoreWork - 0.8f + soon * 1.4f + Value01(plot) * 1.2f - travel * 1.2f;

                if (score <= bestScore) continue;

                bestScore = score;
                best = plot;
            }

            if (best == null) return FarmerDecision.None;

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
            score -= Travel(market.transform.position) * 0.9f;

            return new FarmerDecision(FarmerIntent.Trade, score,
                selling ? "снесу излишки на рынок" : "прикуплю грядку", place: market);
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
                // Ночь ему не принадлежит: работы нет, и посидеть у огня — самое осмысленное.
                score += 1.1f;
                spot = FindFireside(out bool found);
                thought = found ? "посижу у огня" : "тихая ночь";
                if (!found) spot = _agent.HomePosition;
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
        /// Навести порядок: поднести грядку к её паре.
        /// <para>
        /// Это про характер, а не про пользу: у фермера есть вкус, и ферма, разложенная его
        /// руками, выглядит обжитой, а не насыпанной случайно. Оценка держится выше безделья и
        /// ниже любой работы — порядок наводят, когда собирать нечего.
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

            // Порядок наводят при свете и с пустыми руками.
            if (Night || !_agent.Pack.IsEmpty) return FarmerDecision.None;

            if (!FindTidyJob(out Growable move, out Vector3 spot)) return FarmerDecision.None;

            float score = ScoreLiving + 0.9f + tidiness * 1.5f - Travel(move.transform.position) * 0.8f;
            if (score <= 0f) return FarmerDecision.None;

            return new FarmerDecision(FarmerIntent.Tidy, score, "приберусь-ка", plot: move, spot: spot);
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
            float bestGain = _agent.TidySpacing * 2.5f;   // ниже этого возиться не стоит

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
        /// Цена дороги, 0..1 по радиусу обзора. Домосед считает её дороже, а в темноте она
        /// растёт у всех, кроме самых смелых.
        /// </summary>
        private float Travel(Vector3 target)
        {
            float distance = Vector3.Distance(_agent.Pos, target);
            float radius = Mathf.Max(1f, _agent.SearchRadius);

            var traits = _agent.Traits;
            float homebody = traits != null ? traits.Homebody : FarmerTraits.Neutral;
            float courage = traits != null ? traits.NightCourage : FarmerTraits.Neutral;

            float weight = Mathf.Lerp(0.7f, 1.5f, homebody);
            if (Night) weight *= Mathf.Lerp(1.6f, 1f, courage);

            return distance / radius * weight;
        }

        /// <summary>Насколько ценна грядка, 0..1. Сюда же входит склонность к руде.</summary>
        private float Value01(Growable plot)
        {
            var definition = plot.Definition;
            var resource = definition != null ? definition.YieldResource : null;
            if (definition == null || resource == null) return 0f;

            float gold = definition.YieldFor(plot.Level) * Mathf.Max(0, resource.SellPrice);

            var traits = _agent.Traits;
            if (traits != null && plot.Category == ResourceCategory.Ore)
                gold *= Mathf.Lerp(0.7f, 1.45f, traits.OreLover);

            return Mathf.Clamp01(gold / RichHarvest);
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

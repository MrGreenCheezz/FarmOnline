using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Одна грядка / загон / жила: то, что растёт по стадиям и собирается.
    /// <para>
    /// Собственного таймера не держит. Прогресс выводится из таймстампа посадки, поэтому
    /// состояние верно независимо от того, сколько времени прошло между пробуждениями —
    /// грядка, проспавшая четыре стадии, догоняет реальность одним вызовом. Единственная
    /// покадровая работа всей системы происходит в <see cref="GrowthScheduler"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Growable")]
    public sealed class Growable : MonoBehaviour, IGrowthScheduled
    {
        [SerializeField] private GrowableDefinition _definition;

        [Tooltip("Уровень слияния. Два уровня N сливаются в один N+1; уровень масштабирует урожай.")]
        [SerializeField, Min(1)] private int _level = 1;

        [Tooltip("Собственный множитель скорости роста этой грядки. Надбавки построек " +
                 "накладываются сверху и это число не затирают.")]
        [SerializeField, Min(0.01f)] private float _growthSpeed = 1f;

        [SerializeField] private bool _plantOnStart = true;

        private GrowthPhase _phase = GrowthPhase.Empty;
        private int _stageIndex;
        private double _plantedAt;
        private double _readyAt;
        private int _handle = GrowthScheduler.InvalidHandle;

        /// <summary>Скорость грядки без общефермовых надбавок; запоминается при первом включении.</summary>
        private float _ownGrowthSpeed = 1f;
        private bool _ownSpeedCaptured;

        /// <summary>
        /// Полита ли в этом цикле. Флаг, а не множитель, — и это главное решение в поливе.
        /// <para>
        /// Множитель пришлось бы класть в <see cref="_ownGrowthSpeed"/>, а он уезжает в сейв
        /// свободным числом: сервер тогда навсегда теряет право требовать «скорость равна
        /// единице» и не отличит честные 1.25 от 25. Флаг же проверяется точно, а насколько
        /// полив помогает — знает каталог, а не сейв.
        /// </para>
        /// </summary>
        private bool _watered;

        /// <summary>Подкормлена ли в этом цикле. Флагом и по той же причине, что и полив.</summary>
        private bool _fertilized;

        /// <summary>
        /// Сколько циклов подряд грядку поливали — память ухода, из которой растёт качество.
        /// Живёт на грядке, а не на посеве: пересадка её не трогает. Ухоженная земля — свойство
        /// места; это и делает уход вложением, а не расходом.
        /// </summary>
        private int _careStreak;

        /// <summary>
        /// Номер цикла роста, с единицы. Нужен событиям друзей: гость видел снимок, и его
        /// «я полил» может приехать, когда здесь давно другой посев. Uid называет грядку,
        /// номер цикла — тот самый посев; без него чужая помощь ускоряла бы не то, что видела.
        /// </summary>
        private int _cycleId;

        /// <summary>
        /// Стабильное имя грядки между клиентами и сейвами. Позиция и индекс в массиве
        /// ссылками быть не могут: грядку двигают и сливают, — а «друг собрал грядку N»
        /// обязано находить ровно её. Родится лениво, переживает сохранение.
        /// </summary>
        private string _uid;

        // Служебные индексы, которыми владеет GrowableRegistry, — держат его операции O(1).
        internal int RegistryIndex = -1;
        internal int ReadyIndex = -1;

        #region События экземпляра

        /// <summary>Начался новый цикл роста. Поднимается и когда отрастающая грядка перезапускается после сбора.</summary>
        public event Action<Growable> Planted;

        /// <summary>Тик роста: перешла на переданный индекс стадии.</summary>
        public event Action<Growable, int> StageAdvanced;

        /// <summary>Дошла до последней стадии, можно собирать.</summary>
        public event Action<Growable> Ready;

        /// <summary>Собрана; урожай уже в стоке.</summary>
        public event Action<Growable, HarvestResult> Harvested;

        /// <summary>Испортилась, простояв спелой слишком долго.</summary>
        public event Action<Growable> Withered;

        /// <summary>Опустела.</summary>
        public event Action<Growable> Cleared;

        /// <summary>Поднялась на уровень, поглотив переданную грядку — та вот-вот будет уничтожена.</summary>
        public event Action<Growable, Growable> Merged;

        #endregion

        #region Состояние

        /// <summary>Стабильный идентификатор грядки; см. поле <c>_uid</c>.</summary>
        public string Uid
        {
            get
            {
                if (string.IsNullOrEmpty(_uid)) _uid = Guid.NewGuid().ToString("N");
                return _uid;
            }
        }

        public GrowableDefinition Definition => _definition;
        public GrowthPhase Phase => _phase;
        public int StageIndex => _stageIndex;
        public int StageCount => _definition != null ? _definition.StageCount : 0;
        public bool IsReady => _phase == GrowthPhase.Ready;
        public bool IsEmpty => _phase == GrowthPhase.Empty;
        public double PlantedAt => _plantedAt;
        public ResourceCategory Category => _definition != null ? _definition.Category : ResourceCategory.Crop;

        public int Level
        {
            get => _level;
            set => _level = Mathf.Max(1, value);
        }

        /// <summary>
        /// Множитель скорости роста. Смена посреди роста сохраняет уже накопленный прогресс —
        /// перебазируется таймстамп посадки, а не перезапускается грядка.
        /// </summary>
        public float GrowthSpeed
        {
            get => _growthSpeed;
            set
            {
                float v = Mathf.Max(0.01f, value);
                if (Mathf.Approximately(v, _growthSpeed)) return;

                if (_phase == GrowthPhase.Growing)
                {
                    double now = FarmingRuntime.Now;
                    double done = (now - _plantedAt) * _growthSpeed;   // секунды роста, уже заработанные
                    _growthSpeed = v;
                    _plantedAt = now - done / v;
                }
                else
                {
                    _growthSpeed = v;
                }

                ScheduleNext();
            }
        }

        /// <summary>0..1 по всей цепочке — для полосок прогресса.</summary>
        public float Progress01
        {
            get
            {
                if (_phase == GrowthPhase.Ready || _phase == GrowthPhase.Withered) return 1f;
                if (_phase != GrowthPhase.Growing || _definition == null) return 0f;

                double total = _definition.TotalGrowTime;
                if (total <= 0.0) return 1f;
                return Mathf.Clamp01((float)(ElapsedGrowth(FarmingRuntime.Now) / total));
            }
        }

        /// <summary>Реальные секунды до спелости. 0 — уже спелая, -1 — ничего не растёт.</summary>
        public double TimeUntilReady
        {
            get
            {
                if (_phase == GrowthPhase.Ready) return 0.0;
                if (_phase != GrowthPhase.Growing || _definition == null) return -1.0;

                double remaining = _definition.TotalGrowTime - ElapsedGrowth(FarmingRuntime.Now);
                return Math.Max(0.0, remaining / _growthSpeed);
            }
        }

        /// <summary>
        /// Сколько секунд грядка уже стоит спелой. 0, если она не спелая.
        /// <para>
        /// Это память об очереди. Без неё выбор «что собрать» умеет смотреть только на
        /// расстояние, и дальнее не собирается никогда: рядом с домом всегда что-то поспело.
        /// </para>
        /// </summary>
        public double RipeSeconds =>
            _phase == GrowthPhase.Ready ? Math.Max(0.0, FarmingRuntime.Now - _readyAt) : 0.0;

        /// <summary>Полита ли грядка в текущем цикле.</summary>
        public bool Watered => _watered;

        /// <summary>
        /// Можно ли полить прямо сейчас: растёт, в этом цикле ещё не полита — и это растение.
        /// <para>
        /// Только растения, и это не мелочь: тап по растущей корове предлагал «полить» её,
        /// а при пустом колодце корова отвечала «колодец пуст» — игрок читал это как
        /// бессмыслицу, и был прав. Поливают посевы и деревья; у скотины и жил будет
        /// свой уход, когда он придумается, а не позаимствованный у грядок.
        /// </para>
        /// </summary>
        public bool CanWater => _phase == GrowthPhase.Growing && _definition != null && !_watered
                                && Category == ResourceCategory.Crop;

        /// <summary>
        /// Полить: разовое начисление секунд роста, а не множитель скорости.
        /// <para>
        /// Разница принципиальная, а не вкусовая. Множитель применился бы и к оффлайну —
        /// догон отсутствия идёт одним куском (<c>elapsed += offline * speed</c>), и полив
        /// «до конца цикла» молча ускорил бы все восемь часов сна. Тогда ухода за фермой
        /// не возникает вовсе: достаточно полить в момент посадки и уйти.
        /// </para>
        /// <para>
        /// Начисление же исчерпывается там, где сделано. Приём тот же, каким живут
        /// <see cref="StartCycle"/> и <see cref="ForceReady"/>: сдвигаем таймстамп посадки,
        /// а не трогаем скорость.
        /// </para>
        /// </summary>
        /// <param name="cycleFraction">Какую долю полного цикла засчитать. 0.1 — десятую часть.</param>
        public bool TryWater(float cycleFraction)
        {
            if (!CanWater || cycleFraction <= 0f) return false;

            double total = _definition.TotalGrowTime;
            if (total <= 0.0) return false;

            // Делим на скорость: сдвиг живёт в настенных секундах, а начислить надо секунды роста.
            _plantedAt -= total * cycleFraction / _growthSpeed;
            _watered = true;

            double now = FarmingRuntime.Now;
            AdvanceTo(now);
            ScheduleNext();
            return true;
        }

        /// <summary>Подкормлена ли грядка в текущем цикле.</summary>
        public bool Fertilized => _fertilized;

        /// <summary>Потолок ухоженности. Выше него полив всё ещё ускоряет, но качества не копит.</summary>
        public const int CareStreakMax = 9;

        /// <summary>С какого счёта земля «хорошая» (+1 к урожаю).</summary>
        public const int GoodCareStreak = 2;

        /// <summary>С какого — «отборная» (+2 к урожаю).</summary>
        public const int PrimeCareStreak = 5;

        /// <summary>Счёт ухоженных циклов подряд.</summary>
        public int CareStreak => _careStreak;

        /// <summary>Номер текущего цикла роста, с единицы.</summary>
        public int CycleId => _cycleId;

        /// <summary>
        /// Прибавка качества к базовому урожаю, в штуках.
        /// <para>
        /// Целым числом до всех множителей, а не долей после: базовый урожай почти всюду
        /// единица, и любая доля сгорела бы в округлении. «+1 за хорошую землю» к тому же
        /// объяснимо словами — а поведение, которое нельзя объяснить, читается как случайное.
        /// </para>
        /// </summary>
        public int QualityBonus =>
            _careStreak >= PrimeCareStreak ? 2 : _careStreak >= GoodCareStreak ? 1 : 0;

        /// <summary>Можно ли подкормить: растёт, не подкормлена — и это растение, как и полив.</summary>
        public bool CanFertilize => _phase == GrowthPhase.Growing && _definition != null && !_fertilized
                                    && Category == ResourceCategory.Crop;

        /// <summary>
        /// Подкормить. Только отметка: сколько она добавит, решается на сборе — иначе урожай
        /// пришлось бы фиксировать в момент подкормки, и уровень, поднятый слиянием после неё,
        /// пропал бы даром.
        /// </summary>
        public bool TryFertilize()
        {
            if (!CanFertilize) return false;
            _fertilized = true;
            return true;
        }

        /// <summary>Реальные секунды до следующей смены стадии. -1, когда ничего не растёт.</summary>
        public double TimeUntilNextStage
        {
            get
            {
                if (_phase != GrowthPhase.Growing || _definition == null) return -1.0;

                double boundary = _definition.StageStartTime(_stageIndex + 1);
                double remaining = boundary - ElapsedGrowth(FarmingRuntime.Now);
                return Math.Max(0.0, remaining / _growthSpeed);
            }
        }

        #endregion

        #region Жизненный цикл Unity

        private void OnEnable()
        {
            GrowableRegistry.Register(this);

            // Подхватить текущие надбавки сразу: грядка, купленная в ауре уже стоящего ветряка,
            // обязана расти быстро с первой секунды, а не дожидаться следующей постройки.
            if (!_ownSpeedCaptured)
            {
                _ownGrowthSpeed = _growthSpeed;
                _ownSpeedCaptured = true;
            }
            ApplyGrowthBuff(FarmBuffs.GrowthSpeedAt(transform.position, Category));

            var scheduler = GrowthScheduler.Instance;
            if (scheduler != null) _handle = scheduler.Register(this);

            // Пока объект был выключен, время шло — сначала догоняем, потом продолжаем.
            if (_phase == GrowthPhase.Growing) AdvanceTo(FarmingRuntime.Now);
            else if (_phase == GrowthPhase.Ready) GrowableRegistry.SetReady(this, true);

            ScheduleNext();
        }

        private void Start()
        {
            if (_plantOnStart && _phase == GrowthPhase.Empty && _definition != null) Plant();
        }

        private void OnDisable()
        {
            // Existing, а не Instance: нельзя порождать планировщик, пока сцена разбирается.
            var scheduler = GrowthScheduler.Existing;
            if (scheduler != null && _handle != GrowthScheduler.InvalidHandle) scheduler.Unregister(_handle);
            _handle = GrowthScheduler.InvalidHandle;

            GrowableRegistry.Unregister(this);
        }

        #endregion

        #region Публичный API

        /// <summary>Посадить назначенное определение на текущем уровне.</summary>
        public void Plant() => Plant(_definition, _level);

        /// <summary>Посадить <paramref name="definition"/> с первой стадии.</summary>
        public void Plant(GrowableDefinition definition, int level)
        {
            if (definition == null)
            {
                Debug.LogWarning("[Farming] Посадка без GrowableDefinition", this);
                return;
            }

            if (definition.StageCount == 0)
            {
                Debug.LogWarning("[Farming] У '" + definition.Id + "' нет ни одной стадии роста", this);
                return;
            }

            _definition = definition;
            _level = Mathf.Max(1, level);
            StartCycle(0);
        }

        /// <summary>
        /// Собрать, если спелая. Урожай уходит в <see cref="FarmingRuntime.Sink"/>, затем грядка
        /// либо перезапускается (определения с Regrows), либо пустеет.
        /// </summary>
        public bool TryHarvest(out HarvestResult result) => TryHarvest(out result, null);

        /// <summary>
        /// Собрать в конкретный сток. Персонаж, несущий урожай домой, передаёт сюда свой рюкзак,
        /// чтобы урожай попал на склад мира только после настоящей доставки.
        /// </summary>
        /// <param name="into">Приёмник урожая. Null — направить в <see cref="FarmingRuntime.Sink"/>.</param>
        public bool TryHarvest(out HarvestResult result, IResourceSink into)
        {
            result = default;
            if (_phase != GrowthPhase.Ready || _definition == null) return false;

            // Качество земли — по счёту, накопленному прошлыми циклами: этот цикл пойдёт
            // в счёт следующего сбора. Уход — вложение, и платит он со следующего урожая.
            int amount = _definition.YieldFor(_level) + QualityBonus;

            // Подкормка — до аур и целым множителем, а не долей: у почти всех культур базовый
            // урожай равен единице, и любая дробная надбавка сгорела бы в округлении ниже.
            if (_fertilized) amount *= FarmFertilizer.YieldMultiplier;

            // Политый цикл растит счёт, брошенный — снимает одну отметку, а не всё: серия
            // из недели ухода не должна сгорать за один пропущенный вечер.
            _careStreak = _watered
                ? Mathf.Min(CareStreakMax, _careStreak + 1)
                : Mathf.Max(0, _careStreak - 1);

            // Надбавка построек считается здесь, а не у того, кто собирает: урожай обязан быть
            // одним и тем же, снял его фермер днём или игрок кликом ночью. По месту грядки:
            // амбар усиливает то, что стоит рядом с ним, — в этом и есть смысл размещения.
            float yieldBuff = FarmBuffs.HarvestYieldAt(transform.position, Category);
            if (yieldBuff > 1f) amount = Mathf.Max(amount, Mathf.RoundToInt(amount * yieldBuff));

            result = new HarvestResult(this, _definition.YieldResource, amount, _level);

            (into ?? FarmingRuntime.Sink).Add(result.Resource, result.Amount);

            GrowableRegistry.SetReady(this, false);
            Raise(Harvested, result);
            FarmingEvents.RaiseHarvested(this, result);

            if (_definition.Regrows) StartCycle(_definition.RegrowStage);
            else ClearInternal();

            return true;
        }

        /// <summary>Удобная перегрузка для тех, кому детали не нужны.</summary>
        public bool TryHarvest() => TryHarvest(out _);

        /// <summary>Опустошить грядку из любого состояния.</summary>
        public void Clear()
        {
            if (_phase == GrowthPhase.Empty) return;
            GrowableRegistry.SetReady(this, false);
            ClearInternal();
        }

        /// <summary>
        /// Можно ли слить <paramref name="other"/> в эту грядку? Та же культура, тот же уровень,
        /// обе реально посажены — правило, на котором держится вся прогрессия, поэтому оно
        /// живёт здесь, а не в том, что в данный момент таскает объекты.
        /// </summary>
        public bool CanMergeWith(Growable other)
        {
            if (other == null || other == this) return false;
            if (_definition == null || other._definition != _definition) return false;
            if (_level != other._level) return false;
            return _phase != GrowthPhase.Empty && other._phase != GrowthPhase.Empty;
        }

        /// <summary>
        /// Поглотить <paramref name="other"/>: эта грядка растёт на уровень, вторая уничтожается.
        /// <para>
        /// Прогресс роста намеренно сохраняется, а не сбрасывается. Слияние задумано как чистый
        /// выигрыш — брать за него плату в виде нового цикла роста значило бы превратить главное
        /// действие игры в откат. Если балансу позже понадобится цена, менять нужно ровно тут.
        /// </para>
        /// </summary>
        public bool TryMergeWith(Growable other)
        {
            if (!CanMergeWith(other)) return false;

            _level++;

            // Ухоженность переживает слияние лучшей из двух: сливая выхоженную грядку
            // с запущенной, игрок не теряет вложенный уход — иначе слияние и уход спорили бы
            // друг с другом, а они обязаны складываться.
            _careStreak = Mathf.Max(_careStreak, other._careStreak);

            Raise(Merged, other);
            FarmingEvents.RaiseMerged(this, other);

            other.Clear();
            Destroy(other.gameObject);
            return true;
        }

        /// <summary>
        /// Наложить общефермовую надбавку поверх собственной скорости грядки.
        /// Зовёт <see cref="FarmBuffs"/>, когда набор построек изменился; прогресс роста
        /// при этом сохраняется — за это отвечает сеттер <see cref="GrowthSpeed"/>.
        /// </summary>
        internal void ApplyGrowthBuff(float multiplier)
        {
            if (!_ownSpeedCaptured)
            {
                _ownGrowthSpeed = _growthSpeed;
                _ownSpeedCaptured = true;
            }

            GrowthSpeed = _ownGrowthSpeed * Mathf.Max(0.01f, multiplier);
        }

        // ---- сохранение ----

        /// <summary>
        /// Снять состояние роста для сохранения. Время меряется <b>наработанными секундами
        /// роста</b>, а не таймстампом: <see cref="FarmingRuntime.Now"/> отсчитывается от запуска
        /// приложения, и сохранённый таймстамп в новой сессии означал бы совсем другой момент.
        /// </summary>
        public void CaptureState(out double elapsedGrowth, out double ripeSeconds, out float ownGrowthSpeed)
        {
            elapsedGrowth = _phase == GrowthPhase.Growing
                ? ElapsedGrowth(FarmingRuntime.Now)
                : (_definition != null ? _definition.TotalGrowTime : 0.0);

            ripeSeconds = RipeSeconds;
            ownGrowthSpeed = _ownGrowthSpeed;
        }

        /// <summary>
        /// Вернуть отметку полива из сохранения. Отдельным вызовом, а не параметром
        /// <see cref="RestoreState"/>: секунды роста полив уже отдал — в сейве лежит их сумма,
        /// — и восстанавливать надо не эффект, а только запрет полить второй раз за цикл.
        /// </summary>
        public void RestoreCare(bool watered, bool fertilized, int careStreak, int cycleId)
        {
            _watered = watered;
            _fertilized = fertilized;
            _careStreak = Mathf.Clamp(careStreak, 0, CareStreakMax);

            // Восстановление проходит через StartCycle и уже накрутило счётчик — возвращаем
            // сохранённый, иначе каждый вход в игру «пересаживал» бы все грядки для событий друзей.
            if (cycleId > 0) _cycleId = cycleId;
        }

        /// <summary>
        /// Восстановить рост из сохранения.
        /// <para>
        /// Нарочно тихо: событий <see cref="Planted"/> и <see cref="Ready"/> не поднимает —
        /// иначе загрузка фермы с двумя десятками грядок обернулась бы залпом звуков посадки
        /// и вспышек созревания. Поднимается только смена стадии: по ней обновляется меш,
        /// а отклик на неё никто не вешает.
        /// </para>
        /// </summary>
        /// <param name="offlineSeconds">
        /// Сколько настенных секунд ферма прожила закрытой. Пересчёт в секунды роста происходит
        /// именно здесь, а не у вызывающего: только грядка знает свою итоговую скорость с аурами.
        /// </param>
        public void RestoreState(GrowableDefinition definition, int level, bool ready,
                                 double elapsedGrowth, double ripeSeconds, float ownGrowthSpeed,
                                 double offlineSeconds = 0.0, string uid = null)
        {
            if (definition == null || definition.StageCount == 0) return;

            // Идентичность — из сейва: под этим именем грядку знают события друзей.
            if (!string.IsNullOrEmpty(uid)) _uid = uid;

            _definition = definition;
            _level = Mathf.Max(1, level);

            _ownGrowthSpeed = Mathf.Max(0.01f, ownGrowthSpeed);
            _ownSpeedCaptured = true;
            _growthSpeed = _ownGrowthSpeed * Mathf.Max(0.01f, FarmBuffs.GrowthSpeedAt(transform.position, Category));

            // Оффлайн-догон: закрытая игра не тикала, но время шло. Спелая копит «спелые» секунды,
            // растущая — секунды роста; дозревшая за отлучку станет Ready ниже обычным путём.
            if (offlineSeconds > 0.0)
            {
                if (ready) ripeSeconds += offlineSeconds;
                else elapsedGrowth += offlineSeconds * _growthSpeed;
            }

            double now = FarmingRuntime.Now;
            _plantedAt = now - Math.Max(0.0, elapsedGrowth) / _growthSpeed;
            _phase = GrowthPhase.Growing;
            _stageIndex = Mathf.Clamp(definition.StageAtElapsed(elapsedGrowth), 0, definition.LastStageIndex);

            if (ready || _stageIndex >= definition.LastStageIndex)
            {
                _stageIndex = definition.LastStageIndex;
                _phase = GrowthPhase.Ready;
                _readyAt = now - Math.Max(0.0, ripeSeconds);
                GrowableRegistry.SetReady(this, true);
            }
            else
            {
                GrowableRegistry.SetReady(this, false);
            }

            Raise(StageAdvanced, _stageIndex);
            FarmingEvents.RaiseStageAdvanced(this, _stageIndex);
            ScheduleNext();
        }

        /// <summary>Перескочить сразу к спелости. Для бустеров, читов и тестов.</summary>
        public void ForceReady()
        {
            if (_definition == null || _phase == GrowthPhase.Ready) return;
            if (_phase == GrowthPhase.Empty || _phase == GrowthPhase.Withered) StartCycle(0);

            double now = FarmingRuntime.Now;
            _plantedAt = now - _definition.TotalGrowTime / _growthSpeed;
            AdvanceTo(now);
        }

        #endregion

        #region Внутренности

        private double ElapsedGrowth(double now) => (now - _plantedAt) * _growthSpeed;

        /// <summary>Начать цикл роста со стадии <paramref name="fromStage"/> (0 — заново, больше — отрастание).</summary>
        private void StartCycle(int fromStage)
        {
            // Пересадка спелой грядки публичным Plant не проходит через TryHarvest/Clear,
            // и без снятия флага в Ready-списке оставался призрак: мозг фермера выбирал
            // его, агент приходил, разворачивался — и выбирал снова.
            GrowableRegistry.SetReady(this, false);

            int stage = Mathf.Clamp(fromStage, 0, _definition.LastStageIndex);
            double now = FarmingRuntime.Now;

            // Новый цикл — новая жажда. Без этих двух строк отрастающая культура (Regrows)
            // осталась бы политой и подкормленной навсегда после первого же сбора, и разовый
            // уход превратился бы в постоянную прибавку, которую никто не покупал.
            _watered = false;
            _fertilized = false;
            _cycleId++;

            // Перебазируем таймстамп так, чтобы «прошло» уже покрывало пропускаемые стадии.
            _plantedAt = now - _definition.StageStartTime(stage) / _growthSpeed;
            _stageIndex = stage;
            _phase = GrowthPhase.Growing;

            Raise(Planted);
            FarmingEvents.RaisePlanted(this);

            if (stage >= _definition.LastStageIndex) EnterReady(now);
            ScheduleNext();
        }

        /// <summary>Догнать грядку до состояния, которое диктует <paramref name="now"/>.</summary>
        private void AdvanceTo(double now)
        {
            if (_definition == null || _phase != GrowthPhase.Growing) return;

            int target = _definition.StageAtElapsed(ElapsedGrowth(now));
            int last = _definition.LastStageIndex;

            while (_stageIndex < target)
            {
                _stageIndex++;
                Raise(StageAdvanced, _stageIndex);
                FarmingEvents.RaiseStageAdvanced(this, _stageIndex);

                if (_stageIndex >= last)
                {
                    EnterReady(now);
                    return;
                }
            }
        }

        private void EnterReady(double now)
        {
            _phase = GrowthPhase.Ready;
            _stageIndex = _definition.LastStageIndex;

            // Точный момент созревания, а не момент, когда мы заметили, — держит порчу честной.
            _readyAt = _plantedAt + _definition.TotalGrowTime / _growthSpeed;
            if (_readyAt > now) _readyAt = now;

            GrowableRegistry.SetReady(this, true);
            Raise(Ready);
            FarmingEvents.RaiseReady(this);
        }

        private void Wither()
        {
            _phase = GrowthPhase.Withered;
            GrowableRegistry.SetReady(this, false);

            Raise(Withered);
            FarmingEvents.RaiseWithered(this);

            ClearInternal();
        }

        private void ClearInternal()
        {
            bool remove = _definition != null && _definition.RemoveWhenEmpty;

            _phase = GrowthPhase.Empty;
            _stageIndex = 0;
            CancelWakeUp();

            Raise(Cleared);
            FarmingEvents.RaiseCleared(this);

            // Иначе на поле копятся невидимые пустые грядки: они остаются в реестре, носят
            // плашку уровня и перехватывают клики, хотя для игрока их уже нет.
            if (remove && Application.isPlaying) Destroy(gameObject);
        }

        /// <summary>Попросить планировщик разбудить нас в следующий момент, когда что-то реально изменится.</summary>
        private void ScheduleNext()
        {
            var scheduler = GrowthScheduler.Instance;
            if (scheduler == null || _handle == GrowthScheduler.InvalidHandle) return;

            if (_phase == GrowthPhase.Growing && _definition != null)
            {
                double boundary = _definition.StageStartTime(_stageIndex + 1);
                scheduler.Schedule(_handle, _plantedAt + boundary / _growthSpeed);
                return;
            }

            if (_phase == GrowthPhase.Ready && _definition != null && _definition.WitherAfter > 0f)
            {
                scheduler.Schedule(_handle, _readyAt + _definition.WitherAfter);
                return;
            }

            scheduler.Cancel(_handle);
        }

        private void CancelWakeUp()
        {
            var scheduler = GrowthScheduler.Instance;
            if (scheduler != null && _handle != GrowthScheduler.InvalidHandle) scheduler.Cancel(_handle);
        }

        void IGrowthScheduled.OnScheduledDue(double now)
        {
            switch (_phase)
            {
                case GrowthPhase.Growing:
                    AdvanceTo(now);
                    ScheduleNext();
                    break;

                case GrowthPhase.Ready:
                    if (_definition == null || _definition.WitherAfter <= 0f) break;

                    // Под пугалом спелое не портится. Проверка в момент, когда пора вянуть,
                    // а не при созревании: пугало могли поставить или унести, пока стояло.
                    // Защищённое откладывает вопрос ещё на один срок — унесут пугало,
                    // и таймер порчи честно пойдёт заново.
                    if (FarmBuffs.WitherGuardedAt(transform.position, Category))
                    {
                        var scheduler = GrowthScheduler.Instance;
                        if (scheduler != null && _handle != GrowthScheduler.InvalidHandle)
                            scheduler.Schedule(_handle, now + _definition.WitherAfter);
                        break;
                    }

                    Wither();
                    break;
            }
        }

        // Исключение подписчика не должно ломать грядку, поднявшую событие.
        private void Raise(Action<Growable> handler)
        {
            if (handler == null) return;
            try { handler(this); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void Raise(Action<Growable, int> handler, int arg)
        {
            if (handler == null) return;
            try { handler(this, arg); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void Raise(Action<Growable, Growable> handler, Growable arg)
        {
            if (handler == null) return;
            try { handler(this, arg); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void Raise(Action<Growable, HarvestResult> handler, in HarvestResult arg)
        {
            if (handler == null) return;
            try { handler(this, arg); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        #endregion
    }
}

using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    public enum FarmerState
    {
        /// <summary>Стоит между делами.</summary>
        Idle = 0,
        /// <summary>Бредёт к случайной точке вокруг дома.</summary>
        Wandering = 1,
        /// <summary>Идёт к спелой грядке, которую застолбил.</summary>
        GoingToHarvest = 2,
        /// <summary>Стоит у грядки, работает.</summary>
        Harvesting = 3,
        /// <summary>Несёт груз обратно к дому.</summary>
        ReturningHome = 4,
        /// <summary>Дома, выгружается в склад мира.</summary>
        Depositing = 5,
        /// <summary>Идёт к кухне или колодцу — просела нужда.</summary>
        GoingToService = 6,
        /// <summary>Ест или пьёт.</summary>
        UsingService = 7,
        /// <summary>Идёт на рынок — продать, купить или и то и другое.</summary>
        GoingToMarket = 8,
        /// <summary>Стоит на рынке, ведёт дела.</summary>
        AtMarket = 9,
        /// <summary>Спит — ночь или вымотался.</summary>
        Sleeping = 10,
        /// <summary>Короткая пауза: решение принято, но ещё не начато.</summary>
        Thinking = 11,
        /// <summary>Быт: сидит у костра, стоит дома, осматривается.</summary>
        Relaxing = 12,
        /// <summary>Ждёт у грядки, которой осталось несколько секунд.</summary>
        Awaiting = 13,
        /// <summary>Идёт за грядкой, которую решил переставить.</summary>
        GoingToTidy = 14,
        /// <summary>Несёт грядку на новое место.</summary>
        Hauling = 15
    }

    /// <summary>
    /// Простое чувство, которое фермер показывает облачком над головой.
    /// <para>
    /// Нарочно короткий список и только про быт: облачко должно ловиться боковым зрением
    /// и мгновенно читаться символом. Как только сюда попадут сложные темы, символ придётся
    /// заменить текстом, а текст над головой перестаёт замечаться и превращается в шум.
    /// </para>
    /// </summary>
    public enum FarmerEmote
    {
        None = 0,
        /// <summary>Проголодался.</summary>
        Food = 1,
        /// <summary>Хочет пить.</summary>
        Drink = 2,
        /// <summary>Вымотался, идёт спать.</summary>
        Sleep = 3,
        /// <summary>Всё хорошо — мурлычет себе под нос.</summary>
        Happy = 4
    }

    /// <summary>
    /// Фермер: конечный автомат «что я делаю сейчас». Что делать дальше — решает
    /// <see cref="FarmerBrain"/>, и это разделение здесь главное.
    /// <para>
    /// Раньше обе половины жили одной лестницей <c>if</c>: порядок всегда один, ничто ни с чем
    /// не взвешивается, передумать нельзя. Каждый новый мотив становился ещё одной веткой в той
    /// же лестнице, а зритель видел не выбор, а исполнение чеклиста.
    /// </para>
    /// <para>
    /// Решение к тому же телеграфируется: перед новым делом он на мгновение замирает и над ним
    /// появляется мысль. Без этого любая умная система читается как случайность — игрок должен
    /// видеть сам выбор, а не только его последствия.
    /// </para>
    /// <para>
    /// Он никогда не сканирует сцену. Спелые грядки приходят из <see cref="GrowableRegistry"/>,
    /// поэтому цена поиска работы пропорциональна количеству работы, а не размеру фермы.
    /// Урожай попадает в рюкзак у самой грядки и доходит до склада только после того, как
    /// персонаж физически дошёл домой.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Farmer Agent")]
    public sealed class FarmerAgent : MonoBehaviour
    {
        [Header("Дом")]
        [Tooltip("Куда носить урожай. Пусто — берётся стартовая позиция персонажа.")]
        [SerializeField] private Transform _home;

        [Header("Блуждание")]
        [Tooltip("Радиус прогулок вокруг дома.")]
        [SerializeField, Min(0.5f)] private float _wanderRadius = 8f;
        [SerializeField, Min(0f)] private float _idlePauseMin = 0.5f;
        [SerializeField, Min(0f)] private float _idlePauseMax = 2f;

        [Header("Работа")]
        [Tooltip("Насколько далеко персонаж замечает созревший ресурс.")]
        [SerializeField, Min(1f)] private float _searchRadius = 25f;
        [Tooltip("Сколько секунд занимает сбор.")]
        [SerializeField, Min(0f)] private float _harvestDuration = 0.8f;
        [Tooltip("Сколько секунд занимает выгрузка дома.")]
        [SerializeField, Min(0f)] private float _depositDuration = 0.5f;

        [Tooltip("Как часто пересматривать решение, секунд. Чаще — отзывчивее, но и дороже.")]
        [SerializeField, Min(0.05f)] private float _scanInterval = 0.25f;

        [Tooltip("Сколько он «думает» перед новым делом. Пауза нужна не ему, а игроку: без неё " +
                 "решение не видно — персонаж просто оказывается идущим куда-то.")]
        [SerializeField, Min(0f)] private float _thinkDuration = 0.45f;

        [Tooltip("Насколько новое дело должно быть лучше текущего, чтобы он передумал на ходу. " +
                 "Ноль заставит его дёргаться между двумя равными грядками.")]
        [SerializeField, Min(0f)] private float _switchMargin = 1.5f;

        [Header("Что собирать")]
        [SerializeField] private bool _filterByCategory;
        [SerializeField] private ResourceCategory _category = ResourceCategory.Crop;

        [Header("Торговля")]
        [Tooltip("Сколько единиц ресурса оставлять на складе, не продавая. Еду держим про запас: " +
                 "распродать её подчистую — значит остаться без кухни.")]
        [SerializeField, Min(0)] private int _keepFood = 25;

        [Tooltip("Сколько оставлять от несъедобного. Оно нужно только на постройки, " +
                 "поэтому запас заметно больше минимальной партии.")]
        [SerializeField, Min(0)] private int _keepMaterials = 45;

        [Tooltip("Меньше этого на продажу не ходит — не стоит дороги.")]
        [SerializeField, Min(1)] private int _minSaleBatch = 10;

        [Tooltip("Излишек, при котором торговля становится срочной.")]
        [SerializeField, Min(1)] private int _urgentSurplus = 60;

        [Header("Закупка")]
        [Tooltip("Сколько золота фермер не тронет ни при каких условиях.\n" +
                 "Это граница между его самостоятельностью и планами игрока: без запаса он спустит " +
                 "на грядки те деньги, что копились на постройку.")]
        [SerializeField, Min(0)] private int _goldReserve = 400;

        [Tooltip("Сколько всего грядок он готов развести. Дальше расширяет ферму только игрок.")]
        [SerializeField, Min(1)] private int _maxPlots = 40;

        [Tooltip("Сколько секунд занимает торг.")]
        [SerializeField, Min(0f)] private float _sellDuration = 1.2f;

        [Header("Отдых")]
        [Tooltip("Ложится ли спать с наступлением ночи, даже если ещё бодр.")]
        [SerializeField] private bool _sleepAtNight = true;

        [Tooltip("Как быстро осматривается, стоя без дела, градусов в секунду.")]
        [SerializeField, Min(0f)] private float _lookAroundSpeed = 22f;

        [Header("Облачко над головой")]
        [Tooltip("Не показывать облачко чаще, чем раз в столько секунд.\n" +
                 "Смысл именно в редкости: то, что висит над головой постоянно, перестают " +
                 "замечать, и символ превращается в часть силуэта.")]
        [SerializeField, Min(0f)] private float _emoteCooldown = 25f;

        [Tooltip("Через сколько секунд он может замурлыкать, когда всё хорошо: от и до.")]
        [SerializeField] private Vector2 _hummingInterval = new Vector2(70f, 160f);

        [Tooltip("Насколько хорошо должно быть, чтобы замурлыкать.")]
        [SerializeField, Range(0f, 1f)] private float _hummingWellbeing = 0.75f;

        [Header("Обустройство")]
        [Tooltip("На каком расстоянии он ставит грядку рядом с её парой.\n" +
                 "Он сводит одинаковые вплотную, но не сливает: слияние остаётся ходом игрока.")]
        [SerializeField, Min(0.4f)] private float _tidySpacing = 1.3f;

        [Tooltip("На какой высоте несёт груз.")]
        [SerializeField, Min(0f)] private float _carryHeight = 0.55f;

        [Tooltip("Как далеко перед собой держит несомое.")]
        [SerializeField, Min(0f)] private float _carryReach = 0.55f;

        private AgentMover _mover;
        private CharacterInventory _inventoryComponent;
        private CharacterNeeds _needs;
        private FarmerSkills _skills;
        private FarmerTraits _traits;
        private FarmerBrain _brain;

        private FarmerState _state = FarmerState.Idle;
        private FarmerDecision _pending;
        private FarmerDecision _current;
        private float _currentScore;

        private Growable _target;
        private Building _serviceTarget;
        private Building _market;
        private Vector3 _relaxSpot;
        private Vector3 _tidySpot;
        private Vector3 _homePosition;
        private Vector3 _favouriteSpot;
        private string _thought = "";

        private float _timer;
        private float _scanTimer;
        private float _emoteTimer;
        private float _hummingTimer;
        private float _baseSpeed = 1.5f;
        private int _baseCapacity = 8;
        private int _plotsThisTrip;
        private Vector3 _lastPosition;

        /// <summary>Сменил занятие — удобно для анимации и UI.</summary>
        public event Action<FarmerAgent, FarmerState> StateChanged;

        /// <summary>Показал чувство над головой. Редкое событие — см. <see cref="_emoteCooldown"/>.</summary>
        public event Action<FarmerAgent, FarmerEmote> Emoted;

        /// <summary>Что-то подобрал. Это в рюкзаке, ещё не на складе.</summary>
        public event Action<FarmerAgent, HarvestResult> Collected;

        /// <summary>Выгрузился дома. Число — сколько единиц сдано складу мира.</summary>
        public event Action<FarmerAgent, int> Delivered;

        /// <summary>Поел или попил у постройки.</summary>
        public event Action<FarmerAgent, Building> Refreshed;

        /// <summary>Сам продал на рынке. Число — принесённое золото.</summary>
        public event Action<FarmerAgent, int> Traded;

        /// <summary>Купил себе новую грядку на выручку.</summary>
        public event Action<FarmerAgent, ShopItemDefinition> Restocked;

        public FarmerState State => _state;
        public Growable Target => _target;
        public Vector3 HomePosition => _homePosition;

        /// <summary>Его навыки, или null без компонента — всё деградирует к уровню 1.</summary>
        public FarmerSkills Skills => _skills;

        /// <summary>Его характер, или null — тогда все черты нейтральны.</summary>
        public FarmerTraits Traits => _traits;

        /// <summary>
        /// О чём он думает прямо сейчас, одной строкой от первого лица. Показывается в панели
        /// HUD, а не над головой: в мире висит только редкий символ, иначе текст становится шумом.
        /// </summary>
        public string Thought => _thought;

        /// <summary>Истина, пока он спит. HUD по этому флагу приглушает себя.</summary>
        public bool IsAsleep => _state == FarmerState.Sleeping;

        /// <summary>
        /// Самочувствие одним словом. Анимаций под настроение пока нет, поэтому оно читается
        /// подписью — но читается, а это и была задача.
        /// </summary>
        public string Mood
        {
            get
            {
                if (_state == FarmerState.Sleeping) return "спит";
                if (_needs == null) return "";
                if (_needs.IsTired) return "вымотан";
                if (_needs.IsHungry) return "голоден";
                if (_needs.IsThirsty) return "хочет пить";
                return _needs.Wellbeing01 > 0.8f ? "бодр" : "в порядке";
            }
        }

        /// <summary>
        /// Что фермер несёт. Ёмкость живёт на <see cref="CharacterInventory"/>.
        /// <para>
        /// Достаётся по требованию, а не только в <c>Awake</c>: HUD на другом объекте может
        /// обратиться сюда из своего <c>OnEnable</c>, который Unity может выполнить раньше нашего.
        /// </para>
        /// </summary>
        public IInventory Inventory
        {
            get
            {
                if (_inventoryComponent == null) EnsureInventory();
                return _inventoryComponent.Inventory;
            }
        }

        public bool IsCarrying => !Inventory.IsEmpty;

        // ---- то, что читает мозг ----

        internal Vector3 Pos => transform.position;
        internal IInventory Pack => Inventory;
        internal CharacterNeeds Needs => _needs;
        internal Vector3 FavouriteSpot => _favouriteSpot;
        internal float SearchRadius => _searchRadius;
        internal bool SleepsAtNight => _sleepAtNight;
        internal bool OnlyOwnCategory => _filterByCategory;
        internal ResourceCategory Category => _category;
        internal int KeepFood => _keepFood;
        internal int KeepMaterials => _keepMaterials;
        internal int MinSaleBatch => _minSaleBatch;
        internal int UrgentSurplus => _urgentSurplus;
        internal int GoldReserve => _goldReserve;
        internal int MaxPlots => _maxPlots;
        internal float TidySpacing => _tidySpacing;

        #region Жизненный цикл

        private void Awake()
        {
            _mover = GetComponent<AgentMover>();
            if (_mover == null)
                Debug.LogError("[Farmer] Нужен компонент AgentMover (например SimpleMover)", this);

            EnsureInventory();

            _needs = GetComponent<CharacterNeeds>();
            _skills = GetComponent<FarmerSkills>();
            _traits = GetComponent<FarmerTraits>();
            _brain = new FarmerBrain(this);

            if (_mover != null) _baseSpeed = _mover.Speed;
            _baseCapacity = _inventoryComponent.Capacity;

            if (_skills != null) _skills.LevelledUp += OnLevelledUp;

            if (_needs != null)
            {
                _needs.BecameHungry += OnBecameHungry;
                _needs.BecameThirsty += OnBecameThirsty;
                _needs.BecameTired += OnBecameTired;
            }
        }

        private void OnDisable()
        {
            // Выключили посреди переноски — груз должен остаться на земле, а не зависнуть.
            if (_state == FarmerState.Hauling && _target != null)
            {
                var dropped = _target.transform.position;
                dropped.y = FarmingRuntime.Ground.SampleHeight(dropped);
                _target.transform.position = dropped;
            }

            Release();
        }

        private void OnDestroy()
        {
            if (_skills != null) _skills.LevelledUp -= OnLevelledUp;

            if (_needs != null)
            {
                _needs.BecameHungry -= OnBecameHungry;
                _needs.BecameThirsty -= OnBecameThirsty;
                _needs.BecameTired -= OnBecameTired;
            }
        }

        private void Start()
        {
            _homePosition = _home != null ? _home.position : transform.position;
            _lastPosition = transform.position;

            // Любимое место — своё у каждого работника и неизменное от запуска к запуску.
            // Мелочь, но именно из таких мелочей складывается «он тут живёт».
            var random = new System.Random(name.GetHashCode());
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float radius = _wanderRadius * 0.7f;
            _favouriteSpot = _homePosition + new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            // Со случайной задержки, а не с нуля: иначе он мурлычет на первом же кадре партии
            // и занятым кулдауном глушит первое же настоящее «проголодался».
            _hummingTimer = UnityEngine.Random.Range(
                _hummingInterval.x, Mathf.Max(_hummingInterval.x, _hummingInterval.y));

            ApplyCapacity();
            EnterIdle();
        }

        /// <summary>Ёмкость выводится заново, а не инкрементируется, — переживает перезагрузку без потерь.</summary>
        private void OnLevelledUp(FarmerSkills skills, FarmerSkill skill, int level)
        {
            if (skill == FarmerSkill.Back) ApplyCapacity();
        }

        private void ApplyCapacity()
        {
            if (_inventoryComponent == null) return;
            _inventoryComponent.Capacity = _baseCapacity + (_skills != null ? _skills.ExtraCapacity : 0);
        }

        private void EnsureInventory()
        {
            _inventoryComponent = GetComponent<CharacterInventory>();
            if (_inventoryComponent == null) _inventoryComponent = gameObject.AddComponent<CharacterInventory>();
        }

        private void Update()
        {
            if (_mover == null) return;

            // Голод, жажда и усталость бьют по скорости. Не по «жив/мёртв» — иначе игрок теряет
            // партию, ничего не в силах исправить. Навык ног работает поверх штрафа.
            float skillSpeed = _skills != null ? _skills.MoveSpeed : 1f;
            _mover.Speed = _baseSpeed * (_needs != null ? _needs.Productivity01 : 1f) * skillSpeed;

            TrackEffort();
            TickEmotes();

            switch (_state)
            {
                case FarmerState.Idle: TickIdle(); break;
                case FarmerState.Wandering: TickWandering(); break;
                case FarmerState.Thinking: TickThinking(); break;
                case FarmerState.Relaxing: TickRelaxing(); break;
                case FarmerState.Awaiting: TickAwaiting(); break;
                case FarmerState.GoingToTidy: TickGoingToTidy(); break;
                case FarmerState.Hauling: TickHauling(); break;
                case FarmerState.GoingToHarvest: TickGoingToHarvest(); break;
                case FarmerState.Harvesting: TickHarvesting(); break;
                case FarmerState.ReturningHome: TickReturningHome(); break;
                case FarmerState.Depositing: TickDepositing(); break;
                case FarmerState.GoingToService: TickGoingToService(); break;
                case FarmerState.UsingService: TickUsingService(); break;
                case FarmerState.GoingToMarket: TickGoingToMarket(); break;
                case FarmerState.AtMarket: TickAtMarket(); break;
                case FarmerState.Sleeping: TickSleeping(); break;
            }
        }

        /// <summary>
        /// Превращает движение в усталость и в опыт ног.
        /// <para>
        /// Меряется по реально пройденному расстоянию, а не по времени в «ходячем» состоянии:
        /// фермер, упёршийся в забор, не должен крепнуть и выматываться за стояние на месте.
        /// </para>
        /// </summary>
        private void TrackEffort()
        {
            Vector3 position = transform.position;
            float moved = Vector3.Distance(position, _lastPosition);
            _lastPosition = position;

            if (_needs != null)
                _needs.Exertion = _state == FarmerState.Sleeping ? 0f : (moved > 0.001f ? 1f : 0.35f);

            if (_skills != null && moved > 0f && _state != FarmerState.Sleeping)
                _skills.Grant(FarmerSkill.Legs, moved * 0.25f);
        }

        #endregion

        #region Решение

        /// <summary>
        /// Спросить мозг и, если нашлось дело лучше текущего, начать его.
        /// <paramref name="margin"/> — насколько новое должно превосходить нынешнее: на ходу
        /// без запаса он дёргался бы между двумя одинаковыми грядками каждую четверть секунды.
        /// </summary>
        private bool Rethink(float margin = 0f)
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f) return false;
            _scanTimer = _scanInterval;

            var decision = _brain.Choose();
            if (!decision.IsSomething) return false;

            // Уже занят ровно этим. Без такой проверки он каждую четверть секунды заново
            // «решал» продолжать начатое: оценка чуть подросла — и он снова замирал думать,
            // так и не сходя с места.
            if (IsSameAsCurrent(decision)) return false;

            if (decision.Score <= _currentScore + margin) return false;

            BeginThinking(decision);
            return true;
        }

        /// <summary>То же самое дело с тем же объектом, что он делает прямо сейчас?</summary>
        private bool IsSameAsCurrent(in FarmerDecision decision)
        {
            if (decision.Intent != _current.Intent) return false;

            switch (decision.Intent)
            {
                case FarmerIntent.Harvest:
                case FarmerIntent.Await:
                    return ReferenceEquals(decision.Plot, _current.Plot);

                case FarmerIntent.Refresh:
                case FarmerIntent.Trade:
                    return ReferenceEquals(decision.Place, _current.Place);

                case FarmerIntent.Relax:
                    // Место могло чуть сместиться — это всё тот же отдых, а не новое решение.
                    return (decision.Spot - _current.Spot).sqrMagnitude < 1f;

                default:
                    return true;
            }
        }

        /// <summary>Замереть на мгновение с новой мыслью — тот самый видимый момент выбора.</summary>
        private void BeginThinking(in FarmerDecision decision)
        {
            _pending = decision;
            _mover.Stop();
            SetThought(decision.Thought);

            if (_thinkDuration <= 0f) { Commit(decision); return; }

            _timer = _thinkDuration;
            SetState(FarmerState.Thinking);
        }

        private void TickThinking()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f) Commit(_pending);
        }

        /// <summary>Перевести намерение в состояние автомата.</summary>
        private void Commit(in FarmerDecision decision)
        {
            _current = decision;
            _currentScore = decision.Score;

            switch (decision.Intent)
            {
                case FarmerIntent.Sleep:
                    EnterSleeping();
                    break;

                case FarmerIntent.Refresh:
                    if (decision.Place != null) EnterGoingToService(decision.Place);
                    else EnterIdle();
                    break;

                case FarmerIntent.Deliver:
                    EnterReturningHome();
                    break;

                case FarmerIntent.Harvest:
                    if (decision.Plot != null) EnterGoingToHarvest(decision.Plot);
                    else EnterIdle();
                    break;

                case FarmerIntent.Await:
                    if (decision.Plot != null) EnterAwaiting(decision.Plot);
                    else EnterIdle();
                    break;

                case FarmerIntent.Trade:
                    if (decision.Place != null) EnterGoingToMarket(decision.Place);
                    else EnterIdle();
                    break;

                case FarmerIntent.Relax:
                    EnterRelaxing(decision.Spot);
                    break;

                case FarmerIntent.Tidy:
                    if (decision.Plot != null) EnterGoingToTidy(decision.Plot, decision.Spot);
                    else EnterIdle();
                    break;

                default:
                    EnterIdle();
                    break;
            }
        }

        private void SetThought(string text) => _thought = text ?? "";

        #endregion

        #region Чувства

        /// <summary>
        /// Показать чувство над головой, если облачко успело остыть.
        /// <para>
        /// Общий кулдаун на все поводы намеренно один: важно не «сколько раз он проголодался»,
        /// а то, что над фермером изредка что-то всплывает. Два символа подряд читаются как
        /// болтовня, один раз в полминуты — как живой человек.
        /// </para>
        /// </summary>
        private void Emote(FarmerEmote emote)
        {
            if (emote == FarmerEmote.None || _emoteTimer > 0f) return;
            _emoteTimer = _emoteCooldown;

            var handler = Emoted;
            if (handler == null) return;
            try { handler(this, emote); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void TickEmotes()
        {
            if (_emoteTimer > 0f) _emoteTimer -= Time.deltaTime;

            if (_needs == null || _state == FarmerState.Sleeping) return;

            _hummingTimer -= Time.deltaTime;
            if (_hummingTimer > 0f) return;

            _hummingTimer = UnityEngine.Random.Range(
                _hummingInterval.x, Mathf.Max(_hummingInterval.x, _hummingInterval.y));

            // Мурлычет только когда действительно всё хорошо — иначе символ обесценится.
            if (_needs.Wellbeing01 >= _hummingWellbeing) Emote(FarmerEmote.Happy);
        }

        private void OnBecameHungry(CharacterNeeds needs) => Emote(FarmerEmote.Food);
        private void OnBecameThirsty(CharacterNeeds needs) => Emote(FarmerEmote.Drink);
        private void OnBecameTired(CharacterNeeds needs) => Emote(FarmerEmote.Sleep);

        #endregion

        #region Состояния

        private void TickIdle()
        {
            if (Rethink()) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f) EnterWander();
        }

        private void TickWandering()
        {
            if (Rethink()) return;
            if (_mover.HasArrived) EnterIdle();
        }

        /// <summary>Быт: дойти до места и осмотреться. Уходит отсюда, как только появится дело.</summary>
        private void TickRelaxing()
        {
            if (Rethink()) return;

            if (!_mover.HasArrived)
            {
                _mover.SetDestination(_relaxSpot);
                return;
            }

            // Медленно поворачивается: безделье должно читаться как отдых, а не как зависание.
            transform.Rotate(0f, _lookAroundSpeed * Time.deltaTime, 0f);
        }

        /// <summary>Ждать у грядки, которой осталось чуть-чуть.</summary>
        private void TickAwaiting()
        {
            if (_target == null || _target.Phase == GrowthPhase.Empty)
            {
                _target = null;
                EnterIdle();
                return;
            }

            if (_target.IsReady)
            {
                EnterGoingToHarvest(_target);
                return;
            }

            if (!_mover.HasArrived)
            {
                _mover.SetDestination(_target.transform.position);
                return;
            }

            FaceTowards(_target.transform.position);
            Rethink(_switchMargin);
        }

        /// <summary>Идём за грядкой, которую решили переставить.</summary>
        private void TickGoingToTidy()
        {
            // Пока шли, грядку могли собрать, снести — или игрок сам взялся её двигать.
            if (!CanStillTidy())
            {
                _target = null;
                EnterIdle();
                return;
            }

            if (_mover.HasArrived)
            {
                EnterHauling();
                return;
            }

            _mover.SetDestination(_target.transform.position);
        }

        /// <summary>Несём грядку на новое место.</summary>
        private void TickHauling()
        {
            if (_target == null || !DragFocus.IsDragged(_target.transform))
            {
                // Груз перехватили или он исчез — руки пусты, идём заниматься другим.
                Release();
                EnterIdle();
                return;
            }

            // Груз висит перед фермером и едет вместе с ним, по рельефу под ногами.
            Vector3 carried = transform.position + transform.forward * _carryReach;
            carried.y = FarmingRuntime.Ground.SampleHeight(carried) + _carryHeight;
            _target.transform.position = carried;

            if (!_mover.HasArrived)
            {
                _mover.SetDestination(_tidySpot);
                return;
            }

            // Ставим ровно туда, куда несли, и по земле в этой точке.
            var placed = FarmBounds.ClampToFarm(_tidySpot);
            placed.y = FarmingRuntime.Ground.SampleHeight(placed);
            _target.transform.position = placed;

            Release();
            EnterIdle();
        }

        /// <summary>Можно ли всё ещё переставлять взятую на прицел грядку.</summary>
        private bool CanStillTidy()
        {
            if (_target == null || _target.Phase == GrowthPhase.Empty) return false;

            // Игрок взялся за неё сам — его ход важнее наведения порядка.
            return !DragFocus.IsPlayerClaimed(_target.transform);
        }

        /// <summary>Отпустить груз, чем бы дело ни кончилось.</summary>
        private void Release()
        {
            if (_target != null && DragFocus.IsDragged(_target.transform)) DragFocus.Clear();
            _target = null;
        }

        private void TickGoingToHarvest()
        {
            // Кто-то успел раньше, или грядка испортилась, пока мы шли.
            if (_target == null || !_target.IsReady)
            {
                _target = null;
                EnterIdle();
                return;
            }

            // Проверять прибытие ДО повторной установки цели: SetDestination сбрасывает флаг
            // прибытия у ходока, и в обратном порядке мы никогда бы не увидели, что дошли.
            if (_mover.HasArrived)
            {
                EnterHarvesting();
                return;
            }

            // Передумать на ходу можно — но только ради заметно лучшего.
            if (Rethink(_switchMargin)) return;

            _mover.SetDestination(_target.transform.position);
        }

        private void TickHarvesting()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            if (_target != null && _target.TryHarvest(out HarvestResult result, Inventory))
            {
                Raise(Collected, result);
                _plotsThisTrip++;

                if (_skills != null)
                {
                    _skills.Grant(FarmerSkill.Harvesting, 2.5f + result.Level);
                    _skills.Grant(FarmerSkill.Back, result.Amount * 0.6f);

                    // Намётанный глаз: иногда с грядки снимается лишнее.
                    if (result.Resource != null &&
                        UnityEngine.Random.value < _skills.BonusYieldChance)
                    {
                        Inventory.TryAdd(result.Resource, 1);
                        Raise(Collected, new HarvestResult(result.Source, result.Resource, 1, result.Level));
                    }
                }
            }
            _target = null;

            // Обход: с опытом ног он перестаёт бегать домой после каждой грядки.
            int route = _skills != null ? _skills.RouteLength : 1;
            if (Inventory.IsFull || _plotsThisTrip >= route) EnterReturningHome();
            else EnterIdle();
        }

        private void TickReturningHome()
        {
            if (_mover.HasArrived)
            {
                EnterDepositing();
                return;
            }

            _mover.SetDestination(_homePosition);
        }

        private void TickDepositing()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            int moved = Inventory.TransferTo(FarmingRuntime.Sink);
            if (moved > 0)
            {
                Raise(Delivered, moved);
                if (_skills != null) _skills.Grant(FarmerSkill.Back, moved * 1.2f);
            }

            _plotsThisTrip = 0;
            EnterIdle();
        }

        private void TickGoingToService()
        {
            // Пока шли, еду могли продать, а постройку — снести.
            if (_serviceTarget == null || _needs == null ||
                !_serviceTarget.CanServe(_needs.Satiety01, _needs.Hydration01))
            {
                _serviceTarget = null;
                EnterIdle();
                return;
            }

            if (_mover.HasArrived) { EnterUsingService(); return; }
            _mover.SetDestination(_serviceTarget.transform.position);
        }

        private void TickUsingService()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            if (_serviceTarget != null && _needs != null)
            {
                _serviceTarget.Serve(_needs.MaxSatiety, _needs.MaxHydration,
                                     out float food, out float water);
                if (food > 0f) _needs.Eat(food);
                if (water > 0f) _needs.Drink(water);

                Raise(Refreshed, _serviceTarget);
            }

            _serviceTarget = null;
            EnterIdle();
        }

        private void TickGoingToMarket()
        {
            // Рынок могли снести, а склад — опустошить, пока он шёл.
            if (_market == null || !_brain.HasMarketErrand())
            {
                _market = null;
                EnterIdle();
                return;
            }

            if (_mover.HasArrived) { EnterAtMarket(); return; }
            _mover.SetDestination(_market.transform.position);
        }

        /// <summary>
        /// Один визит закрывает обе половины сделки: продать излишки и потратить выручку.
        /// Вместе, а не двумя походами, потому что в этом смысл похода — и золото с продажи
        /// ровно то, чем оплачивается покупка.
        /// </summary>
        private void TickAtMarket()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            var shop = Shop.Instance;

            if (shop != null && _brain.HasSurplus(out var resource, out int amount))
            {
                int gold = shop.TrySell(resource, amount);
                if (gold > 0)
                {
                    Raise(Traded, gold);
                    if (_skills != null) _skills.Grant(FarmerSkill.Wits, 2f + gold * 0.02f);
                }
            }

            var purchase = _brain.PickRestock();
            if (purchase != null && shop != null && shop.TryBuy(purchase))
            {
                Raise(Restocked, purchase);
                if (_skills != null) _skills.Grant(FarmerSkill.Wits, 6f);
            }

            _market = null;
            EnterIdle();
        }

        private void TickSleeping()
        {
            if (_needs == null) { EnterIdle(); return; }

            _needs.Sleep(Time.deltaTime);

            // Просыпается, когда выспался и рассвело. Ночью выспавшийся продолжает лежать —
            // иначе он всю ночь будет вставать и снова ложиться.
            bool night = _sleepAtNight && DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
            if (_needs.IsRested && !night) EnterIdle();
        }

        #endregion

        #region Переходы

        private void EnterIdle()
        {
            _mover.Stop();

            // Свободен: ничем не занят, поэтому любое дело теперь лучше безделья.
            _current = FarmerDecision.None;
            _currentScore = 0f;
            _timer = UnityEngine.Random.Range(_idlePauseMin, Mathf.Max(_idlePauseMin, _idlePauseMax));
            SetState(FarmerState.Idle);
        }

        private void EnterWander()
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * _wanderRadius;
            _mover.SetDestination(_homePosition + new Vector3(offset.x, 0f, offset.y));
            SetState(FarmerState.Wandering);
        }

        private void EnterRelaxing(Vector3 spot)
        {
            _relaxSpot = FarmBounds.ClampToFarm(spot);
            _mover.SetDestination(_relaxSpot);
            SetState(FarmerState.Relaxing);
        }

        private void EnterAwaiting(Growable plot)
        {
            _target = plot;
            _mover.SetDestination(plot.transform.position);
            SetState(FarmerState.Awaiting);
        }

        private void EnterGoingToTidy(Growable plot, Vector3 spot)
        {
            _target = plot;
            _tidySpot = spot;
            _mover.SetDestination(plot.transform.position);
            SetState(FarmerState.GoingToTidy);
        }

        /// <summary>
        /// Взять грядку в руки. Занимаем тот же слот, что и мышь игрока: нести её одновременно
        /// вдвоём нельзя, а животное по дороге должно перестать брести само.
        /// </summary>
        private void EnterHauling()
        {
            if (_target == null) { EnterIdle(); return; }

            DragFocus.Set(_target.transform, byPlayer: false);

            _mover.SetDestination(_tidySpot);
            SetState(FarmerState.Hauling);
        }

        private void EnterGoingToHarvest(Growable target)
        {
            _target = target;
            _mover.SetDestination(target.transform.position);
            SetState(FarmerState.GoingToHarvest);
        }

        private void EnterHarvesting()
        {
            _mover.Stop();

            // Пока работает — лицом к грядке.
            if (_target != null) FaceTowards(_target.transform.position);

            float speed = _skills != null ? _skills.HarvestSpeed : 1f;
            _timer = _harvestDuration / Mathf.Max(0.1f, speed);
            SetState(FarmerState.Harvesting);
        }

        private void EnterReturningHome()
        {
            _mover.SetDestination(_homePosition);
            SetState(FarmerState.ReturningHome);
        }

        private void EnterDepositing()
        {
            _mover.Stop();
            _timer = _depositDuration;
            SetState(FarmerState.Depositing);
        }

        private void EnterGoingToService(Building target)
        {
            _serviceTarget = target;
            _mover.SetDestination(target.transform.position);
            SetState(FarmerState.GoingToService);
        }

        private void EnterUsingService()
        {
            _mover.Stop();
            if (_serviceTarget != null) FaceTowards(_serviceTarget.transform.position);

            _timer = _serviceTarget != null && _serviceTarget.Definition != null
                ? _serviceTarget.Definition.ServiceDuration
                : 1f;

            SetState(FarmerState.UsingService);
        }

        private void EnterGoingToMarket(Building market)
        {
            _market = market;
            _mover.SetDestination(market.transform.position);
            SetState(FarmerState.GoingToMarket);
        }

        private void EnterAtMarket()
        {
            _mover.Stop();
            FaceTowards(_market != null ? _market.transform.position : transform.position);
            _timer = _sellDuration;
            SetState(FarmerState.AtMarket);
        }

        /// <summary>
        /// Спит там, где стоит. Ничто на ферме не зависит от места его сна, а фермер,
        /// свалившийся у дальнего забора и бредущий домой, прежде чем ему позволят отдохнуть,
        /// читается как наказание за решение, которого игрок не принимал.
        /// </summary>
        private void EnterSleeping()
        {
            _mover.Stop();
            Emote(FarmerEmote.Sleep);
            Release();   // уснуть с грядкой в руках нельзя — она осталась бы висеть в воздухе
            _target = null;
            _serviceTarget = null;
            _market = null;
            SetState(FarmerState.Sleeping);
        }

        private void FaceTowards(Vector3 position)
        {
            Vector3 look = position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }

        private void SetState(FarmerState state)
        {
            if (_state == state) return;
            _state = state;

            var handler = StateChanged;
            if (handler == null) return;
            try { handler(this, state); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void Raise<T>(Action<FarmerAgent, T> handler, T arg)
        {
            if (handler == null) return;
            try { handler(this, arg); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        #endregion

        private void OnDrawGizmosSelected()
        {
            Vector3 home = Application.isPlaying
                ? _homePosition
                : (_home != null ? _home.position : transform.position);

            Gizmos.color = new Color(0.3f, 0.8f, 0.4f, 0.9f);
            DrawCircle(home, _wanderRadius);

            Gizmos.color = new Color(0.9f, 0.7f, 0.2f, 0.5f);
            DrawCircle(transform.position, _searchRadius);

            if (Application.isPlaying && _target != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, _target.transform.position);
            }
        }

        private static void DrawCircle(Vector3 center, float radius)
        {
            const int segments = 48;
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}

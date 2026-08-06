using System;
using System.Collections.Generic;
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
        Hauling = 15,
        /// <summary>Идёт домой ложиться.</summary>
        GoingToSleep = 16,
        /// <summary>Идёт к месту, где задумал что-то построить.</summary>
        GoingToImprove = 17,
        /// <summary>Мастерит задуманное.</summary>
        Improving = 18,
        /// <summary>Мастеровой идёт к станку (этап 2 колонии).</summary>
        GoingToCraft = 19,
        /// <summary>Стоит у станка и ведёт партии.</summary>
        Crafting = 20,
        /// <summary>Возчик идёт к дому взять короб сданного заказа.</summary>
        GoingToLoad = 21,
        /// <summary>Возчик везёт короб заказа к рынку.</summary>
        DeliveringOrder = 22
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

        [Tooltip("Где он спит. Пусто — у дома. Поставь сюда пустой объект внутри избы, " +
                 "и он будет ложиться именно там.")]
        [SerializeField] private Transform _bed;

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
        [Tooltip("Может ли фермер собирать урожай сам. В онлайн-версии выключено: урожай — " +
                 "хозяйский, ради него игрок и возвращается. Вернётся в виде платной помощи " +
                 "с ограничениями (Ф3, docs/ONLINE.md).")]
        [SerializeField] private bool _mayHarvest;
        [SerializeField] private bool _filterByCategory;
        [SerializeField] private ResourceCategory _category = ResourceCategory.Crop;

        [Header("Торговля")]
        [Tooltip("Сколько единиц ресурса оставлять на складе, не продавая. Еду держим про запас: " +
                 "распродать её подчистую — значит остаться без кухни.")]
        [SerializeField, Min(0)] private int _keepFood = 25;

        [Tooltip("Сколько оставлять от несъедобного. Оно нужно только на постройки, " +
                 "поэтому запас заметно больше минимальной партии.")]
        [SerializeField, Min(0)] private int _keepMaterials = 45;

        [Tooltip("Сколько материала фермер бережёт, берясь за собственный замысел.\n" +
                 "Отдельно от торгового запаса и намеренно маленький: замысел стоит 1-4 единицы, " +
                 "и мерить его торговым буфером — значит выключить обустройство целиком.")]
        [SerializeField, Min(0)] private int _keepForImprovements = 6;

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

        [Header("Замыслы")]
        [Tooltip("Что он однажды решит построить сам — клумбу, скамейку, костёр. Пусто — не строит.\n" +
                 "Список личный: у будущих жителей будут свои замыслы, и ферма обживётся разными руками.")]
        [SerializeField] private ImprovementDefinition[] _improvements = Array.Empty<ImprovementDefinition>();

        [Tooltip("Не больше стольких замыслов в день: обживание — приправа к работе, а не вторая работа.")]
        [SerializeField, Min(1)] private int _improvementsPerDay = 2;

        [Tooltip("За сколько секунд желание построить дозревает до готовности отложить рутину.\n" +
                 "Тот же приём, что с забытыми грядками: на занятой ферме свободной минуты не " +
                 "бывает никогда, и замысел, который ждёт её вежливо, не случится вообще.")]
        [SerializeField, Min(10f)] private float _improvementUrgeTime = 180f;

        [Header("Живость")]
        [Tooltip("Как быстро он поворачивается взглянуть на то, что привлекло внимание, градусов в секунду.")]
        [SerializeField, Min(30f)] private float _glanceTurnSpeed = 220f;

        [Tooltip("Пауза между случайными взглядами по сторонам в простое: от и до, секунд.")]
        [SerializeField] private Vector2 _glanceInterval = new Vector2(2.5f, 6f);

        [Tooltip("Пауза между праздными мыслями в простое: от и до, секунд.")]
        [SerializeField] private Vector2 _musingInterval = new Vector2(16f, 30f);

        [Tooltip("С какого расстояния он замечает чужие события — слияние, поднятую игроком грядку.")]
        [SerializeField, Min(1f)] private float _noticeRadius = 9f;

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
        private Vector3 _bedPosition;
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

        // Живость: куда и сколько ещё глазеть, когда думать о своём.
        private Vector3 _glancePoint;
        private float _glanceHold;
        private float _glanceTimer;
        private float _musingTimer;
        private float _sinceThought = 999f;   // свежее решение мыслью не перебиваем

        // Мягкое внимание на ходу: цель для взгляда головой, не трогающая ни маршрут,
        // ни состояние. Потребляет FarmerHeadLook; корпусом продолжает рулить ходок.
        private Vector3 _softGlancePoint;
        private float _softGlanceHold;

        // Рабочие мысли — второй, заметно более редкий канал, чем праздные в простое.
        private float _workMusingTimer;

        // Плавный довод лица к рабочей цели вместо мгновенного щелчка при входе в стойку.
        private Vector3 _facePoint;
        private bool _hasFacePoint;

        // Замыслы: что задумал, где строит и сколько уже настроил.
        private ImprovementDefinition _project;
        private Vector3 _projectSpot;
        private Building _projectAnchor;   // чей дворик обустраивает; null — дом или любимое место

        private readonly Dictionary<ImprovementDefinition, int> _builtCount =
            new Dictionary<ImprovementDefinition, int>();

        // Свой счёт на каждый экземпляр якоря: у двух хлевов — два независимых дворика.
        private readonly Dictionary<Building, Dictionary<ImprovementDefinition, int>> _builtAt =
            new Dictionary<Building, Dictionary<ImprovementDefinition, int>>();

        private int _builtToday;
        private int _builtDayStamp = -1;
        private float _lastImprovedAt;

        internal int RegistryIndex = -1;

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

        /// <summary>Достроил задуманное. Третий аргумент — что появилось на ферме.</summary>
        public event Action<FarmerAgent, ImprovementDefinition, GameObject> Improved;

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

        /// <summary>Личный список замыслов. У каждого жителя свой.</summary>
        internal IReadOnlyList<ImprovementDefinition> Improvements => _improvements;

        /// <summary>
        /// Сколько таких он построил: у якоря-постройки — в её дворике, иначе на ферме.
        /// </summary>
        internal int BuiltCount(ImprovementDefinition project, Building anchor = null)
        {
            if (project == null) return 0;

            if (anchor != null)
                return _builtAt.TryGetValue(anchor, out var yard) && yard.TryGetValue(project, out int at) ? at : 0;

            return _builtCount.TryGetValue(project, out int n) ? n : 0;
        }

        /// <summary>Не выбрана ли дневная норма обживания.</summary>
        internal bool ImprovementQuotaLeft
        {
            get
            {
                RefreshImprovementDay();
                return _builtToday < _improvementsPerDay;
            }
        }

        /// <summary>
        /// Насколько дозрело желание что-то построить, 0..1. Растёт со времени последней стройки:
        /// вежливое ожидание свободной минуты на занятой ферме не наступает никогда, поэтому
        /// зрелое желание вправе отодвинуть дешёвую рутину — как это делают забытые грядки.
        /// </summary>
        internal float ImprovementUrge01 =>
            Mathf.Clamp01((Time.time - _lastImprovedAt) / Mathf.Max(10f, _improvementUrgeTime));

        private void RefreshImprovementDay()
        {
            int day = FarmProgress.Day;
            if (day == _builtDayStamp) return;
            _builtDayStamp = day;
            _builtToday = 0;
        }

        // ---- сохранение ----

        /// <summary>Общефермовый счёт построенного (дом и любимое место) — для сохранения.</summary>
        public IReadOnlyDictionary<ImprovementDefinition, int> CaptureBuilt() => _builtCount;

        /// <summary>Счёт по дворикам: у какой постройки сколько чего он уже поставил.</summary>
        public IReadOnlyDictionary<Building, Dictionary<ImprovementDefinition, int>> CaptureBuiltYards() => _builtAt;

        public void RestoreBuilt(ImprovementDefinition project, int count)
        {
            if (project == null || count <= 0) return;
            _builtCount[project] = count;
        }

        public void RestoreBuiltYard(Building anchor, ImprovementDefinition project, int count)
        {
            if (anchor == null || project == null || count <= 0) return;

            if (!_builtAt.TryGetValue(anchor, out var yard))
                _builtAt[anchor] = yard = new Dictionary<ImprovementDefinition, int>();

            yard[project] = count;
        }

        /// <summary>Поставить фермера туда, где его застало сохранение, и вернуть в покой.</summary>
        public void RestorePlacement(Vector3 position, float yaw)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _lastPosition = position;

            Release();
            _serviceTarget = null;
            _market = null;
            _project = null;
            _projectAnchor = null;

            if (_mover != null) _mover.Stop();
            EnterIdle();
        }

        internal Vector3 Pos => transform.position;
        internal IInventory Pack => Inventory;
        internal CharacterNeeds Needs => _needs;
        internal Vector3 FavouriteSpot => _favouriteSpot;
        internal float SearchRadius => _searchRadius;
        internal bool SleepsAtNight => _sleepAtNight;
        // Наём подсобника включает сбор рутины; наём мастерового — станки, не сбор:
        // демаркация ролей (CLAUDE.md), урожай остаётся хозяйским.
        internal bool MayHarvest => _mayHarvest || (IsHired && Role == ResidentRole.None);
        internal bool OnlyOwnCategory => _filterByCategory;
        internal ResourceCategory Category => _category;
        internal int KeepFood => _keepFood;
        internal int KeepMaterials => _keepMaterials;
        internal int KeepForImprovements => _keepForImprovements;
        internal int MinSaleBatch => _minSaleBatch;
        internal int UrgentSurplus => _urgentSurplus;
        internal int GoldReserve => _goldReserve;
        internal int MaxPlots => _maxPlots;
        internal float TidySpacing => _tidySpacing;

        // ---- наём (Ф3, docs/ONLINE.md) ----

        /// <summary>
        /// Вершина лестницы принадлежит рукам игрока: нанятый фермер собирает только рутину —
        /// ступени не выше этой. Требование владельца: помощь не должна снимать с игрока
        /// необходимость собирать самому, и дорогие многочасовые культуры — всегда его.
        /// </summary>
        public const int HelperMaxTier = 3;

        /// <summary>
        /// До какого момента (в секундах серверных часов) фермер нанят. Не сериализуется
        /// в сцену: живёт в сохранении и продлевается жалованием.
        /// </summary>
        private double _hiredUntil;

        /// <summary>Нанят ли сейчас — по тем же часам, что растят грядки.</summary>
        public bool IsHired => FarmingRuntime.Now < _hiredUntil;

        /// <summary>Сколько секунд найма осталось. 0 — не нанят.</summary>
        public double HiredSecondsLeft => System.Math.Max(0.0, _hiredUntil - FarmingRuntime.Now);

        /// <summary>Для сохранения: момент окончания найма как есть.</summary>
        public double HiredUntil => _hiredUntil;

        /// <summary>
        /// Нанять фермера на <paramref name="seconds"/> вперёд за <paramref name="wage"/> золота.
        /// Продление складывается: заплатил дважды — работает двое суток. Денег нет — false,
        /// и говорить об этом вслух обязан вызывающий UI.
        /// </summary>
        public bool TryHire(double seconds, int wage)
        {
            if (seconds <= 0.0) return false;

            var wallet = Wallet.Instance;
            if (wallet == null || !wallet.TrySpend(wage)) return false;

            double from = System.Math.Max(FarmingRuntime.Now, _hiredUntil);
            _hiredUntil = from + seconds;
            return true;
        }

        /// <summary>Вернуть наём из сохранения.</summary>
        public void RestoreHired(double hiredUntil) => _hiredUntil = hiredUntil;

        /// <summary>Роль жителя из определения. Меняет смысл найма, не сам механизм.</summary>
        public ResidentRole Role => _role;

        /// <summary>
        /// Жалование этой роли за сутки. Ноль в определении читается как общий тариф —
        /// чтобы подсобники не требовали числа в каждом ассете.
        /// </summary>
        public int RoleWagePerDay => _roleWage > 0 ? _roleWage : TierEconomy.FarmerWagePerDay;

        private ResidentRole _role = ResidentRole.None;
        private int _roleWage;

        /// <summary>Рвение к порядку из определения жителя. Читает мозг при оценке уборки.</summary>
        internal float TidyZeal => _tidyZeal;

        private float _tidyZeal = 1f;

        /// <summary>
        /// Применить определение жителя: имя-идентичность, койку, рвения, личные замыслы.
        /// Зовёт <see cref="ColonyRoster"/> в Awake — раньше Start, где из койки считается
        /// место сна, и раньше пересева черт с загрузкой (SaveRunner, order 100): их данные
        /// главнее и лягут поверх.
        /// </summary>
        internal void ApplyDefinition(ResidentDefinition definition, Transform bed, Transform home)
        {
            if (definition == null) return;

            if (!string.IsNullOrEmpty(definition.DisplayName)) name = definition.DisplayName;
            if (bed != null) _bed = bed;
            if (home != null) _home = home;

            _role = definition.Role;
            _roleWage = definition.WagePerDay;
            _tidyZeal = Mathf.Clamp(definition.TidyZeal, 0.5f, 2f);

            // Тяга к обустройству делит срок дозревания желания: рьяный хочет строить
            // раньше. Деление готовой настройки, а не своё поле в мозге, — мозг читает
            // ImprovementUrge01 как раньше, и полосы оценок разницы не видят.
            _improvementUrgeTime = Mathf.Max(10f, _improvementUrgeTime /
                Mathf.Clamp(definition.ProjectZeal, 0.5f, 2f));

            if (definition.Improvements != null && definition.Improvements.Length > 0)
                _improvements = (ImprovementDefinition[])definition.Improvements.Clone();

            // Характер от имени по определению: свой у каждого жителя и одинаковый от
            // запуска к запуску даже без входа в аккаунт. Пересев от имени игрока и
            // характер из сейва придут позже и перепишут этот — так и задумано.
            var traits = GetComponent<FarmerTraits>();
            if (traits != null) traits.Reroll(name);
        }

        #region Жизненный цикл

        private void Awake()
        {
            _mover = GetComponent<AgentMover>();
            if (_mover == null)
                Debug.LogError("[Farmer] Нужен компонент AgentMover (например SimpleMover)", this);

            EnsureInventory();

            // Голова как канал внимания. Добавляется кодом по тому же праву, что инвентарь:
            // это часть фермера, а не опция сцены. Не нашла кость — тихо бездействует.
            if (GetComponent<FarmerHeadLook>() == null) gameObject.AddComponent<FarmerHeadLook>();

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

            // События мира — только повод оглянуться, никогда не повод бросить дело.
            // «-=» перед «+=» страхует от двойной подписки при выключении-включении.
            FarmingEvents.Merged -= OnWorldMerged;
            FarmingEvents.Merged += OnWorldMerged;
            FarmingEvents.Ready -= OnWorldReady;
            FarmingEvents.Ready += OnWorldReady;
            DragFocus.Changed -= OnDragChanged;
            DragFocus.Changed += OnDragChanged;
        }

        private void OnEnable()
        {
            FarmerRegistry.Register(this);

            // Подписка безусловная, роль проверяет сам хендлер: Awake и OnEnable Unity
            // зовёт ПАРАМИ ПО ОБЪЕКТАМ, и OnEnable жителя может пройти раньше Awake
            // ростера — роль в этот миг ещё не назначена (замер 06.08.2026: Тимофей
            // включился первым и не подписался). Лишний вызов на жителя при сдаче —
            // копейки, потерянная подписка — молчаливо мёртвый возчик.
            FarmOrders.FilledOrder += OnOrderFilled;
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

            // Снос посреди смены у станка минует SetState — отписка руками. Лишний
            // вызов без подписки безвреден.
            if (_craftShop != null) { _craftShop.Produced -= OnCraftProduced; _craftShop = null; }
            FarmOrders.FilledOrder -= OnOrderFilled;

            FarmerRegistry.Unregister(this);
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

            FarmingEvents.Merged -= OnWorldMerged;
            FarmingEvents.Ready -= OnWorldReady;
            DragFocus.Changed -= OnDragChanged;
        }

        private void Start()
        {
            _homePosition = _home != null ? _home.position : transform.position;
            _bedPosition = _bed != null ? _bed.position : _homePosition;
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

            _glanceTimer = UnityEngine.Random.Range(_glanceInterval.x, Mathf.Max(_glanceInterval.x, _glanceInterval.y));
            _musingTimer = UnityEngine.Random.Range(_musingInterval.x, Mathf.Max(_musingInterval.x, _musingInterval.y));
            _workMusingTimer = UnityEngine.Random.Range(45f, 90f);

            // Желание строить стартует наполовину дозревшим, со сдвигом на работника:
            // первая мелочь появляется в первый же день, но у всех в разное время.
            _lastImprovedAt = Time.time - _improvementUrgeTime * UnityEngine.Random.Range(0.3f, 0.6f);

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
            _mover.Speed = _baseSpeed * (_needs != null ? _needs.Productivity01 : 1f)
                           * skillSpeed * FarmBuffs.MoveSpeedAt(transform.position);

            TrackEffort();
            TickEmotes();

            // Нанятый возчик держит слот доски заказов меткой со сроком годности:
            // кончился наём или пропал возчик — метка протухла, слот ушёл сам.
            if (_role == ResidentRole.Carter && IsHired) FarmOrders.StampCarrier();

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
                case FarmerState.GoingToSleep: TickGoingToSleep(); break;
                case FarmerState.Sleeping: TickSleeping(); break;
                case FarmerState.GoingToImprove: TickGoingToImprove(); break;
                case FarmerState.Improving: TickImproving(); break;
                case FarmerState.GoingToCraft: TickGoingToCraft(); break;
                case FarmerState.Crafting: TickCrafting(); break;
                case FarmerState.GoingToLoad: TickGoingToLoad(); break;
                case FarmerState.DeliveringOrder: TickDeliveringOrder(); break;
            }

            TickLiveliness();
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
            {
                _needs.Exertion = _state == FarmerState.Sleeping ? 0f : (moved > 0.001f ? 1f : 0.35f);

                // Тот же единственный источник истины, что у Exertion: во сне голод и жажда
                // текут медленнее, иначе каждое утро начинается с кризиса воды.
                _needs.IsSleeping = _state == FarmerState.Sleeping;
            }

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
                case FarmerIntent.Craft:
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
                    EnterGoingToSleep();
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

                case FarmerIntent.Improve:
                    // Place здесь — постройка-якорь ансамбля, не место визита.
                    if (decision.Improvement != null)
                        EnterGoingToImprove(decision.Improvement, decision.Spot, decision.Place);
                    else EnterIdle();
                    break;

                case FarmerIntent.Tidy:
                    if (decision.Plot != null) EnterGoingToTidy(decision.Plot, decision.Spot);
                    else EnterIdle();
                    break;

                case FarmerIntent.Craft:
                    if (decision.Place != null) EnterGoingToCraft(decision.Place);
                    else EnterIdle();
                    break;

                case FarmerIntent.CarryOrder:
                    EnterGoingToLoad();
                    break;

                default:
                    EnterIdle();
                    break;
            }
        }

        private void SetThought(string text)
        {
            _thought = text ?? "";
            _sinceThought = 0f;   // праздная мысль не смеет затирать свежую мысль-решение
        }

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

        #region Живость

        // Всё в этом регионе — шум ВОКРУГ решений, никогда вместо них: ни взгляд, ни мысль
        // не меняют состояние, не трогают выбор дела и не стоят ни секунды работы.
        // Ровно поэтому им можно быть случайными — случайность в деле читается как поломка,
        // случайность в безделье читается как жизнь.

        /// <summary>
        /// Оглянуться на точку, если сейчас удобно. Занятого не отвлекает: слияние за спиной
        /// идущего с полным рюкзаком фермера пропадёт незамеченным — и это правильно,
        /// увлечённость делом такой же признак живого, как и любопытство.
        /// </summary>
        private bool Notice(Vector3 point)
        {
            bool receptive = _state == FarmerState.Idle || _state == FarmerState.Relaxing ||
                             _state == FarmerState.Wandering;
            if (!receptive) return false;

            if ((point - transform.position).sqrMagnitude > _noticeRadius * _noticeRadius) return false;

            // Прогуливался — остановился поглазеть: остановка сама по себе жест.
            // Wandering при остановленном ходоке штатно стекает в Idle следующим тиком.
            if (_state == FarmerState.Wandering) _mover.Stop();

            _glancePoint = point;
            _glanceHold = UnityEngine.Random.Range(1.2f, 2f);
            _glanceTimer = UnityEngine.Random.Range(_glanceInterval.x, Mathf.Max(_glanceInterval.x, _glanceInterval.y));
            return true;
        }

        /// <summary>
        /// Проводить взглядом, не прерывая дела: только голова (через <see cref="GlanceTarget"/>
        /// и FarmerHeadLook), маршрут, темп и состояние нетронуты. Это ответ на «идёт с каменным
        /// лицом»: занятый не бросает работу ради событий мира, но живой их ЗАМЕЧАЕТ.
        /// </summary>
        private bool NoticeSoft(Vector3 point)
        {
            bool walking = _state == FarmerState.GoingToHarvest || _state == FarmerState.ReturningHome ||
                           _state == FarmerState.GoingToMarket || _state == FarmerState.GoingToService ||
                           _state == FarmerState.GoingToTidy || _state == FarmerState.GoingToImprove ||
                           _state == FarmerState.GoingToSleep || _state == FarmerState.Hauling;
            if (!walking) return false;

            if ((point - transform.position).sqrMagnitude > _noticeRadius * _noticeRadius) return false;

            _softGlancePoint = point;
            _softGlanceHold = UnityEngine.Random.Range(0.8f, 1.4f);
            return true;
        }

        /// <summary>
        /// Куда сейчас смотрит внимание — для головы (FarmerHeadLook), не для корпуса.
        /// Null — смотреть некуда, голова возвращается к нейтрали.
        /// </summary>
        public Vector3? GlanceTarget
        {
            get
            {
                if (_softGlanceHold > 0f) return _softGlancePoint;
                if (_glanceHold > 0f) return _glancePoint;
                return null;
            }
        }

        /// <summary>Слияние — ход игрока, и фермер радуется ему вместе с игроком.</summary>
        private void OnWorldMerged(Growable survivor, Growable absorbed)
        {
            if (survivor == null) return;

            // Свободный оборачивается всем телом; идущий — хотя бы головой. Раньше занятый
            // не реагировал вовсе, и главный ход игрока в двух метрах от фермера проходил
            // при каменном лице.
            bool seen = Notice(survivor.transform.position) || NoticeSoft(survivor.transform.position);
            if (seen && UnityEngine.Random.value < 0.4f)
                Emote(FarmerEmote.Happy);
        }

        private void OnWorldReady(Growable plot)
        {
            if (plot == null) return;

            // Не каждый хлопок созревания заслуживает поворота головы — иначе взгляды
            // сами станут метрономом при двадцати грядках. На ходу — ещё реже: занятому
            // до созреваний меньше дела, чем праздному.
            if (UnityEngine.Random.value < 0.5f && Notice(plot.transform.position)) return;
            if (UnityEngine.Random.value < 0.25f) NoticeSoft(plot.transform.position);
        }

        private void OnDragChanged(Transform dragged)
        {
            // Только то, что поднял игрок: собственную ношу он и так «видит», а прикосновение
            // игрока DragFocus как раз помечает.
            if (dragged == null || !DragFocus.IsPlayerClaimed(dragged)) return;
            if (!Notice(dragged.position)) NoticeSoft(dragged.position);
        }

        private void TickLiveliness()
        {
            _sinceThought += Time.deltaTime;

            // Мягкий взгляд гаснет сам — голова вернётся к нейтрали в FarmerHeadLook.
            if (_softGlanceHold > 0f) _softGlanceHold -= Time.deltaTime;

            TickGlances();
            TickMusings();
            TickWorkMusings();
        }

        /// <summary>
        /// Взгляды: стоя без дела, он то и дело находит, на что посмотреть, — с паузами
        /// и плавным поворотом, а не постоянным вращением.
        /// </summary>
        private void TickGlances()
        {
            // Спокойные состояния, где корпусом не рулит ни ходок, ни работа. Awaiting теперь
            // тоже здесь: его довод к грядке стал плавным и уступает взгляду (_glanceHold),
            // так что прежней драки поворотов, из-за которой его исключали, больше нет —
            // ожидающий постоит, поглазеет по сторонам и вернётся взглядом к грядке.
            bool calm = _state == FarmerState.Idle || _state == FarmerState.Relaxing ||
                        _state == FarmerState.Awaiting;
            if (!calm)
            {
                _glanceHold = 0f;
                return;
            }

            if (_glanceHold > 0f)
            {
                _glanceHold -= Time.deltaTime;
                TurnTowards(_glancePoint, _glanceTurnSpeed);
                return;
            }

            _glanceTimer -= Time.deltaTime;
            if (_glanceTimer > 0f) return;

            _glanceTimer = UnityEngine.Random.Range(_glanceInterval.x, Mathf.Max(_glanceInterval.x, _glanceInterval.y));
            _glancePoint = PickGlancePoint();
            _glanceHold = UnityEngine.Random.Range(0.9f, 1.8f);
        }

        /// <summary>Куда естественно смотреть: живое интереснее грядок, грядки интереснее пустоты.</summary>
        private Vector3 PickGlancePoint()
        {
            var animal = NearestLivestock(10f);
            if (animal != null && UnityEngine.Random.value < 0.6f) return animal.transform.position;

            var ready = GrowableRegistry.FindNearestReady(transform.position, null, 12f);
            if (ready != null && UnityEngine.Random.value < 0.6f) return ready.transform.position;

            var dir = UnityEngine.Random.insideUnitCircle.normalized;
            return transform.position + new Vector3(dir.x, 0f, dir.y) * 4f;
        }

        private Growable NearestLivestock(float within)
        {
            Growable best = null;
            float bestSqr = within * within;

            var all = GrowableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var g = all[i];
                if (g == null || g.Category != ResourceCategory.Livestock || g.Phase == GrowthPhase.Empty) continue;

                float sqr = (g.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = g;
            }

            return best;
        }

        /// <summary>
        /// Праздные мысли в простое. Не журнал и не подсказка — свидетельство, что в голове
        /// что-то происходит и между делами; именно этого «между» автоматам не хватает.
        /// </summary>
        private void TickMusings()
        {
            bool idleLike = _state == FarmerState.Idle || _state == FarmerState.Wandering ||
                            _state == FarmerState.Relaxing || _state == FarmerState.Awaiting;
            if (!idleLike) return;

            _musingTimer -= Time.deltaTime;
            if (_musingTimer > 0f) return;

            // Свежая мысль-решение важнее колорита: она телеграфирует выбор.
            if (_sinceThought < 6f) return;

            _musingTimer = UnityEngine.Random.Range(_musingInterval.x, Mathf.Max(_musingInterval.x, _musingInterval.y));

            // Нехватка материала на задуманное вытесняет колорит: это не праздная мысль,
            // а единственный сигнал игроку о том, почему ферма не обустраивается.
            SetThought(MissingMaterialMusing() ?? PickMusing());
        }

        private string PickMusing()
        {
            var clock = DayNightCycle.Instance;

            if (clock != null && clock.IsNight)
                return Pick("огонь трещит — хорошо", "звёзды нынче яркие", "тихо-то как…");

            // Ещё не «голоден» по порогу, но уже думает о еде — как и все мы.
            if (_needs != null && _needs.Satiety01 < 0.45f && !_needs.IsHungry)
                return Pick("перекусить бы вскоре", "в животе понемногу бурчит");

            if (GrowableRegistry.ReadyCount >= 6)
                return Pick("поспело-то сколько…", "урожай сам себя не соберёт");

            if (NearestLivestock(6f) != null)
                return Pick("жуёт себе и жуёт", "хорошая скотинка", "и им спокойно, и мне");

            if (clock != null && clock.DayProgress01 < 0.2f)
                return Pick("утро доброе", "роса ещё не сошла", "день будет долгий");

            if (clock != null && clock.DayProgress01 > 0.85f)
                return Pick("спина к вечеру гудит", "скоро закат", "день почти отработан");

            return Pick("хороший нынче день", "ветер приятный", "жить можно");
        }

        /// <summary>
        /// Мысли на работе — второй канал, заметно более редкий, чем праздный (45–90 секунд
        /// против 16–30): работающая голова занята делом, но не пуста. Раньше мысль-решение
        /// висела в облачке весь рейс неподвижно — 20 секунд застывшего текста подтверждали
        /// игроку, что внутри ничего не происходит.
        /// </summary>
        private void TickWorkMusings()
        {
            bool working = _state == FarmerState.GoingToHarvest || _state == FarmerState.ReturningHome ||
                           _state == FarmerState.GoingToMarket || _state == FarmerState.Hauling;
            if (!working) return;

            _workMusingTimer -= Time.deltaTime;
            if (_workMusingTimer > 0f) return;

            // Мысль-решение телеграфирует выбор — ей нужно пожить дольше, чем в простое.
            if (_sinceThought < 10f) return;

            _workMusingTimer = UnityEngine.Random.Range(45f, 90f);
            SetThought(PickWorkMusing());
        }

        /// <summary>
        /// Мысль о том, чего не хватает на задуманное. Отдельный, редкий канал: замысел,
        /// который упёрся в пустой склад, иначе выглядит как «фермеру ничего не хочется» —
        /// а это разные вещи, и игрок должен их различать.
        /// </summary>
        private string MissingMaterialMusing()
        {
            var missing = _brain != null ? _brain.MissingForImprovement : null;
            if (missing == null) return null;

            string what = missing.DisplayName;
            return Pick("не из чего мастерить — " + what + " бы",
                        what + " бы раздобыть, руки чешутся");
        }

        private string PickWorkMusing()
        {
            if (Inventory != null && Inventory.IsFull)
                return Pick("ноша тянет плечи", "полный короб — хорошо-то как");

            switch (_state)
            {
                case FarmerState.ReturningHome:
                    return Pick("дотащу — и славно", "шаг за шагом");

                case FarmerState.GoingToMarket:
                    return Pick("почём нынче возьмут?", "поторгуемся");

                case FarmerState.Hauling:
                    // Подноска пары — единственный момент, когда фермер может научить игрока его
                    // собственному ходу, ничего за него не решив: он ставит рядом и вслух
                    // отказывается сливать. Ровно до первого слияния — дальше это уже сюсюканье.
                    return FarmProgress.TotalMerges == 0
                        ? Pick("к такой же её — а свести уже тебе", "вот пара; дальше твоя рука")
                        : Pick("тут ей будет лучше", "к своим её, к своим");

                default:
                    if (_target != null && _target.Level > 1)
                        return Pick("ну и вымахало", "такое грех не снять");
                    return Pick("дела идут", "руки помнят");
            }
        }

        private static string Pick(params string[] options) =>
            options[UnityEngine.Random.Range(0, options.Length)];

        /// <summary>Плавный поворот к точке — в отличие от мгновенного <see cref="FaceTowards"/>.</summary>
        private void TurnTowards(Vector3 point, float degreesPerSecond)
        {
            Vector3 look = point - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(look, Vector3.up),
                degreesPerSecond * Time.deltaTime);
        }

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

        /// <summary>
        /// Быт: дойти до места и побыть там. Уходит отсюда, как только появится дело.
        /// <para>
        /// Осматривается он не здесь: раньше тут крутилось постоянное вращение, и оно
        /// читалось как турель. Отдельные взгляды с паузами живут в <see cref="TickGlances"/>
        /// и обслуживают заодно простой и ожидание.
        /// </para>
        /// </summary>
        private void TickRelaxing()
        {
            if (Rethink()) return;
            if (!_mover.HasArrived) _mover.SetDestination(_relaxSpot);
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

            // Грядку несут — в руке игрока она не цель: бежать за точкой под курсором
            // и снять урожай из чужих рук значило бы разрушить подготовленное слияние.
            if (DragFocus.IsDragged(_target.transform))
            {
                _target = null;
                EnterIdle();
                return;
            }

            if (_target.IsReady)
            {
                // Текущее решение переписывается на сбор: переход идёт мимо Commit, и без
                // этого интент оставался «ждать» — свежий выбор сбора той же грядки выглядел
                // новым делом, и фермер зависал в лишнем «раздумье» у только что созревшего.
                _current = new FarmerDecision(FarmerIntent.Harvest, _currentScore, _current.Thought,
                    plot: _target);
                EnterGoingToHarvest(_target);
                return;
            }

            // Ждать перестало иметь смысл: игрок унёс ветряк и рост замедлился, грядка увяла
            // или пересажена. Раньше выхода отсюда не было, и фермер стоял у растения минуты —
            // замороженная оценка с маржой не пропускали никакое другое дело.
            if (_target.Phase != GrowthPhase.Growing ||
                _target.TimeUntilReady > FarmerBrain.WorthWaiting * 1.25f)
            {
                _target = null;
                EnterIdle();
                return;
            }

            if (!_mover.HasArrived)
            {
                _mover.SetDestination(_target.transform.position);
                return;
            }

            // Плавный довод к грядке — и только когда взгляд не занят чем-то поинтереснее:
            // мгновенный FaceTowards каждый кадр делал из него турель.
            if (_glanceHold <= 0f) TurnTowards(_target.transform.position, _glanceTurnSpeed);

            // Вот-вот поспеет — дотерпим. Разворот к колодцу за три секунды до спелости
            // читается как поломка, а жажда не убивает: минута терпения ничего не стоит.
            if (_target.TimeUntilReady < 5.0) return;

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

            // Метка живёт, пока живо дело: перестал продлевать — протухла сама.
            TidyClaims.Stamp(this, _target);

            if (_mover.HasArrived)
            {
                EnterHauling();
                return;
            }

            // Уборка — самое дешёвое из дел, и бросить дорогу к ней ничего не стоит:
            // созревшее в эту минуту не должно ждать конца перестановки. Ноши ещё нет,
            // поэтому пересмотр здесь безопасен — в отличие от Hauling, где руки заняты.
            if (Rethink(_switchMargin)) return;

            _mover.SetDestination(_target.transform.position);
        }

        /// <summary>Несём грядку на новое место.</summary>
        private void TickHauling()
        {
            if (_target == null || !DragFocus.IsDragged(_target.transform))
            {
                // Груз перехватили или он исчез — руки пусты, идём заниматься другим.
                // Но сначала опустить на землю то, что ещё есть: без этого перехваченная
                // грядка так и висела бы в воздухе на высоте переноски.
                if (_target != null)
                {
                    var dropped = _target.transform.position;
                    dropped.y = FarmingRuntime.Ground.SampleHeight(dropped);
                    _target.transform.position = dropped;
                }

                Release();
                EnterIdle();
                return;
            }

            // Метка живёт и на ходке с ношей — иначе на долгой дороге она протухла бы,
            // и сосед посчитал бы несомую грядку свободной парой для своей уборки.
            TidyClaims.Stamp(this, _target);

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

            // Довёл дело до конца — накопленное раздражение отпускает. Единственное место,
            // где оно сбрасывается: уборка засчитывается по поставленной грядке, а не по
            // принятому решению, иначе брошенная на полдороге считалась бы за сделанную.
            _brain.MessTended();

            Release();
            EnterIdle();
        }

        /// <summary>Можно ли всё ещё переставлять взятую на прицел грядку.</summary>
        private bool CanStillTidy()
        {
            if (_target == null || _target.Phase == GrowthPhase.Empty) return false;

            // Созрела по дороге — правило мозга «спелое сначала собирают» действует и тут:
            // нести спелую грядку через ферму вместо сбора выглядит как издевательство.
            if (_target.IsReady) return false;

            // Сосед застолбил раньше (гонка одного кадра на входе). Дело ещё не начато,
            // и уступка отсюда выглядит обычной сменой намерения, а не поломкой.
            if (TidyClaims.HeldByOther(this, _target)) return false;

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

            // Несомую игроком не преследуем: гнаться за точкой под курсором и собрать
            // из руки — сломать слияние, которое игрок как раз готовит.
            if (DragFocus.IsDragged(_target.transform))
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

            // Передумать на ходу можно — но только ради заметно лучшего, и не в двух шагах
            // от цели: развернуться к колодцу у самой грядки читается как поломка, а урожай
            // будет снят за секунды.
            if ((transform.position - _target.transform.position).sqrMagnitude > 16f &&
                Rethink(_switchMargin)) return;

            _mover.SetDestination(_target.transform.position);
        }

        private void TickHarvesting()
        {
            TickFacing();

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
            else
            {
                EnterIdle();

                // Редкий выдох между грядками — телесная пунктуация вместо конвейера.
                // Ничего не задерживает: в Idle пересмотр дел идёт до таймера паузы.
                if (UnityEngine.Random.value < 0.2f) SetThought(Pick("фух…", "дальше"));
            }
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

            // Лимитная перегрузка, а не силовая: статический тип Sink — IResourceSink, и
            // без приведения компилятор выбирал overload, который очищает рюкзак целиком —
            // при полном складе весь груз уничтожался, а Delivered и звук награды
            // рапортовали об успехе. Остаток теперь честно остаётся в рюкзаке.
            int moved = FarmingRuntime.Sink is IInventory storage
                ? Inventory.TransferTo(storage)
                : Inventory.TransferTo(FarmingRuntime.Sink);

            if (moved > 0)
            {
                Raise(Delivered, moved);
                if (_skills != null) _skills.Grant(FarmerSkill.Back, moved * 1.2f);
            }

            _plotsThisTrip = 0;
            EnterIdle();

            // Склад не принял всё — отказ обязан быть заметным, и виден он в двух местах:
            // здесь мыслью и в HUD строкой «полон!». Мозг с остатком в рюкзаке доставку
            // больше не предложит — рюкзак разгрузится, когда игрок освободит полки.
            if (!Inventory.IsEmpty)
            {
                SetThought("склад полон, некуда класть…");
                return;
            }

            // Большая ходка заслуживает выдоха — и в мысли, и в лишней секунде передышки.
            if (moved >= 12 && UnityEngine.Random.value < 0.6f)
            {
                SetThought("ну и денёк — полный воз");
                _timer += 1f;
            }
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
            TickFacing();

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            if (_serviceTarget != null && _needs != null)
            {
                // Текущие значения нужд уходят в постройку: визит наполняет до потолка
                // её уровня, и для этого ей надо знать, сколько уже налито.
                _serviceTarget.Serve(_needs.Satiety, _needs.MaxSatiety,
                                     _needs.Hydration, _needs.MaxHydration,
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
            TickFacing();

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

        /// <summary>Дорога к месту стройки. Передумать можно — замысел подождёт, голод нет.</summary>
        private void TickGoingToImprove()
        {
            if (_project == null) { EnterIdle(); return; }

            if (_mover.HasArrived)
            {
                FaceTowards(_projectSpot);
                _timer = _project.BuildSeconds;
                SetState(FarmerState.Improving);
                return;
            }

            if (Rethink(_switchMargin)) return;
            _mover.SetDestination(_projectSpot);
        }

        /// <summary>
        /// Мастерит. Без пересмотра решений, как и сбор: работа короткая, и бросить её на
        /// полпути значило бы показать игроку человека, который передумал посреди удара молотком.
        /// </summary>
        private void TickImproving()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            CompleteImprovement();
        }

        private void CompleteImprovement()
        {
            var project = _project;
            var anchor = _projectAnchor;
            _project = null;
            _projectAnchor = null;

            if (project == null || project.Prefab == null) { EnterIdle(); return; }

            // Дворик осиротел, пока он шёл: постройку-якорь унесли из игры. Строить
            // стог посреди пустого места — ровно то, от чего ансамбли и лечат.
            if (project.Anchor == ImprovementAnchor.Building && anchor == null)
            {
                SetThought("а строить-то уже не у чего…");
                EnterIdle();
                return;
            }

            // Материалы могли уйти, пока он шёл и мастерил, — тогда честно бросаем без вещи.
            var cost = project.Cost;
            if (cost.IsValid)
            {
                var storage = FarmingRuntime.Sink as IInventory;
                if (storage == null || storage.GetAmount(cost.Resource) < cost.Amount)
                {
                    SetThought("материала не хватило…");
                    EnterIdle();
                    return;
                }
                storage.TryRemove(cost.Resource, cost.Amount);
            }

            var spot = _projectSpot;
            spot.y = FarmingRuntime.Ground.SampleHeight(spot);

            var built = Instantiate(project.Prefab, spot,
                Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            built.name = "Improvement_" + project.Id;

            // Построенное жителем игрок волен переставить — то же правило, что у покупок.
            if (built.GetComponent<Movable>() == null) built.AddComponent<Movable>();

            if (anchor != null)
            {
                if (!_builtAt.TryGetValue(anchor, out var yard))
                    _builtAt[anchor] = yard = new Dictionary<ImprovementDefinition, int>();
                yard.TryGetValue(project, out int at);
                yard[project] = at + 1;
            }
            else
            {
                _builtCount.TryGetValue(project, out int count);
                _builtCount[project] = count + 1;
            }

            RefreshImprovementDay();
            _builtToday++;
            _lastImprovedAt = Time.time;   // желание утолено — зреет заново

            if (_skills != null) _skills.Grant(FarmerSkill.Wits, 5f);

            SetThought("готово. глаз радуется");
            Emote(FarmerEmote.Happy);

            var handler = Improved;
            if (handler != null)
            {
                try { handler(this, project, built); }
                catch (Exception e) { Debug.LogException(e, this); }
            }

            EnterIdle();
        }

        /// <summary>
        /// Дорога до кровати. Передумать по пути нельзя: усталость и без того выигрывает
        /// у всего остального, и повторный выбор просто заставил бы его замирать на каждом
        /// шагу с той же мыслью.
        /// </summary>
        private void TickGoingToSleep()
        {
            if (_mover.HasArrived)
            {
                EnterSleeping();
                return;
            }

            _mover.SetDestination(_bedPosition);
        }

        private void TickSleeping()
        {
            if (_needs == null) { EnterIdle(); return; }

            _needs.Sleep(Time.deltaTime);

            // Просыпается, когда выспался и рассвело. Ночью выспавшийся продолжает лежать —
            // иначе он всю ночь будет вставать и снова ложиться.
            bool night = _sleepAtNight && DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
            if (_needs.IsRested && !night)
            {
                // Проснуться — это момент, а не переключение бита: постоять, оглядеться,
                // и только потом дела. Телесное потягивание играет FarmJuice по смене состояния.
                SetThought("выспался. что тут у нас?");
                if (UnityEngine.Random.value < 0.5f) Emote(FarmerEmote.Happy);

                EnterIdle();
                _timer = Mathf.Max(_timer, UnityEngine.Random.Range(1.2f, 2f));
            }
        }

        #endregion

        #region Переходы

        private void EnterIdle()
        {
            _mover.Stop();

            // Недоведённый разворот к прошлой цели не должен доигрываться в новом деле.
            _hasFacePoint = false;

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
            // Застолбить на входе: сосед, выбравший ту же грядку в этом же кадре, увидит
            // чужую метку в первом своём тике и отступится (TidyClaims не даст перебить).
            TidyClaims.Stamp(this, plot);

            _target = plot;
            _tidySpot = spot;
            _mover.SetDestination(plot.transform.position);
            SetState(FarmerState.GoingToTidy);
        }

        private void EnterGoingToImprove(ImprovementDefinition project, Vector3 spot, Building anchor)
        {
            _project = project;
            _projectSpot = spot;
            _projectAnchor = anchor;
            _mover.SetDestination(spot);
            SetState(FarmerState.GoingToImprove);
        }

        /// <summary>
        /// Взять грядку в руки. Занимаем тот же слот, что и мышь игрока: нести её одновременно
        /// вдвоём нельзя, а животное по дороге должно перестать брести само.
        /// </summary>
        private void EnterHauling()
        {
            if (_target == null) { EnterIdle(); return; }

            // Слот занят — игрок что-то тащит или другой житель уже несёт. Перехват выдёргивал
            // бы ношу из чужих рук; с реестром жителей это больше не гипотетический случай.
            if (DragFocus.Current != null)
            {
                _target = null;
                EnterIdle();
                return;
            }

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

            // Пока работает — лицом к грядке. Довод плавный (см. Face/TickFacing): ходок
            // подводит почти лицом, и мгновенный щелчок на остаточные 20–40° был мелким,
            // но регулярным маркером автомата у каждой грядки.
            if (_target != null) Face(_target.transform.position);

            float speed = _skills != null ? _skills.HarvestSpeed : 1f;

            // Лёгкая дрожь длительности: одинаковые до кадра сборы стучат как метроном,
            // а метроном — первое, что выдаёт автомат даже боковым зрением.
            _timer = _harvestDuration / Mathf.Max(0.1f, speed) * UnityEngine.Random.Range(0.85f, 1.2f);
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
            _timer = _depositDuration * UnityEngine.Random.Range(0.85f, 1.25f);   // та же анти-метрономная дрожь
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
            if (_serviceTarget != null) Face(_serviceTarget.transform.position);

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
            if (_market != null) Face(_market.transform.position);
            _timer = _sellDuration;
            SetState(FarmerState.AtMarket);
        }

        // ---- мастеровой у станка (этап 2 колонии) ----

        /// <summary>Станок, к которому идёт или у которого стоит мастеровой.</summary>
        private Workshop _craftShop;

        private void EnterGoingToCraft(Building place)
        {
            _craftShop = place != null ? place.GetComponent<Workshop>() : null;
            if (_craftShop == null) { EnterIdle(); return; }

            _mover.SetDestination(place.transform.position);
            SetState(FarmerState.GoingToCraft);
        }

        private void TickGoingToCraft()
        {
            // Станок снесли или партии кончились вместе с сырьём — идти больше незачем.
            if (_craftShop == null || !_craftShop.isActiveAndEnabled || !_craftShop.IsWorking)
            {
                _craftShop = null;
                EnterIdle();
                return;
            }

            if (_mover.HasArrived) { EnterCrafting(); return; }

            // Дорога к станку бросается легко, как дорога к уборке: ноша ещё не взята.
            if (Rethink(_switchMargin)) return;

            _mover.SetDestination(_craftShop.transform.position);
        }

        private void EnterCrafting()
        {
            if (_craftShop == null) { EnterIdle(); return; }

            _mover.Stop();
            Face(_craftShop.transform.position);

            // Опыт Ремесла — по готовым партиям: станок объявляет их всем желающим, а
            // мастеровой слушает, только пока стоит рядом. Отписка — в SetState, через
            // неё проходят все выходы из состояния без исключения.
            _craftShop.Produced += OnCraftProduced;

            SetState(FarmerState.Crafting);
        }

        private void TickCrafting()
        {
            // Порядок в выходах не косметика: сперва EnterIdle — его SetState отписывает
            // от партий станка, пока ссылка жива, — и только потом обнуление ссылки.
            if (_craftShop == null || !_craftShop.isActiveAndEnabled)
            {
                EnterIdle();
                _craftShop = null;
                return;
            }

            // Перерыв уводит от станка явно: партии бесконечны, и «доведу до конца», как
            // у грядки, здесь не наступит никогда. Причина — вслух, по правилу перерыва.
            if (_brain.IsResting)
            {
                SetThought("передохну");
                EnterIdle();
                _craftShop = null;
                return;
            }

            // Сырьё кончилось — стоять над пустым станком нечего, пусть быт заберёт.
            if (!_craftShop.IsWorking)
            {
                SetThought("стружка вышла — сырья бы");
                EnterIdle();
                _craftShop = null;
                return;
            }

            // Метка скорости продлевается каждый тик и протухает сама, если мастеровой ушёл.
            _craftShop.SetTendSpeed(_skills != null ? _skills.CraftSpeed : 1f);

            if (_glanceHold <= 0f) TurnTowards(_craftShop.transform.position, _glanceTurnSpeed);

            Rethink(_switchMargin);
        }

        /// <summary>Партия вышла при мастеровом — его Ремесло растёт от сделанной работы.</summary>
        private void OnCraftProduced(Workshop shop, WorkshopRecipe recipe, int stored)
        {
            if (_state != FarmerState.Crafting || shop != _craftShop || recipe == null) return;

            // Порядок величин как у сбора, но партии куда чаще — потому доли, не единицы.
            if (_skills != null)
                _skills.Grant(FarmerSkill.Crafting, 0.8f + recipe.OutputValue * 0.01f);
        }

        // ---- возчик (этап 2 колонии) ----

        /// <summary>
        /// Сколько сданных заказов ждут ходки с коробом. Театр труда: слот доски даёт сама
        /// метка найма, а ходка — шум вокруг решения игрока, никогда вместо него.
        /// Потолок в три: доска сдана залпом — возчик не обязан отрабатывать каждый клик.
        /// </summary>
        private int _pendingDeliveries;

        /// <summary>Публичный не ради UI, а ради проверяемости: очередь ходок читают тесты.</summary>
        public int PendingDeliveries => _pendingDeliveries;

        /// <summary>Рынок, к которому едет короб. Память дороги — как станок у мастерового.</summary>
        private Building _orderMarket;

        private void OnOrderFilled(FarmOrder order)
        {
            // Роль здесь, а не на подписке: см. комментарий в OnEnable про порядок Awake.
            if (_role != ResidentRole.Carter || !IsHired) return;
            if (_pendingDeliveries < 3) _pendingDeliveries++;
        }

        private void EnterGoingToLoad()
        {
            _mover.SetDestination(_homePosition);
            SetState(FarmerState.GoingToLoad);
        }

        private void TickGoingToLoad()
        {
            // Дорога за коробом бросается легко: ноши ещё нет.
            if (Rethink(_switchMargin)) return;

            if (!_mover.HasArrived)
            {
                _mover.SetDestination(_homePosition);
                return;
            }

            // Короб взят — теперь к рынку. Рынок мог пропасть, пока шли: ходка сгорает,
            // а слот доски цел — он держится наймом, не театром.
            _orderMarket = null;
            foreach (var building in BuildingRegistry.All)
                if (building != null && building.Service == BuildingService.Market)
                { _orderMarket = building; break; }

            if (_orderMarket == null)
            {
                _pendingDeliveries = 0;
                EnterIdle();
                return;
            }

            SetThought("повезу заказ горожанам");
            _mover.SetDestination(_orderMarket.transform.position);
            SetState(FarmerState.DeliveringOrder);
        }

        private void TickDeliveringOrder()
        {
            if (_orderMarket == null)
            {
                _pendingDeliveries = Mathf.Max(0, _pendingDeliveries - 1);
                EnterIdle();
                return;
            }

            if (!_mover.HasArrived)
            {
                _mover.SetDestination(_orderMarket.transform.position);
                return;
            }

            // Довёз. Спина помнит короб — тем же зерном, что доставка урожая.
            _pendingDeliveries = Mathf.Max(0, _pendingDeliveries - 1);
            if (_skills != null) _skills.Grant(FarmerSkill.Back, 2f);

            SetThought("сдал в лучшем виде");
            _orderMarket = null;
            EnterIdle();
        }

        /// <summary>
        /// Собраться и пойти спать домой.
        /// <para>
        /// Раньше он ложился там, где стоял: правилу «ничто не зависит от места сна» это не
        /// противоречило, но выглядело так, будто персонаж вырубается посреди поля. У дома
        /// есть смысл именно потому, что туда возвращаются, — иначе это просто точка выгрузки.
        /// Дорога ничего не стоит: спать он идёт, когда работа всё равно кончилась.
        /// </para>
        /// </summary>
        private void EnterGoingToSleep()
        {
            Emote(FarmerEmote.Sleep);
            Release();   // уснуть с грядкой в руках нельзя — она осталась бы висеть в воздухе
            _target = null;
            _serviceTarget = null;
            _market = null;

            _mover.SetDestination(_bedPosition);
            SetState(FarmerState.GoingToSleep);
        }

        /// <summary>Лечь. Вызывается по прибытии домой, а до того — только через <see cref="EnterGoingToSleep"/>.</summary>
        private void EnterSleeping()
        {
            _mover.Stop();
            SetState(FarmerState.Sleeping);
        }

        private void FaceTowards(Vector3 position)
        {
            Vector3 look = position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }

        /// <summary>Повернуться к цели плавно: запомнить точку, довод делает <see cref="TickFacing"/>.</summary>
        private void Face(Vector3 position)
        {
            _facePoint = position;
            _hasFacePoint = true;
        }

        /// <summary>
        /// Довод лица к рабочей цели, градусов 360 в секунду: быстро, но без телепорта.
        /// Зовётся из стоячих рабочих состояний; работе не мешает — таймер дела идёт параллельно.
        /// </summary>
        private void TickFacing()
        {
            if (_hasFacePoint) TurnTowards(_facePoint, 360f);
        }

        private void SetState(FarmerState state)
        {
            if (_state == state) return;

            // Единственная дверь из состояния «у станка» — здесь и отписка от его партий:
            // выходов из Crafting полдюжины, а SetState минуют только они все разом.
            if (_state == FarmerState.Crafting && _craftShop != null)
                _craftShop.Produced -= OnCraftProduced;

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

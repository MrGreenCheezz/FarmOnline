using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    public enum FarmerState
    {
        /// <summary>Standing still between wander legs.</summary>
        Idle = 0,
        /// <summary>Strolling to a random point around home.</summary>
        Wandering = 1,
        /// <summary>Walking toward a ripe growable it has claimed.</summary>
        GoingToHarvest = 2,
        /// <summary>Standing at the plot, working.</summary>
        Harvesting = 3,
        /// <summary>Carrying the load back to the home point.</summary>
        ReturningHome = 4,
        /// <summary>At home, unloading into world storage.</summary>
        Depositing = 5,
        /// <summary>Walking to a kitchen or well because a need ran low.</summary>
        GoingToService = 6,
        /// <summary>Eating or drinking.</summary>
        UsingService = 7,
        /// <summary>Walking to the market — to sell, to buy, or both.</summary>
        GoingToMarket = 8,
        /// <summary>Standing at the market, doing business.</summary>
        AtMarket = 9,
        /// <summary>Asleep at home — night, or worn out.</summary>
        Sleeping = 10
    }

    /// <summary>
    /// The farmer, in its first form: wander → notice something ripe → harvest it → carry it home.
    /// <para>
    /// It never scans the scene. Ripe plots come from <see cref="GrowableRegistry"/>, which the
    /// farming system keeps up to date, so the cost of looking for work is proportional to how much
    /// work exists — not to the size of the farm.
    /// </para>
    /// <para>
    /// Crops go into the character's <see cref="CharacterInventory"/> at the plot and only reach
    /// <see cref="FarmingRuntime.Sink"/> once the character has physically walked home. Drop the
    /// character mid-trip and the load is genuinely lost, which is what makes hauling matter.
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

        [Tooltip("Как часто искать работу, секунд. Чаще — отзывчивее, но и дороже.")]
        [SerializeField, Min(0.05f)] private float _scanInterval = 0.25f;

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

        [Tooltip("Столько излишка он бросает ради него сбор урожая. Ниже — торгует только в простой.")]
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

        private AgentMover _mover;
        private CharacterInventory _inventoryComponent;
        private CharacterNeeds _needs;
        private FarmerSkills _skills;
        private FarmerState _state = FarmerState.Idle;
        private Growable _target;
        private Building _serviceTarget;
        private Building _market;
        private Vector3 _homePosition;
        private float _timer;
        private float _scanTimer;
        private float _baseSpeed = 1.5f;
        private int _baseCapacity = 8;
        private int _plotsThisTrip;
        private Vector3 _lastPosition;

        /// <summary>Fired whenever the farmer switches activity — handy for animation and UI.</summary>
        public event Action<FarmerAgent, FarmerState> StateChanged;

        /// <summary>Picked something up. It is in the backpack, not in storage.</summary>
        public event Action<FarmerAgent, HarvestResult> Collected;

        /// <summary>Unloaded at home. The int is how many units were handed to world storage.</summary>
        public event Action<FarmerAgent, int> Delivered;

        /// <summary>Ate or drank at a building.</summary>
        public event Action<FarmerAgent, Building> Refreshed;

        /// <summary>Sold at the market on his own. The int is the gold he brought in.</summary>
        public event Action<FarmerAgent, int> Traded;

        /// <summary>Bought himself a new plot with the takings.</summary>
        public event Action<FarmerAgent, ShopItemDefinition> Restocked;

        public FarmerState State => _state;
        public Growable Target => _target;
        public Vector3 HomePosition => _homePosition;

        /// <summary>His skills, or null when the component is absent — everything degrades to level 1.</summary>
        public FarmerSkills Skills => _skills;

        /// <summary>True while he is asleep. The HUD greys itself out on this.</summary>
        public bool IsAsleep => _state == FarmerState.Sleeping;

        /// <summary>
        /// What the farmer is carrying. Capacity lives on <see cref="CharacterInventory"/>.
        /// <para>
        /// Resolved on demand rather than only in <c>Awake</c>: a HUD on another GameObject can
        /// reach this from its own <c>OnEnable</c>, which Unity may run before ours.
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

        private void Awake()
        {
            _mover = GetComponent<AgentMover>();
            if (_mover == null)
                Debug.LogError("[Farmer] Нужен компонент AgentMover (например SimpleMover)", this);

            EnsureInventory();

            _needs = GetComponent<CharacterNeeds>();
            _skills = GetComponent<FarmerSkills>();

            if (_mover != null) _baseSpeed = _mover.Speed;
            _baseCapacity = _inventoryComponent.Capacity;

            if (_skills != null) _skills.LevelledUp += OnLevelledUp;
        }

        private void OnDestroy()
        {
            if (_skills != null) _skills.LevelledUp -= OnLevelledUp;
        }

        /// <summary>Capacity is re-derived rather than incremented, so it survives a reload intact.</summary>
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

        private void Start()
        {
            _homePosition = _home != null ? _home.position : transform.position;
            _lastPosition = transform.position;
            ApplyCapacity();
            EnterIdle();
        }

        private void Update()
        {
            if (_mover == null) return;

            // Голод, жажда и усталость бьют по скорости. Не по «жив/мёртв» — иначе игрок теряет
            // партию, ничего не в силах исправить. Навык ног работает поверх штрафа.
            float skillSpeed = _skills != null ? _skills.MoveSpeed : 1f;
            _mover.Speed = _baseSpeed * (_needs != null ? _needs.Productivity01 : 1f) * skillSpeed;

            TrackEffort();

            switch (_state)
            {
                case FarmerState.Idle: TickIdle(); break;
                case FarmerState.Wandering: TickWandering(); break;
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
        /// Turn movement into tiredness and into leg experience.
        /// <para>
        /// Measured from distance actually covered, not from time spent in a walking state: a farmer
        /// stuck against a fence should not be getting fitter and more exhausted for standing still.
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

        #region States

        private void TickIdle()
        {
            if (TryTakeWork()) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f) EnterWander();
        }

        private void TickWandering()
        {
            if (TryTakeWork()) return;
            if (_mover.HasArrived) EnterIdle();
        }

        private void TickGoingToHarvest()
        {
            // Someone else got there first, or it withered while we walked.
            if (_target == null || !_target.IsReady)
            {
                _target = null;
                if (!TryTakeWork()) EnterIdle();
                return;
            }

            // Check arrival BEFORE re-issuing the destination: setting it again clears the mover's
            // arrived flag, so doing it first would mean we never see that we got there.
            if (_mover.HasArrived)
            {
                EnterHarvesting();
                return;
            }

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

                    // Намётанный глаз: иногда со грядки снимается лишнее.
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

        private void TickGoingToService()
        {
            // Пока шли, еду могли продать, а постройку — снести
            if (_serviceTarget == null ||
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

        // ---- торговля ----

        private void TickGoingToMarket()
        {
            // Рынок могли снести, а склад — опустошить, пока он шёл.
            if (_market == null || !HasMarketErrand())
            {
                _market = null;
                EnterIdle();
                return;
            }

            if (_mover.HasArrived) { EnterAtMarket(); return; }
            _mover.SetDestination(_market.transform.position);
        }

        /// <summary>
        /// One visit does both halves of the trade: sell the surplus, then spend the takings.
        /// <para>
        /// Together rather than as two errands because that is the point of the trip — a farmer who
        /// walks to the market, sells, walks home and then walks back to buy looks broken, and the
        /// gold from the sale is exactly what pays for the purchase.
        /// </para>
        /// </summary>
        private void TickAtMarket()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            var shop = Shop.Instance;

            if (shop != null && HasSurplus(out var resource, out int amount))
            {
                int gold = shop.TrySell(resource, amount);
                if (gold > 0)
                {
                    Raise(Traded, gold);
                    if (_skills != null) _skills.Grant(FarmerSkill.Wits, 2f + gold * 0.02f);
                }
            }

            var purchase = PickRestock();
            if (purchase != null && shop != null && shop.TryBuy(purchase))
            {
                Raise(Restocked, purchase);
                if (_skills != null) _skills.Grant(FarmerSkill.Wits, 6f);
            }

            _market = null;
            EnterIdle();
        }

        // ---- сон ----

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

        #region Transitions

        private void EnterIdle()
        {
            _mover.Stop();
            _timer = UnityEngine.Random.Range(_idlePauseMin, Mathf.Max(_idlePauseMin, _idlePauseMax));
            SetState(FarmerState.Idle);
        }

        private void EnterWander()
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * _wanderRadius;
            _mover.SetDestination(_homePosition + new Vector3(offset.x, 0f, offset.y));
            SetState(FarmerState.Wandering);
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

            // Face the plot while working.
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

            if (_serviceTarget != null)
            {
                Vector3 look = _serviceTarget.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }

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

        private void EnterSleeping()
        {
            _mover.Stop();
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

        #endregion

        #region Decisions

        /// <summary>
        /// Look for something worth doing. Returns true if the state changed.
        /// Throttled — re-querying every frame buys nothing when crops ripen seconds apart.
        /// </summary>
        private bool TryTakeWork()
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f) return false;
            _scanTimer = _scanInterval;

            // Сон раньше всего: работать без сил — значит работать медленно и всё равно уснуть.
            if (TryGoToBed()) return true;

            // Собственные нужды раньше работы: голодный фермер работает медленнее, и чем дольше
            // он терпит, тем меньше успевает. Сходить поесть — это не пауза, это вложение.
            if (TryTakeCare()) return true;

            if (Inventory.IsFull)
            {
                EnterReturningHome();
                return true;
            }

            // Склад забит настолько, что дальше собирать бессмысленно — сначала на рынок.
            // Без этой проверки продажа стоит в очереди после сбора и на живой ферме
            // не выполняется никогда: спелое есть всегда.
            if (Inventory.IsEmpty && TryGoToMarket(_urgentSurplus)) return true;

            var target = FindTarget();
            if (target != null)
            {
                EnterGoingToHarvest(target);
                return true;
            }

            // Nothing ripe within reach — no reason to keep hauling a part-load around.
            if (!Inventory.IsEmpty)
            {
                EnterReturningHome();
                return true;
            }

            // Урожая нет, руки свободны — самое время отнести излишки на рынок.
            if (TryGoToMarket(0)) return true;

            return false;
        }

        /// <summary>
        /// Go to bed when night falls or when he is worn out. Returns true when the state changed.
        /// <para>
        /// He sleeps where he stands rather than walking home first. Nothing in the farm depends on
        /// where he sleeps, and a farmer who collapses at the far fence and then trudges home before
        /// he may rest reads as punishment for a decision the player never made.
        /// </para>
        /// </summary>
        private bool TryGoToBed()
        {
            if (_needs == null) return false;

            bool night = _sleepAtNight && DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
            if (!night && !_needs.IsTired) return false;

            // С полным рюкзаком сначала разгрузиться: уснуть с урожаем на руках — потерять день.
            if (!Inventory.IsEmpty && _state != FarmerState.ReturningHome)
            {
                EnterReturningHome();
                return true;
            }

            if (!Inventory.IsEmpty) return false;

            EnterSleeping();
            return true;
        }

        /// <summary>
        /// Head to the market when there is business to do there.
        /// <paramref name="minimum"/> raises the selling bar so an urgent check ignores small piles;
        /// a purchase is always worth the walk, so it is not gated by it.
        /// </summary>
        private bool TryGoToMarket(int minimum)
        {
            if (_skills == null || !_skills.CanSell) return false;
            if (Shop.Instance == null) return false;

            bool worthSelling = HasSurplus(out _, out int amount) && amount >= minimum;
            if (!worthSelling && PickRestock() == null) return false;

            var market = FindMarket();
            if (market == null) return false;

            EnterGoingToMarket(market);
            return true;
        }

        /// <summary>Anything left to do at the market at all.</summary>
        private bool HasMarketErrand() => HasSurplus(out _, out _) || PickRestock() != null;

        /// <summary>
        /// The best plot he is willing to buy for himself, or null.
        /// <para>
        /// Two rules keep his spending from trampling the player's plans. He never dips below the
        /// gold reserve, and he only buys what costs plain gold — the material stockpile is what the
        /// player is saving for buildings, and spending planks on saplings on their behalf is one
        /// step past helpful. Buildings he never buys at all: where the kitchen goes is a decision,
        /// not a chore.
        /// </para>
        /// </summary>
        private ShopItemDefinition PickRestock()
        {
            if (_skills == null || !_skills.CanRestock) return null;
            if (GrowableRegistry.Count >= _maxPlots) return null;

            var shop = Shop.Instance;
            var catalog = shop != null ? shop.Catalog : null;
            if (catalog == null) return null;

            int gold = shop.Wallet != null ? shop.Wallet.Gold : 0;

            ShopItemDefinition best = null;
            int bestPrice = 0;

            var items = catalog.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.Kind != ShopItemKind.Plot) continue;

                var price = item.Price;
                if (price.Resources != null && price.Resources.Length > 0) continue;
                if (gold - price.Gold < _goldReserve) continue;
                if (!shop.CanBuy(item, out _)) continue;

                // Самое дорогое из посильного: ферма растёт вверх по ступеням, а не вширь
                // одной пшеницей.
                if (best != null && price.Gold <= bestPrice) continue;

                best = item;
                bestPrice = price.Gold;
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

        /// <summary>
        /// The biggest stack worth selling, or false when nothing qualifies.
        /// <para>
        /// Food keeps a bigger reserve than materials on purpose: the kitchen eats out of the same
        /// store, and a farmer who sold the last of the wheat has just made himself unfeedable.
        /// </para>
        /// </summary>
        private bool HasSurplus(out ResourceDefinition resource, out int amount)
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

                int keep = r.IsFood ? _keepFood : _keepMaterials;
                int spare = entries[i].Amount - keep;
                if (spare < _minSaleBatch) continue;

                int value = spare * r.SellPrice;
                if (value <= bestValue) continue;

                bestValue = value;
                resource = r;
                amount = spare;
            }

            return resource != null;
        }

        /// <summary>
        /// Go eat or drink if a need is low and something can actually help.
        /// Returns true when the state changed.
        /// </summary>
        private bool TryTakeCare()
        {
            if (_needs == null) return false;
            if (!_needs.IsHungry && !_needs.IsThirsty) return false;

            // Когда просело только одно — идём к нужной постройке; когда оба, берём что ближе.
            BuildingService? want = null;
            if (_needs.IsThirsty && !_needs.IsHungry) want = BuildingService.Well;
            else if (_needs.IsHungry && !_needs.IsThirsty) want = BuildingService.Kitchen;

            var building = BuildingRegistry.FindNearestUseful(
                transform.position, _needs.Satiety01, _needs.Hydration01, want);

            if (building == null) return false;

            EnterGoingToService(building);
            return true;
        }

        private Growable FindTarget()
        {
            ResourceCategory? filter = _filterByCategory ? _category : (ResourceCategory?)null;
            return GrowableRegistry.FindNearestReady(transform.position, filter, _searchRadius);
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

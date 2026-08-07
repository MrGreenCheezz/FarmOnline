using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Покупка и продажа. Владеет правилами — что по карману, что списывается, что появляется, —
    /// поэтому UI только задаёт вопросы и никогда сам не трогает ни кошелёк, ни склад.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Shop")]
    public sealed class Shop : MonoBehaviour
    {
        [SerializeField] private ShopCatalog _catalog;
        [SerializeField] private Wallet _wallet;

        [Header("Куда ставить купленное")]
        [Tooltip("Центр расстановки. Пусто — берётся позиция этого объекта.")]
        [SerializeField] private Transform _placementCenter;
        [SerializeField, Min(0f)] private float _placementRadiusMin = 5f;

        [Tooltip("Внешний край кольца расстановки. Работает запасом на случай, когда FarmBounds в " +
                 "сцене нет: при живых границах край берётся у них и растёт вместе с фермой.")]
        [SerializeField, Min(0f)] private float _placementRadiusMax = 10f;

        [Tooltip("Насколько не доводить покупки до забора.")]
        [SerializeField, Min(0f)] private float _placementEdgeMargin = 0.7f;

        [Tooltip("Минимальный зазор между объектами на ферме.")]
        [SerializeField, Min(0.1f)] private float _placementSpacing = 1.6f;

        private readonly List<Transform> _placed = new List<Transform>();
        private readonly Dictionary<ShopItemDefinition, int> _owned = new Dictionary<ShopItemDefinition, int>();

        public static Shop Instance { get; private set; }

        public ShopCatalog Catalog => _catalog;
        public Wallet Wallet => _wallet != null ? _wallet : Wallet.Instance;

        /// <summary>Откуда оплачиваются покупки и берётся продаваемое.</summary>
        public IInventory Storage => FarmingRuntime.Sink as IInventory;

        public event Action<Shop, ShopItemDefinition> Bought;

        /// <summary>Продано: ресурс, единицы, полученное золото.</summary>
        public event Action<Shop, ResourceDefinition, int, int> Sold;

        /// <summary>В покупке или продаже отказано; строка объясняет почему и готова для UI.</summary>
        public event Action<Shop, string> Refused;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Shop] В сцене больше одного Shop — лишний отключён", this);
                enabled = false;
                return;
            }

            Instance = this;
            if (_wallet == null) _wallet = FindFirstObjectByType<Wallet>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public int OwnedCount(ShopItemDefinition item)
        {
            if (item == null) return 0;
            _owned.TryGetValue(item, out int count);
            return count;
        }

        // ---- сохранение ----

        /// <summary>Что и сколько куплено — лимиты «одна на ферму» обязаны пережить загрузку.</summary>
        public IReadOnlyDictionary<ShopItemDefinition, int> CaptureOwned() => _owned;

        public void RestoreOwned(ShopItemDefinition item, int count)
        {
            if (item == null || count <= 0) return;
            _owned[item] = count;
        }

        /// <summary>
        /// Записать объект в занятые места. Загрузка ставит купленное сама, минуя
        /// <see cref="TryBuy"/>, — без этого магазин считал бы ферму пустой и ронял
        /// новые покупки прямо в стоящие постройки.
        /// </summary>
        public void RegisterPlaced(Transform placed)
        {
            if (placed != null) _placed.Add(placed);
        }

        // ---- покупка ----

        /// <summary>
        /// Заперт ли товар прогрессией (максимум владения, уровень игрока) — в отличие от
        /// «просто дорого». Витрина показывает замок видимой строкой: запертая воротами
        /// грядка, выкрашенная как дорогая, читалась бы «накоплю и куплю» — а копить тут
        /// бесполезно, нужен уровень. Тултип не годится: рантайм UI Toolkit его не рисует,
        /// а выключенная кнопка и клика не принимает — отказ был бы нем.
        /// </summary>
        public bool ProgressLock(ShopItemDefinition item, out string reason)
        {
            reason = null;
            if (item == null) return false;

            if (item.MaxOwned > 0 && OwnedCount(item) >= item.MaxOwned)
            {
                reason = "уже куплено максимум (" + item.MaxOwned + ")";
                return true;
            }

            // Ворота уровня — только на грядки: ступень меряет глубину прогрессии, и перескочить
            // её кошельком нельзя. Постройки и декор уровня не спрашивают — они про обустройство,
            // а не про лестницу. «Уровень игрока» — словами: шкал с именем «уровень» в игре
            // пять, и безымянный отказ не говорит, какую качать.
            if (item.Growable != null && item.Growable.YieldResource != null)
            {
                int need = FarmExperience.LevelForTier(item.Growable.YieldResource.Tier);
                if (FarmExperience.Level < need)
                {
                    reason = "нужен уровень игрока " + need + " (сейчас " + FarmExperience.Level + ")";
                    return true;
                }
            }

            return false;
        }

        public bool CanBuy(ShopItemDefinition item, out string reason)
        {
            reason = null;

            if (item == null) { reason = "нет товара"; return false; }
            if (ProgressLock(item, out reason)) return false;

            int gold = Wallet != null ? Wallet.Gold : 0;
            return item.Price.CanPay(gold, Storage, out reason);
        }

        public bool TryBuy(ShopItemDefinition item)
        {
            // Правило в системе, а не в интерфейсе (как в FarmLevels.TryBuyNext): спрятанная
            // кнопка держится ровно до первого нового способа нажать, а тратить чужое
            // золото в гостях нельзя ни одним из них.
            if (GuestMode.IsGuest)
            {
                RaiseRefused("в гостях не покупают — это чужая ферма");
                return false;
            }

            if (!CanBuy(item, out string reason))
            {
                RaiseRefused(reason);
                return false;
            }

            // Золото первым: если кошелёк откажет, склад ещё не тронут.
            if (item.Price.Gold > 0 && (Wallet == null || !Wallet.TrySpend(item.Price.Gold)))
            {
                RaiseRefused("не хватает золота");
                return false;
            }

            item.Price.ChargeResources(Storage);

            if (!Deliver(item))
            {
                RaiseRefused("некуда поставить — на ферме нет места");
                // Возврат: игрок никогда не должен платить за то, что не приехало.
                if (item.Price.Gold > 0 && Wallet != null) Wallet.Add(item.Price.Gold);
                RefundResources(item.Price);
                return false;
            }

            _owned.TryGetValue(item, out int owned);
            _owned[item] = owned + 1;

            var handler = Bought;
            if (handler != null)
            {
                try { handler(this, item); }
                catch (Exception e) { Debug.LogException(e, this); }
            }

            return true;
        }

        private void RefundResources(Price price)
        {
            var storage = Storage;
            if (storage == null || price.Resources == null) return;

            foreach (var cost in price.Resources)
                if (cost.IsValid) storage.TryAdd(cost.Resource, cost.Amount);
        }

        private bool Deliver(ShopItemDefinition item)
        {
            if (!TryFindSpot(out Vector3 position)) return false;

            switch (item.Kind)
            {
                case ShopItemKind.Plot:
                {
                    if (item.Growable == null) return false;

                    var plot = new GameObject("Plot_" + item.Id);
                    plot.transform.position = position;

                    var growable = plot.AddComponent<Growable>();
                    plot.AddComponent<GrowableVisuals>();

                    // Скотина должна пастись, а не стоять столбом. Компонент вешается здесь,
                    // а не в определении: определение — это данные, а «оно живое» — поведение.
                    if (item.Growable.Category == ResourceCategory.Livestock)
                        plot.AddComponent<GrazingAnimal>();

                    growable.Plant(item.Growable, 1);

                    _placed.Add(plot.transform);
                    return true;
                }

                case ShopItemKind.Prop:
                {
                    if (item.Prefab == null) return false;

                    // Театр труда (этап 3): вещь не падает с неба, а строится минуты.
                    // Имя Improvement_<id> получит уже готовая — площадку сейв ведёт
                    // отдельным массивом, и как «готовое» её ловить нельзя.
                    var site = new GameObject().AddComponent<ConstructionSite>();
                    site.transform.SetPositionAndRotation(
                        position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                    site.Begin(ConstructionSite.TargetKind.Prop, item.Id,
                               ConstructionSite.PropSeconds, ConstructionSite.PropSeconds);

                    // Стройку, как и вещь, игрок вправе переставить.
                    site.gameObject.AddComponent<Movable>();

                    _placed.Add(site.transform);
                    return true;
                }

                case ShopItemKind.Building:
                {
                    var definition = item.Building;
                    if (definition == null || definition.Prefab == null) return false;

                    // Постройки — те же минуты стройки, но без случайного поворота: у них
                    // есть перёд, и криво развёрнутая кухня читается как ошибка.
                    var site = new GameObject().AddComponent<ConstructionSite>();
                    site.transform.SetPositionAndRotation(position, Quaternion.identity);
                    site.Begin(ConstructionSite.TargetKind.Building, definition.Id,
                               ConstructionSite.BuildingSeconds, ConstructionSite.BuildingSeconds);

                    site.gameObject.AddComponent<Movable>();

                    _placed.Add(site.transform);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Куда поставить купленное. Кольцо расстановки растёт вместе с фермой: уровень покупают
        /// ровно за место, и магазин, продолжающий раскладывать покупки по старому кольцу, отменял
        /// бы всю затею — земли больше, а поставить негде.
        /// <para>
        /// Наружу — в пару к <see cref="RegisterPlaced"/>: снаружи спрашивают ровно то же свободное
        /// место (загрузка, проверка расширения), и второй такой расстановки заводить не нужно.
        /// </para>
        /// </summary>
        public bool TryFindSpot(out Vector3 position)
        {
            Vector3 center = _placementCenter != null ? _placementCenter.position : transform.position;
            var bounds = FarmBounds.Instance;

            float min = Mathf.Min(_placementRadiusMin, _placementRadiusMax);

            // Край — у границ, с отступом от забора. Сериализованное поле остаётся запасом:
            // без FarmBounds в сцене магазин работает ровно как раньше.
            float max = Mathf.Max(_placementRadiusMin, _placementRadiusMax);
            if (bounds != null) max = Mathf.Max(max, bounds.UsableRadius - _placementEdgeMargin);
            if (max < min) max = min;

            // 48 попыток хватало на кольцо 4.5–11 м (около 320 м²). Кольцо растёт с фермой, и то
            // же число тыкалось бы в давно занятую середину: покупка отказывала бы при пустых
            // окраинах — а это ровно тот отказ, за отсутствие которого игрок и заплатил.
            // Держим постоянной плотность попыток на квадратный метр.
            const float AreaPerAttempt = 6.6f;
            const int MinAttempts = 48;
            const int MaxAttempts = 400;

            float ringArea = Mathf.PI * Mathf.Max(0f, max * max - min * min);
            int attempts = Mathf.Clamp(Mathf.RoundToInt(ringArea / AreaPerAttempt), MinAttempts, MaxAttempts);

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

                // Корень — иначе точки сгущаются к центру: площадь кольца растёт как квадрат
                // радиуса, а равномерный радиус этого не знает. На большой ферме это разница
                // между «ищем по всей земле» и «ищем в самой застроенной её части».
                float radius = Mathf.Sqrt(Mathf.Lerp(min * min, max * max, UnityEngine.Random.value));

                var candidate = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                // Центр расстановки и центр фермы — разные объекты сцены; без этой проверки
                // сдвинутый центр однажды вынесет покупку за забор.
                if (bounds != null && !bounds.Contains(candidate)) continue;

                if (IsClear(candidate))
                {
                    // Кладём по рельефу — иначе покупка появится над землёй или в ней.
                    candidate.y = FarmingRuntime.Ground.SampleHeight(candidate);
                    position = candidate;
                    return true;
                }
            }

            position = center;
            return false;
        }

        private bool IsClear(Vector3 candidate)
        {
            float sqrSpacing = _placementSpacing * _placementSpacing;

            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
                if ((plots[i].transform.position - candidate).sqrMagnitude < sqrSpacing) return false;

            // Всё переставляемое: постройки из сцены, замыслы фермера, купленный декор.
            // Свой список _placed знает только о покупках этой сессии, а кухня, стоявшая
            // в сцене с начала партии, для него невидима — покупка садилась ей на крышу.
            var movables = MovableRegistry.All;
            for (int i = 0; i < movables.Count; i++)
            {
                var movable = movables[i];
                if (movable == null) continue;
                if ((movable.transform.position - candidate).sqrMagnitude < sqrSpacing) return false;
            }

            for (int i = _placed.Count - 1; i >= 0; i--)
            {
                if (_placed[i] == null) { _placed.RemoveAt(i); continue; }
                if ((_placed[i].position - candidate).sqrMagnitude < sqrSpacing) return false;
            }

            return true;
        }

        // ---- продажа ----

        /// <summary>
        /// Золото за стопку, с учётом рынков. Каждая продажа проходит здесь, поэтому рынок
        /// с надбавкой — одна строка, а не правило, которое UI и продавец должны помнить каждый сам.
        /// </summary>
        public int SellValue(ResourceDefinition resource, int amount) =>
            SellValue(resource, amount, ResourceGrade.Common);

        /// <summary>
        /// Золото за стопку названного сорта. Надбавка сорта считается до рыночной, а не
        /// после: рынок платит процент с цены товара, а сорт эту цену и определяет.
        /// </summary>
        public int SellValue(ResourceDefinition resource, int amount, ResourceGrade grade)
        {
            if (resource == null || amount <= 0) return 0;

            int raw = Mathf.RoundToInt(Mathf.Max(0, resource.SellPrice) * amount *
                                       ResourceGrades.PriceFactor(grade));
            if (raw <= 0) return 0;

            return Mathf.Max(raw, Mathf.RoundToInt(raw * (1f + BuildingRegistry.MarketBonus)));
        }

        /// <summary>
        /// Сколько даст продажа <paramref name="amount"/> единиц прямо сейчас — с тем же
        /// порядком сортов, каким её проведёт <see cref="TrySell(ResourceDefinition,int)"/>.
        /// Нужен UI: цена в меню, посчитанная по обычному сорту, соврала бы ровно на надбавку
        /// за уход, а обещание кнопки обязано совпадать с её делом.
        /// </summary>
        public int PreviewSell(ResourceDefinition resource, int amount)
        {
            var storage = Storage;
            if (resource == null || amount <= 0 || storage == null) return 0;

            int gold = 0;
            int left = amount;

            var order = ResourceGrades.All;
            for (int i = 0; i < order.Length && left > 0; i++)
            {
                int take = Mathf.Min(left, storage.GetAmount(resource, order[i]));
                if (take <= 0) continue;

                gold += SellValue(resource, take, order[i]);
                left -= take;
            }

            return gold;
        }

        /// <summary>Продать до <paramref name="amount"/> единиц названного сорта.</summary>
        public int TrySell(ResourceDefinition resource, int amount, ResourceGrade grade)
        {
            if (GuestMode.IsGuest)
            {
                RaiseRefused("в гостях не продают — это чужой склад");
                return 0;
            }

            var storage = Storage;
            if (resource == null || amount <= 0 || storage == null) return 0;

            if (resource.SellPrice <= 0)
            {
                RaiseRefused(resource.DisplayName + " не продаётся");
                return 0;
            }

            int removed = storage.TryRemove(resource, amount, grade);
            if (removed <= 0)
            {
                RaiseRefused("нечего продавать");
                return 0;
            }

            int gold = SellValue(resource, removed, grade);
            if (Wallet != null) Wallet.Add(gold);

            var handler = Sold;
            if (handler != null)
            {
                try { handler(this, resource, removed, gold); }
                catch (Exception e) { Debug.LogException(e, this); }
            }

            return gold;
        }

        /// <summary>Продать до <paramref name="amount"/> единиц. Возвращает полученное золото.</summary>
        public int TrySell(ResourceDefinition resource, int amount)
        {
            // Симметрично TryBuy: чужой склад в гостях не распродают, каким бы путём
            // ни пришёл вызов.
            if (GuestMode.IsGuest)
            {
                RaiseRefused("в гостях не продают — это чужой склад");
                return 0;
            }

            var storage = Storage;
            if (resource == null || amount <= 0 || storage == null) return 0;

            if (resource.SellPrice <= 0)
            {
                RaiseRefused(resource.DisplayName + " не продаётся");
                return 0;
            }

            // Продаём сортами от обычного к лучшему и платим за каждый по его цене. Одной
            // строкой это не сделать: TryRemove вернул бы общее число, а стопки стоят разного,
            // и «в среднем» тут означало бы обмануть игрока ровно на надбавку за уход.
            int removed = 0;
            int gold = 0;
            int left = amount;

            var order = ResourceGrades.All;
            for (int i = 0; i < order.Length && left > 0; i++)
            {
                int taken = storage.TryRemove(resource, left, order[i]);
                if (taken <= 0) continue;

                removed += taken;
                left -= taken;
                gold += SellValue(resource, taken, order[i]);
            }

            if (removed <= 0)
            {
                RaiseRefused("нечего продавать");
                return 0;
            }

            if (Wallet != null) Wallet.Add(gold);

            var handler = Sold;
            if (handler != null)
            {
                try { handler(this, resource, removed, gold); }
                catch (Exception e) { Debug.LogException(e, this); }
            }

            return gold;
        }

        private void RaiseRefused(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return;

            var handler = Refused;
            if (handler == null) return;
            try { handler(this, reason); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}

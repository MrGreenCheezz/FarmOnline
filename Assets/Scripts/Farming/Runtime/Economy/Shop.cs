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
        [SerializeField, Min(0f)] private float _placementRadiusMax = 10f;
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

        // ---- покупка ----

        public bool CanBuy(ShopItemDefinition item, out string reason)
        {
            reason = null;

            if (item == null) { reason = "нет товара"; return false; }

            if (item.MaxOwned > 0 && OwnedCount(item) >= item.MaxOwned)
            {
                reason = "уже куплено максимум (" + item.MaxOwned + ")";
                return false;
            }

            int gold = Wallet != null ? Wallet.Gold : 0;
            return item.Price.CanPay(gold, Storage, out reason);
        }

        public bool TryBuy(ShopItemDefinition item)
        {
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

                    var instance = Instantiate(item.Prefab, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                    instance.name = item.Id;

                    // Всё купленное игрок должен иметь возможность переставить.
                    if (instance.GetComponent<Movable>() == null) instance.AddComponent<Movable>();

                    _placed.Add(instance.transform);
                    return true;
                }

                case ShopItemKind.Building:
                {
                    var definition = item.Building;
                    if (definition == null || definition.Prefab == null) return false;

                    // Постройки ставятся без случайного поворота: у них есть перёд, и фермер
                    // подходит к ним — криво развёрнутая кухня читается как ошибка.
                    var instance = Instantiate(definition.Prefab, position, Quaternion.identity);
                    instance.name = "Building_" + definition.Id;

                    var building = instance.GetComponent<Building>();
                    if (building == null) building = instance.AddComponent<Building>();
                    building.Configure(definition, 1);

                    // Мастерская — отдельный компонент: уровень и цикл производства живут врозь.
                    if (definition.Service == BuildingService.Workshop && instance.GetComponent<Workshop>() == null)
                        instance.AddComponent<Workshop>();

                    if (instance.GetComponent<Movable>() == null) instance.AddComponent<Movable>();

                    _placed.Add(instance.transform);
                    return true;
                }
            }

            return false;
        }

        private bool TryFindSpot(out Vector3 position)
        {
            Vector3 center = _placementCenter != null ? _placementCenter.position : transform.position;
            float min = Mathf.Min(_placementRadiusMin, _placementRadiusMax);
            float max = Mathf.Max(_placementRadiusMin, _placementRadiusMax);

            for (int attempt = 0; attempt < 48; attempt++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = UnityEngine.Random.Range(min, max);
                var candidate = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

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
        public int SellValue(ResourceDefinition resource, int amount)
        {
            if (resource == null || amount <= 0) return 0;

            int raw = Mathf.Max(0, resource.SellPrice) * amount;
            if (raw <= 0) return 0;

            return Mathf.Max(raw, Mathf.RoundToInt(raw * (1f + BuildingRegistry.MarketBonus)));
        }

        /// <summary>Продать до <paramref name="amount"/> единиц. Возвращает полученное золото.</summary>
        public int TrySell(ResourceDefinition resource, int amount)
        {
            var storage = Storage;
            if (resource == null || amount <= 0 || storage == null) return 0;

            if (resource.SellPrice <= 0)
            {
                RaiseRefused(resource.DisplayName + " не продаётся");
                return 0;
            }

            int removed = storage.TryRemove(resource, amount);
            if (removed <= 0)
            {
                RaiseRefused("нечего продавать");
                return 0;
            }

            int gold = SellValue(resource, removed);
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

using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Shop tab an item belongs to. Values are serialized — append, never renumber.</summary>
    public enum ShopCategory
    {
        Plants = 0,
        Animals = 1,
        Buildings = 2,
        Ore = 3
    }

    /// <summary>What actually appears on the farm when the item is bought.</summary>
    public enum ShopItemKind
    {
        /// <summary>Creates a working plot running the assigned <see cref="GrowableDefinition"/>.</summary>
        Plot = 0,
        /// <summary>Instantiates the prefab as-is. Decoration and anything without behaviour yet.</summary>
        Prop = 1,
        /// <summary>Places a working <see cref="Building"/> at level 1 from a <see cref="BuildingDefinition"/>.</summary>
        Building = 2
    }

    /// <summary>
    /// One line in the shop. Data only — the <see cref="Shop"/> decides what buying means, so
    /// adding a new kind of purchase never touches the catalogue assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Farm/Shop Item", fileName = "Shop_")]
    public sealed class ShopItemDefinition : ScriptableObject
    {
        [Header("Что это")]
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField, TextArea(2, 4)] private string _description;
        [SerializeField] private Sprite _icon;
        [SerializeField] private ShopCategory _category = ShopCategory.Plants;

        [Header("Цена")]
        [SerializeField] private Price _price;

        [Header("Что появляется")]
        [SerializeField] private ShopItemKind _kind = ShopItemKind.Plot;

        [Tooltip("Для Plot: что будет расти на новой грядке.")]
        [SerializeField] private GrowableDefinition _growable;

        [Tooltip("Для Prop: что поставить на ферму.")]
        [SerializeField] private GameObject _prefab;

        [Tooltip("Для Building: какую постройку возвести. Цена берётся с её первого уровня.")]
        [SerializeField] private BuildingDefinition _building;

        [Tooltip("0 — без ограничений. Иначе сколько всего таких можно купить.")]
        [SerializeField, Min(0)] private int _maxOwned;

        public string Id => string.IsNullOrEmpty(_id) ? name : _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? Id : _displayName;
        public string Description => _description;
        public ShopCategory Category => _category;
        public ShopItemKind Kind => _kind;
        public GrowableDefinition Growable => _growable;
        public GameObject Prefab => _prefab;
        public BuildingDefinition Building => _building;
        public int MaxOwned => _maxOwned;

        /// <summary>Falls back to the building's own icon so a building is drawn once, not twice.</summary>
        public Sprite Icon => _icon != null ? _icon : (_building != null ? _building.Icon : null);

        /// <summary>
        /// What the item costs. A building quotes its own level-1 cost rather than a copy typed here:
        /// the shop price and the first rung of the upgrade ladder are the same number by definition,
        /// and two places to edit it is one place to get it wrong.
        /// </summary>
        public Price Price
        {
            get
            {
                if (_kind != ShopItemKind.Building || _building == null) return _price;
                return _building.CostOf(1) ?? _price;
            }
        }

        private void OnValidate()
        {
            if (_kind == ShopItemKind.Plot && _growable == null)
                Debug.LogWarning("[Shop] У '" + Id + "' тип Plot, но не задан GrowableDefinition", this);
            if (_kind == ShopItemKind.Prop && _prefab == null)
                Debug.LogWarning("[Shop] У '" + Id + "' тип Prop, но не задан префаб", this);
            if (_kind == ShopItemKind.Building && _building == null)
                Debug.LogWarning("[Shop] У '" + Id + "' тип Building, но не задан BuildingDefinition", this);
        }
    }
}

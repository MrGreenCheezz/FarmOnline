using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Вкладка магазина, к которой относится товар. Значения сериализуются — добавляй в конец, не перенумеровывай.</summary>
    public enum ShopCategory
    {
        Plants = 0,
        Animals = 1,
        Buildings = 2,
        Ore = 3
    }

    /// <summary>Что реально появляется на ферме при покупке.</summary>
    public enum ShopItemKind
    {
        /// <summary>Создаёт рабочую грядку с назначенным <see cref="GrowableDefinition"/>.</summary>
        Plot = 0,
        /// <summary>Ставит префаб как есть. Декор и всё, у чего пока нет поведения.</summary>
        Prop = 1,
        /// <summary>Ставит рабочую <see cref="Building"/> 1-го уровня из <see cref="BuildingDefinition"/>.</summary>
        Building = 2
    }

    /// <summary>
    /// Одна строка магазина. Только данные — что значит «купить», решает <see cref="Shop"/>,
    /// поэтому новый вид покупки никогда не трогает ассеты каталога.
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

        /// <summary>Откатывается к иконке самой постройки — постройку рисуют один раз, а не дважды.</summary>
        public Sprite Icon => _icon != null ? _icon : (_building != null ? _building.Icon : null);

        /// <summary>
        /// Сколько товар стоит. Постройка называет собственную цену 1-го уровня, а не копию,
        /// вбитую здесь: цена в магазине и первая ступень лестницы улучшений — одно и то же
        /// число по определению, а два места для правки — это одно место для ошибки.
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

using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Тип собираемого ресурса (пшеница, молоко, железная руда...). Нарочно тонкий —
    /// существует, чтобы урожай был типизированной ссылкой на ассет, а не магической
    /// строкой, и чтобы у иконок и названий был дом.
    /// </summary>
    [CreateAssetMenu(menuName = "Farm/Resource", fileName = "Resource_")]
    public sealed class ResourceDefinition : ScriptableObject
    {
        [Tooltip("Стабильный ключ для сохранений и поиска. Пусто — берётся имя ассета.")]
        [SerializeField] private string _id;

        [SerializeField] private string _displayName;
        [SerializeField] private Sprite _icon;
        [SerializeField] private ResourceCategory _category = ResourceCategory.Crop;

        [Tooltip("Сколько золота даёт одна единица при продаже. 0 — продавать нельзя.")]
        [SerializeField, Min(0)] private int _sellPrice = 1;

        [Tooltip("Может ли фермер продавать это сам.\n" +
                 "Снимай у всего, что копится на постройки: игрок вернётся к складу за досками, " +
                 "а их уже обменяли на золото — и это худший вид «помощи».\n" +
                 "На продажу руками не влияет: игрок вправе продать что угодно.")]
        [SerializeField] private bool _farmerMaySell = true;

        [Header("Ступень")]
        [Tooltip("Ступень ресурса: 1 — стартовая (медь, обычное дерево), 2 — следующая (железо, дуб) и так далее.\n" +
                 "Цены построек считаются от ступени формулой, поэтому новая ступень — это один ассет, а не таблица чисел.")]
        [SerializeField, Min(1)] private int _tier = 1;

        [Header("Питательность")]
        [Tooltip("Сколько сытости даёт одна единица. 0 — не еда.")]
        [SerializeField, Min(0f)] private float _nutrition;

        [Tooltip("Сколько воды даёт одна единица. 0 — не питьё.")]
        [SerializeField, Min(0f)] private float _hydration;

        public string Id => string.IsNullOrEmpty(_id) ? name : _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? Id : _displayName;
        public Sprite Icon => _icon;
        public ResourceCategory Category => _category;
        public int SellPrice => _sellPrice;

        /// <summary>Может ли фермер продавать это самостоятельно? Игрок может всегда.</summary>
        public bool FarmerMaySell => _farmerMaySell;

        public int Tier => _tier;

        public float Nutrition => _nutrition;
        public float Hydration => _hydration;

        public bool IsFood => _nutrition > 0f;
        public bool IsDrink => _hydration > 0f;
    }
}

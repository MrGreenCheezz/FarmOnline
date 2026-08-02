using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Всё, что продаёт магазин. Один ассет — чтобы весь ассортимент можно было
    /// просмотреть и отбалансировать в одном месте, а не собирать по ссылкам в сцене.
    /// </summary>
    [CreateAssetMenu(menuName = "Farm/Shop Catalog", fileName = "ShopCatalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        [SerializeField] private ShopItemDefinition[] _items = new ShopItemDefinition[0];

        public IReadOnlyList<ShopItemDefinition> Items => _items;

        /// <summary>Заполняет <paramref name="results"/> товарами одной вкладки. Сначала очищает список.</summary>
        public void GetByCategory(ShopCategory category, List<ShopItemDefinition> results)
        {
            if (results == null) return;
            results.Clear();

            if (_items == null) return;
            foreach (var item in _items)
                if (item != null && item.Category == category) results.Add(item);
        }

        public bool HasCategory(ShopCategory category)
        {
            if (_items == null) return false;
            foreach (var item in _items)
                if (item != null && item.Category == category) return true;
            return false;
        }
    }
}

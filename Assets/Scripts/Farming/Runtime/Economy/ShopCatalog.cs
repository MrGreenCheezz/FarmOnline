using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Everything the shop sells. A single asset so the whole offer can be reviewed and balanced
    /// in one place instead of being scattered across scene references.
    /// </summary>
    [CreateAssetMenu(menuName = "Farm/Shop Catalog", fileName = "ShopCatalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        [SerializeField] private ShopItemDefinition[] _items = new ShopItemDefinition[0];

        public IReadOnlyList<ShopItemDefinition> Items => _items;

        /// <summary>Fills <paramref name="results"/> with the items of one tab. Clears it first.</summary>
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

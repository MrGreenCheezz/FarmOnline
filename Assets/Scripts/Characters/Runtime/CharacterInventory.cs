using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    /// <summary>
    /// Что несёт персонаж, в виде компонента: ёмкость крутится в инспекторе, а другие
    /// системы находят инвентарь обычным <c>GetComponent</c>.
    /// <para>
    /// Сам контейнер — обычный <see cref="Farming.Inventory"/>. Это лишь сценовая обёртка,
    /// поэтому та же логика без единого MonoBehaviour обслуживает амбар, сундук или тест.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Character Inventory")]
    public sealed class CharacterInventory : MonoBehaviour
    {
        [Tooltip("Чем ограничен объём: ничем, суммой единиц, или числом разных ресурсов.")]
        [SerializeField] private InventoryCapacity _capacityMode = InventoryCapacity.Units;

        [SerializeField, Min(1)] private int _capacity = 8;

        [Tooltip("Разрешить одному сбору превысить лимит.\n" +
                 "Нужно персонажу: грядка 3-го уровня отдаёт 4 за раз, и без этого крупный урожай " +
                 "стал бы несобираемым, когда в рюкзаке осталось меньше места.")]
        [SerializeField] private bool _allowOverflow = true;

        private Inventory _inventory;

        /// <summary>
        /// Создаётся лениво, чтобы порядок компонентов не имел значения — кто первым спросил,
        /// тот и получил готовый.
        /// </summary>
        public Inventory Inventory
        {
            get
            {
                if (_inventory == null)
                    _inventory = new Inventory(_capacityMode, _capacity, _allowOverflow);
                return _inventory;
            }
        }

        public int TotalUnits => Inventory.TotalUnits;
        public bool IsEmpty => Inventory.IsEmpty;
        public bool IsFull => Inventory.IsFull;
        public int FreeUnits => Inventory.FreeUnits;

        /// <summary>Проброшено, чтобы UI подписывался на компонент, не залезая внутрь.</summary>
        public event Action<IInventory> Changed
        {
            add => Inventory.Changed += value;
            remove => Inventory.Changed -= value;
        }

        public event Action<IInventory, ResourceDefinition, int> Added
        {
            add => Inventory.Added += value;
            remove => Inventory.Added -= value;
        }

        public event Action<IInventory, ResourceDefinition, int> Removed
        {
            add => Inventory.Removed += value;
            remove => Inventory.Removed -= value;
        }

        public int Capacity
        {
            get => _capacity;
            set
            {
                _capacity = Mathf.Max(1, value);
                Inventory.Capacity = _capacity;
            }
        }

        private void OnValidate()
        {
            if (_inventory == null) return;   // ещё не работает — геттер подхватит эти значения сам

            _inventory.CapacityMode = _capacityMode;
            _inventory.Capacity = Mathf.Max(1, _capacity);
            _inventory.AllowOverflow = _allowOverflow;
        }
    }
}

using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    /// <summary>
    /// What the character is carrying, as a component: capacity is tweakable in the inspector and
    /// other systems can find the inventory with a plain <c>GetComponent</c>.
    /// <para>
    /// The container itself is a plain <see cref="Farming.Inventory"/>. This is only the scene-facing
    /// shell, so the same logic backs a barn, a chest or a test with no MonoBehaviour in sight.
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
        /// Built lazily so component order never matters — whoever asks first gets it ready.
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

        /// <summary>Forwarded so UI can subscribe to the component without reaching inside.</summary>
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
            if (_inventory == null) return;   // not running yet — the getter will pick these up

            _inventory.CapacityMode = _capacityMode;
            _inventory.Capacity = Mathf.Max(1, _capacity);
            _inventory.AllowOverflow = _allowOverflow;
        }
    }
}

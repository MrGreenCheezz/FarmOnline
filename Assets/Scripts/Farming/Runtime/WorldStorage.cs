using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// The farm's own store — where everything the character hauls home ends up.
    /// <para>
    /// Drop it in a scene and it becomes <see cref="FarmingRuntime.Sink"/>, replacing the debug
    /// placeholder. It is a real <see cref="IInventory"/>, so it raises change events the UI can
    /// bind to and can be capped later when barns start limiting how much the farm holds.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]   // becomes the sink before any Growable can harvest into it
    [AddComponentMenu("Farm/World Storage")]
    public sealed class WorldStorage : MonoBehaviour
    {
        [Tooltip("Пока склад безлимитный. Появятся амбары — переключим на Units и свяжем с их вместимостью.")]
        [SerializeField] private InventoryCapacity _capacityMode = InventoryCapacity.Unlimited;

        [SerializeField, Min(1)] private int _capacity = 999;

        private Inventory _inventory;

        public static WorldStorage Instance { get; private set; }

        public Inventory Inventory
        {
            get
            {
                if (_inventory == null)
                    _inventory = new Inventory(_capacityMode, _capacity);
                return _inventory;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Farming] В сцене больше одного WorldStorage — лишний отключён", this);
                enabled = false;
                return;
            }

            Instance = this;
            FarmingRuntime.Sink = Inventory;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnValidate()
        {
            if (_inventory == null) return;
            _inventory.CapacityMode = _capacityMode;
            _inventory.Capacity = Mathf.Max(1, _capacity);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}

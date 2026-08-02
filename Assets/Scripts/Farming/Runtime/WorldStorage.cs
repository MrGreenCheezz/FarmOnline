using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Собственный склад фермы — сюда попадает всё, что персонаж приносит домой.
    /// <para>
    /// Брось в сцену — и он станет <see cref="FarmingRuntime.Sink"/> вместо отладочной
    /// заглушки. Это настоящий <see cref="IInventory"/>: поднимает события изменений,
    /// к которым привязывается UI, и позже может получить лимит, когда амбары начнут
    /// ограничивать вместимость фермы.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]   // становится стоком раньше, чем первая грядка сможет собраться в него
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

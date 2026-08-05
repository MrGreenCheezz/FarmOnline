using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Собственный склад фермы — сюда попадает всё, что персонаж приносит домой.
    /// <para>
    /// Брось в сцену — и он станет <see cref="FarmingRuntime.Sink"/> вместо отладочной
    /// заглушки. Это настоящий <see cref="IInventory"/>: поднимает события изменений,
    /// к которым привязывается UI.
    /// </para>
    /// <para>
    /// Про контент он не знает ничего: вместимость меряется ячейками объёма, а не видами
    /// ресурсов, поэтому новый ресурс в игре никогда не требует правки склада. Это условие,
    /// а не совпадение — склад, который надо расширять под каждый новый ассет, рано или
    /// поздно молча съест урожай, о котором забыли.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]   // становится стоком раньше, чем первая грядка сможет собраться в него
    [AddComponentMenu("Farm/World Storage")]
    public sealed class WorldStorage : MonoBehaviour
    {
        [Tooltip("Чем ограничен склад: ничем, суммой единиц или ячейками.")]
        [SerializeField] private InventoryCapacity _capacityMode = InventoryCapacity.Slots;

        [Tooltip("Сколько ячеек на складе своих, без построек. Ячейка — это объём, а не вид " +
                 "ресурса: поднимать число нужно, когда амбар должен вмещать больше, а не когда " +
                 "в игре появился новый ресурс.")]
        [SerializeField, Min(1)] private int _capacity = 48;

        [Tooltip("Сколько единиц одного ресурса влезает в ячейку. Что не влезло — займёт следующую.")]
        [SerializeField, Min(1)] private int _stackSize = Inventory.DefaultStackSize;

        /// <summary>Как редко жаловаться в консоль на переполнение, секунд.</summary>
        private const float WarningInterval = 5f;

        private Inventory _inventory;
        private float _lastWarning = -WarningInterval;

        public static WorldStorage Instance { get; private set; }

        /// <summary>
        /// Склад не принял груз: ресурс и сколько единиц пропало. Потеря обязана быть
        /// заметной — «собрал, а на складе не прибавилось» игрок читает как поломку,
        /// и без этого события так оно и выглядело бы.
        /// </summary>
        public event Action<ResourceDefinition, int> Overflowed;

        public Inventory Inventory
        {
            get
            {
                if (_inventory == null)
                {
                    _inventory = new Inventory(_capacityMode, TotalCapacity, false, _stackSize);
                    _inventory.Rejected += OnRejected;
                }
                return _inventory;
            }
        }

        /// <summary>Свои ячейки плюс те, что дают силосы и амбары.</summary>
        public int TotalCapacity => Mathf.Max(1, _capacity) + FarmBuffs.StorageSlots;

        /// <summary>Свободных ячеек прямо сейчас. Отрицательным не бывает.</summary>
        public int FreeSlots => _capacityMode == InventoryCapacity.Slots
            ? Mathf.Max(0, Inventory.Capacity - Inventory.UsedSlots)
            : int.MaxValue;

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

            FarmBuffs.Changed += OnBuffsChanged;
        }

        private void OnDestroy()
        {
            FarmBuffs.Changed -= OnBuffsChanged;

            if (_inventory != null) _inventory.Rejected -= OnRejected;
            if (Instance == this) Instance = null;
        }

        private void OnValidate()
        {
            if (_inventory == null) return;
            _inventory.CapacityMode = _capacityMode;
            _inventory.Capacity = TotalCapacity;
            _inventory.StackSize = Mathf.Max(1, _stackSize);
        }

        /// <summary>Построили или улучшили силос — вместимость обязана вырасти сразу, а не к следующей доставке.</summary>
        private void OnBuffsChanged()
        {
            if (_inventory == null) return;
            _inventory.Capacity = TotalCapacity;
        }

        private void OnRejected(IInventory inventory, ResourceDefinition resource, int amount)
        {
            // В консоль — редко: полный склад отказывает каждой доставке, и лог без паузы
            // залил бы всё остальное. Игроку об этом говорит HUD, а не консоль.
            if (Time.unscaledTime - _lastWarning >= WarningInterval)
            {
                _lastWarning = Time.unscaledTime;
                Debug.LogWarning("[Farming] Склад полон: " + amount + "x " +
                                 (resource != null ? resource.DisplayName : "?") + " пропало", this);
            }

            var handler = Overflowed;
            if (handler == null) return;
            try { handler(resource, amount); }
            catch (Exception e) { Debug.LogException(e); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}

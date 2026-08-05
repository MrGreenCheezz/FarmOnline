using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Сериализуемое содержимое инвентаря, с ключом-идентификатором вместо ссылки на ассет.</summary>
    [Serializable]
    public struct InventorySnapshotEntry
    {
        public string ResourceId;
        public int Amount;
    }

    /// <summary>
    /// Готовая к сохранению копия инвентаря. Идентификаторы вместо ссылок на объекты —
    /// переживает пересборку базы ассетов и идёт прямиком в JSON.
    /// </summary>
    [Serializable]
    public sealed class InventorySnapshot
    {
        public InventorySnapshotEntry[] Entries = Array.Empty<InventorySnapshotEntry>();
    }

    /// <summary>
    /// Реализация <see cref="IInventory"/> по умолчанию. Чистый C# — ни MonoBehaviour, ни
    /// зависимости от сцены — поэтому одинаково служит рюкзаком персонажа, хранилищем
    /// постройки или одноразовым контейнером в тесте.
    /// <para>
    /// Содержимое лежит в списке, а не в словаре: записей максимум десятки, линейный проход
    /// на таком размере быстрее хеширования, а стабильный порядок означает, что список UI
    /// не перетасовывается при каждом подборе.
    /// </para>
    /// </summary>
    public class Inventory : IInventory
    {
        /// <summary>Сколько единиц кладётся в ячейку, если не сказано иное.</summary>
        public const int DefaultStackSize = 99;

        private readonly List<InventoryEntry> _entries = new List<InventoryEntry>(8);

        private int _stackSize = DefaultStackSize;

        public Inventory() : this(InventoryCapacity.Unlimited, 0) { }

        public Inventory(InventoryCapacity mode, int capacity, bool allowOverflow = false,
                         int stackSize = DefaultStackSize)
        {
            CapacityMode = mode;
            Capacity = capacity;
            AllowOverflow = allowOverflow;
            StackSize = stackSize;
        }

        public InventoryCapacity CapacityMode { get; set; }

        /// <summary>Смысл зависит от <see cref="CapacityMode"/>: единицы или ячейки.</summary>
        public int Capacity { get; set; }

        /// <summary>
        /// Вместимость одной ячейки в единицах. Это то, что делает лимит независимым от
        /// контента: ячейка меряет объём, поэтому новый вид ресурса ничего не занимает,
        /// пока его реально не принесли.
        /// </summary>
        public int StackSize
        {
            get => _stackSize;
            set => _stackSize = Mathf.Max(1, value);
        }

        /// <summary>
        /// Когда включено, одно добавление никогда не урезается, даже сверх лимита —
        /// контейнер просто сообщает <see cref="IsFull"/> после.
        /// <para>
        /// Рюкзаку фермера это необходимо: грядка 3-го уровня отдаёт 4 за раз, и отказ от
        /// перелива сделал бы любой урожай крупнее свободного места несобираемым навсегда.
        /// У фиксированных контейнеров вроде сундуков должно быть выключено.
        /// </para>
        /// </summary>
        public bool AllowOverflow { get; set; }

        public int TotalUnits { get; private set; }
        public int DistinctCount => _entries.Count;
        public bool IsEmpty => TotalUnits == 0;

        /// <summary>
        /// Считается проходом, а не счётчиком: записей десятки, а кэш пришлось бы чинить
        /// в четырёх местах и заново при смене <see cref="StackSize"/> — цена расхождения
        /// здесь выше цены цикла.
        /// </summary>
        public int UsedSlots
        {
            get
            {
                int stack = _stackSize;
                int slots = 0;
                for (int i = 0; i < _entries.Count; i++)
                    slots += (_entries[i].Amount + stack - 1) / stack;
                return slots;
            }
        }

        public IReadOnlyList<InventoryEntry> Entries => _entries;

        public event Action<IInventory> Changed;
        public event Action<IInventory, ResourceDefinition, int> Added;
        public event Action<IInventory, ResourceDefinition, int> Removed;
        public event Action<IInventory, ResourceDefinition, int> Rejected;

        public bool IsFull
        {
            get
            {
                switch (CapacityMode)
                {
                    case InventoryCapacity.Units: return TotalUnits >= Capacity;
                    case InventoryCapacity.Slots: return UsedSlots >= Capacity;
                    default: return false;
                }
            }
        }

        public int FreeUnits
        {
            get
            {
                switch (CapacityMode)
                {
                    case InventoryCapacity.Units: return Mathf.Max(0, Capacity - TotalUnits);
                    // Только целые ячейки: недобитый стек примет свой ресурс и откажет
                    // любому другому, поэтому обещать его «вообще всем» нельзя.
                    case InventoryCapacity.Slots: return ClampToInt(FreeSlotUnits());
                    default: return int.MaxValue;
                }
            }
        }

        // ---- чтение ----

        public int GetAmount(ResourceDefinition resource)
        {
            int i = IndexOf(resource);
            return i >= 0 ? _entries[i].Amount : 0;
        }

        public bool Contains(ResourceDefinition resource, int amount = 1) =>
            amount <= 0 || GetAmount(resource) >= amount;

        // ---- запись ----

        /// <summary>Вход <see cref="IResourceSink"/>. О переполнении сообщает через <see cref="Rejected"/>.</summary>
        public void Add(ResourceDefinition resource, int amount) => TryAdd(resource, amount);

        public int TryAdd(ResourceDefinition resource, int amount)
        {
            if (resource == null || amount <= 0) return 0;

            int accepted = AllowOverflow ? amount : Mathf.Min(amount, FreeUnitsFor(resource));
            if (accepted <= 0)
            {
                Raise(Rejected, resource, amount);
                return 0;
            }

            int i = IndexOf(resource);
            if (i >= 0) _entries[i] = _entries[i].WithAmount(_entries[i].Amount + accepted);
            else _entries.Add(new InventoryEntry(resource, accepted));

            TotalUnits += accepted;

            Raise(Added, resource, accepted);
            if (accepted < amount) Raise(Rejected, resource, amount - accepted);
            RaiseChanged();
            return accepted;
        }

        public int TryRemove(ResourceDefinition resource, int amount)
        {
            if (amount <= 0) return 0;

            int i = IndexOf(resource);
            if (i < 0) return 0;

            int removed = Mathf.Min(amount, _entries[i].Amount);
            int left = _entries[i].Amount - removed;

            if (left > 0) _entries[i] = _entries[i].WithAmount(left);
            else _entries.RemoveAt(i);

            TotalUnits -= removed;

            Raise(Removed, resource, removed);
            RaiseChanged();
            return removed;
        }

        public int TransferTo(IResourceSink target)
        {
            if (target == null || TotalUnits == 0) return 0;

            int moved = TotalUnits;
            for (int i = 0; i < _entries.Count; i++)
                target.Add(_entries[i].Resource, _entries[i].Amount);

            _entries.Clear();
            TotalUnits = 0;
            RaiseChanged();
            return moved;
        }

        public int TransferTo(IInventory target)
        {
            if (target == null || TotalUnits == 0) return 0;

            int moved = 0;
            // С конца: опустевшие записи удаляются, и это сдвигает всё после них.
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                int accepted = target.TryAdd(entry.Resource, entry.Amount);
                if (accepted <= 0) continue;

                moved += accepted;
                TotalUnits -= accepted;

                if (accepted >= entry.Amount) _entries.RemoveAt(i);
                else _entries[i] = entry.WithAmount(entry.Amount - accepted);
            }

            if (moved > 0) RaiseChanged();
            return moved;
        }

        public void Clear()
        {
            if (_entries.Count == 0) return;
            _entries.Clear();
            TotalUnits = 0;
            RaiseChanged();
        }

        // ---- сохранение ----

        public InventorySnapshot CaptureState()
        {
            var snapshot = new InventorySnapshot
            {
                Entries = new InventorySnapshotEntry[_entries.Count]
            };

            for (int i = 0; i < _entries.Count; i++)
            {
                snapshot.Entries[i] = new InventorySnapshotEntry
                {
                    ResourceId = _entries[i].Resource != null ? _entries[i].Resource.Id : null,
                    Amount = _entries[i].Amount
                };
            }

            return snapshot;
        }

        /// <summary>
        /// Наполнить заново из снимка. <paramref name="resolve"/> превращает сохранённый id обратно
        /// в ассет — передай тот поиск, который в итоге появится в проекте; инвентарь намеренно
        /// не владеет никаким реестром. Ненайденные id пропускаются с предупреждением,
        /// но никогда не теряются молча.
        /// </summary>
        public void RestoreState(InventorySnapshot snapshot, Func<string, ResourceDefinition> resolve)
        {
            Clear();
            if (snapshot?.Entries == null || resolve == null) return;

            foreach (var e in snapshot.Entries)
            {
                if (string.IsNullOrEmpty(e.ResourceId) || e.Amount <= 0) continue;

                var resource = resolve(e.ResourceId);
                if (resource == null)
                {
                    Debug.LogWarning("[Inventory] Не найден ресурс '" + e.ResourceId + "' при загрузке");
                    continue;
                }

                TryAdd(resource, e.Amount);
            }
        }

        /// <summary>
        /// Сколько единиц этого ресурса инвентарь ещё примет. Публичный не для галочки:
        /// мозг фермера спрашивает это перед доставкой — нести урожай на склад, который
        /// его не возьмёт, значит уничтожить урожай и соврать игроку об успехе.
        /// </summary>
        public int FreeUnitsFor(ResourceDefinition resource)
        {
            switch (CapacityMode)
            {
                case InventoryCapacity.Units:
                    return Mathf.Max(0, Capacity - TotalUnits);
                case InventoryCapacity.Slots:
                {
                    long units = FreeSlotUnits();

                    // Верхний стек этого ресурса мог остаться недобитым — в него ещё влезет,
                    // и это единственное, что отличает «место для него» от «места вообще».
                    int tail = GetAmount(resource) % _stackSize;
                    if (tail > 0) units += _stackSize - tail;

                    return ClampToInt(units);
                }
                default:
                    return int.MaxValue;
            }
        }

        /// <summary>Целые свободные ячейки, переведённые в единицы. long — чтобы большой склад не переполнил int.</summary>
        private long FreeSlotUnits()
        {
            int free = Capacity - UsedSlots;
            return free > 0 ? (long)free * _stackSize : 0L;
        }

        private static int ClampToInt(long units) => units >= int.MaxValue ? int.MaxValue : (int)units;

        private int IndexOf(ResourceDefinition resource)
        {
            if (resource == null) return -1;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Resource == resource) return i;
            return -1;
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;
            try { handler(this); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void Raise(Action<IInventory, ResourceDefinition, int> handler,
                           ResourceDefinition resource, int amount)
        {
            if (handler == null) return;
            try { handler(this, resource, amount); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public override string ToString()
        {
            if (_entries.Count == 0) return "пусто";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_entries[i]);
            }
            return sb.ToString();
        }
    }
}

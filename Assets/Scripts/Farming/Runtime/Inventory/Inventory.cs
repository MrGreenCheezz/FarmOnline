using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Serialisable contents of an inventory, keyed by resource id rather than asset reference.</summary>
    [Serializable]
    public struct InventorySnapshotEntry
    {
        public string ResourceId;
        public int Amount;
    }

    /// <summary>
    /// Save-ready copy of an inventory. Ids instead of object references, so it survives a rebuild
    /// of the asset database and can go straight into JSON.
    /// </summary>
    [Serializable]
    public sealed class InventorySnapshot
    {
        public InventorySnapshotEntry[] Entries = Array.Empty<InventorySnapshotEntry>();
    }

    /// <summary>
    /// Default <see cref="IInventory"/>. Plain C# — no MonoBehaviour, no scene dependency — so it
    /// works equally as a character's backpack, a building's store, or a throwaway in a test.
    /// <para>
    /// Contents live in a list rather than a dictionary: inventories hold tens of entries at most,
    /// a linear scan beats hashing at that size, and the stable ordering means a UI list does not
    /// reshuffle itself every time something is picked up.
    /// </para>
    /// </summary>
    public class Inventory : IInventory
    {
        private readonly List<InventoryEntry> _entries = new List<InventoryEntry>(8);

        public Inventory() : this(InventoryCapacity.Unlimited, 0) { }

        public Inventory(InventoryCapacity mode, int capacity, bool allowOverflow = false)
        {
            CapacityMode = mode;
            Capacity = capacity;
            AllowOverflow = allowOverflow;
        }

        public InventoryCapacity CapacityMode { get; set; }

        /// <summary>Meaning depends on <see cref="CapacityMode"/>: units or distinct resources.</summary>
        public int Capacity { get; set; }

        /// <summary>
        /// When true a single add is never truncated, even if it exceeds capacity — the container
        /// simply reports <see cref="IsFull"/> afterwards.
        /// <para>
        /// The farmer's backpack needs this: a level-3 plot yields 4 at once, and refusing the
        /// overflow would make any yield larger than the free space permanently unharvestable.
        /// Fixed containers such as chests should leave it off.
        /// </para>
        /// </summary>
        public bool AllowOverflow { get; set; }

        public int TotalUnits { get; private set; }
        public int DistinctCount => _entries.Count;
        public bool IsEmpty => TotalUnits == 0;

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
                    case InventoryCapacity.Slots: return DistinctCount >= Capacity;
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
                    // Slots do not bound units — only how many different resources fit.
                    case InventoryCapacity.Slots: return DistinctCount < Capacity ? int.MaxValue : 0;
                    default: return int.MaxValue;
                }
            }
        }

        // ---- reads ----

        public int GetAmount(ResourceDefinition resource)
        {
            int i = IndexOf(resource);
            return i >= 0 ? _entries[i].Amount : 0;
        }

        public bool Contains(ResourceDefinition resource, int amount = 1) =>
            amount <= 0 || GetAmount(resource) >= amount;

        // ---- writes ----

        /// <summary><see cref="IResourceSink"/> entry point. Overflow is reported via <see cref="Rejected"/>.</summary>
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
            // Backwards: entries that empty out are removed, and that shifts everything after them.
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

        // ---- saving ----

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
        /// Refill from a snapshot. <paramref name="resolve"/> turns a saved id back into the asset —
        /// pass whatever lookup the project ends up with; the inventory deliberately owns no registry.
        /// Unresolved ids are skipped and reported, never silently dropped.
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

        // ---- internals ----

        private int FreeUnitsFor(ResourceDefinition resource)
        {
            switch (CapacityMode)
            {
                case InventoryCapacity.Units:
                    return Mathf.Max(0, Capacity - TotalUnits);
                case InventoryCapacity.Slots:
                    // An existing stack never runs out of room; a new one needs a free slot.
                    return IndexOf(resource) >= 0 || DistinctCount < Capacity ? int.MaxValue : 0;
                default:
                    return int.MaxValue;
            }
        }

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

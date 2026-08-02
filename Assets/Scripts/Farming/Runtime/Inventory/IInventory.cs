using System;
using System.Collections.Generic;

namespace Farm.Farming
{
    /// <summary>How an inventory decides that it is full.</summary>
    public enum InventoryCapacity
    {
        /// <summary>No limit. World storage starts here.</summary>
        Unlimited = 0,
        /// <summary>Capacity counts total units across every resource. A character's carry weight.</summary>
        Units = 1,
        /// <summary>Capacity counts distinct resources; each one stacks without limit. Chest-style slots.</summary>
        Slots = 2
    }

    /// <summary>One resource and how much of it is held. Immutable — replace, don't mutate.</summary>
    public readonly struct InventoryEntry
    {
        public readonly ResourceDefinition Resource;
        public readonly int Amount;

        public InventoryEntry(ResourceDefinition resource, int amount)
        {
            Resource = resource;
            Amount = amount;
        }

        public InventoryEntry WithAmount(int amount) => new InventoryEntry(Resource, amount);

        public override string ToString() =>
            Amount + "x " + (Resource != null ? Resource.Id : "<none>");
    }

    /// <summary>
    /// A container of resources. Everything that holds stuff implements this — the character's
    /// backpack today, barns, silos and chests later — so transfer, UI binding and save code are
    /// written once against the interface rather than per container.
    /// <para>
    /// It extends <see cref="IResourceSink"/>, so any inventory can be handed straight to
    /// <see cref="Growable.TryHarvest(out HarvestResult, IResourceSink)"/> as a harvest destination.
    /// </para>
    /// </summary>
    public interface IInventory : IResourceSink
    {
        /// <summary>How this container decides it is full. The UI needs it to know what to draw.</summary>
        InventoryCapacity CapacityMode { get; }

        /// <summary>Units or slots depending on <see cref="CapacityMode"/>; meaningless when unlimited.</summary>
        int Capacity { get; }

        /// <summary>Sum of all amounts.</summary>
        int TotalUnits { get; }

        /// <summary>How many different resources are held.</summary>
        int DistinctCount { get; }

        bool IsEmpty { get; }
        bool IsFull { get; }

        /// <summary>Units that still fit. <see cref="int.MaxValue"/> when unlimited.</summary>
        int FreeUnits { get; }

        /// <summary>Live view of the contents. Stable order — safe to drive a UI list from.</summary>
        IReadOnlyList<InventoryEntry> Entries { get; }

        int GetAmount(ResourceDefinition resource);
        bool Contains(ResourceDefinition resource, int amount = 1);

        /// <summary>Add up to <paramref name="amount"/>. Returns how much was actually accepted.</summary>
        int TryAdd(ResourceDefinition resource, int amount);

        /// <summary>Take up to <paramref name="amount"/>. Returns how much was actually removed.</summary>
        int TryRemove(ResourceDefinition resource, int amount);

        /// <summary>Push everything into a plain sink. Returns units moved.</summary>
        int TransferTo(IResourceSink target);

        /// <summary>Push into another inventory, respecting its capacity. Returns units moved.</summary>
        int TransferTo(IInventory target);

        void Clear();

        /// <summary>Anything changed. The cheap hook for repainting UI.</summary>
        event Action<IInventory> Changed;

        /// <summary>Units went in.</summary>
        event Action<IInventory, ResourceDefinition, int> Added;

        /// <summary>Units went out.</summary>
        event Action<IInventory, ResourceDefinition, int> Removed;

        /// <summary>Units did not fit and were dropped. Wire this before players can lose loot silently.</summary>
        event Action<IInventory, ResourceDefinition, int> Rejected;
    }
}

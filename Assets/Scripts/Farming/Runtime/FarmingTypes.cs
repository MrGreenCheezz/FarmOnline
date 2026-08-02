namespace Farm.Farming
{
    /// <summary>Broad family a growable belongs to. Merge rules and AI priorities key off this.</summary>
    public enum ResourceCategory
    {
        Crop = 0,
        Livestock = 1,
        Ore = 2
    }

    /// <summary>Lifecycle state of a single growable slot.</summary>
    public enum GrowthPhase
    {
        /// <summary>Nothing planted — the slot is free.</summary>
        Empty = 0,
        /// <summary>Planted and advancing through stages.</summary>
        Growing = 1,
        /// <summary>On the final stage and harvestable.</summary>
        Ready = 2,
        /// <summary>Was ready but sat unharvested past the wither timeout.</summary>
        Withered = 3
    }

    /// <summary>What a single harvest produced. Returned by <see cref="Growable.TryHarvest"/>.</summary>
    public readonly struct HarvestResult
    {
        public readonly Growable Source;
        public readonly ResourceDefinition Resource;
        public readonly int Amount;
        /// <summary>Merge level of the growable at harvest time — the amount already accounts for it.</summary>
        public readonly int Level;

        public HarvestResult(Growable source, ResourceDefinition resource, int amount, int level)
        {
            Source = source;
            Resource = resource;
            Amount = amount;
            Level = level;
        }

        public override string ToString()
        {
            string id = Resource != null ? Resource.Id : "<none>";
            return Amount + "x " + id + " (lvl " + Level + ")";
        }
    }
}

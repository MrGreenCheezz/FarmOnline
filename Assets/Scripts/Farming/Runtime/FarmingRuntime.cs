using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Time source for all growth math. Growth is derived from timestamps rather than
    /// accumulated per-frame deltas, so swapping this for a persisted clock is all that
    /// offline progress requires.
    /// </summary>
    public interface IGameClock
    {
        /// <summary>Seconds. Must be monotonically non-decreasing.</summary>
        double Now { get; }
    }

    /// <summary>Default clock: Unity's scaled time since startup. Resets when the app restarts.</summary>
    public sealed class UnityGameClock : IGameClock
    {
        public double Now => Time.timeAsDouble;
    }

    /// <summary>
    /// Wall-clock seconds since the Unix epoch. Assign this to <see cref="FarmingRuntime.Clock"/>
    /// once saving exists and crops keep growing while the game is closed.
    /// </summary>
    public sealed class UnixGameClock : IGameClock
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public double Now => (DateTime.UtcNow - Epoch).TotalSeconds;
    }

    /// <summary>
    /// Where harvested resources land. Storage isn't built yet, so the farming system depends
    /// only on this seam — the real inventory can be dropped in without touching any growable.
    /// </summary>
    public interface IResourceSink
    {
        void Add(ResourceDefinition resource, int amount);
    }

    /// <summary>Placeholder sink: logs the yield and keeps a running total per resource id.</summary>
    public sealed class DebugResourceSink : IResourceSink
    {
        private readonly System.Collections.Generic.Dictionary<string, int> _totals =
            new System.Collections.Generic.Dictionary<string, int>();

        public System.Collections.Generic.IReadOnlyDictionary<string, int> Totals => _totals;

        public void Add(ResourceDefinition resource, int amount)
        {
            if (resource == null || amount <= 0) return;

            _totals.TryGetValue(resource.Id, out int current);
            _totals[resource.Id] = current + amount;

            if (FarmingRuntime.LogHarvests)
                Debug.Log("[Farming] +" + amount + " " + resource.Id + " (всего " + _totals[resource.Id] + ")");
        }
    }

    /// <summary>
    /// Single place to swap the farming system's external dependencies. Set these once at
    /// startup — every growable reads through here instead of holding its own reference.
    /// </summary>
    public static class FarmingRuntime
    {
        private static IGameClock _clock;
        private static IResourceSink _sink;

        public static IGameClock Clock
        {
            get => _clock ?? (_clock = new UnityGameClock());
            set => _clock = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static IResourceSink Sink
        {
            get => _sink ?? (_sink = new DebugResourceSink());
            set => _sink = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Turn off once a real inventory UI exists.</summary>
        public static bool LogHarvests = true;

        public static double Now => Clock.Now;

        // Statics survive play-mode restarts when domain reload is disabled, so clear them explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _clock = null;
            _sink = null;
            LogHarvests = true;
        }
    }
}

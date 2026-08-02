using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Live index of every growable in the scene, plus a separate list of the ones ready to harvest.
    /// <para>
    /// This is the query surface the autonomous character will live on: instead of scanning the
    /// world it asks "what is ripe near me?" and gets an answer without touching a single plot that
    /// is still growing. Both lists use swap-removal with a cached index, so registering,
    /// unregistering and flipping ready state are all O(1).
    /// </para>
    /// </summary>
    public static class GrowableRegistry
    {
        private static readonly List<Growable> _all = new List<Growable>(256);
        private static readonly List<Growable> _ready = new List<Growable>(64);

        public static IReadOnlyList<Growable> All => _all;

        /// <summary>Everything currently harvestable. Do not hold across frames — it is mutated in place.</summary>
        public static IReadOnlyList<Growable> Ready => _ready;

        public static int Count => _all.Count;
        public static int ReadyCount => _ready.Count;

        internal static void Register(Growable g)
        {
            if (g == null || g.RegistryIndex >= 0) return;
            g.RegistryIndex = _all.Count;
            _all.Add(g);
        }

        internal static void Unregister(Growable g)
        {
            if (g == null) return;
            SetReady(g, false);

            int i = g.RegistryIndex;
            if (i < 0 || i >= _all.Count || _all[i] != g) { g.RegistryIndex = -1; return; }

            int last = _all.Count - 1;
            _all[i] = _all[last];
            if (_all[i] != null) _all[i].RegistryIndex = i;
            _all.RemoveAt(last);
            g.RegistryIndex = -1;
        }

        internal static void SetReady(Growable g, bool ready)
        {
            if (g == null) return;

            if (ready)
            {
                if (g.ReadyIndex >= 0) return;
                g.ReadyIndex = _ready.Count;
                _ready.Add(g);
                return;
            }

            int i = g.ReadyIndex;
            if (i < 0 || i >= _ready.Count || _ready[i] != g) { g.ReadyIndex = -1; return; }

            int last = _ready.Count - 1;
            _ready[i] = _ready[last];
            if (_ready[i] != null) _ready[i].ReadyIndex = i;
            _ready.RemoveAt(last);
            g.ReadyIndex = -1;
        }

        /// <summary>
        /// Closest harvestable growable to <paramref name="position"/>, optionally limited to one
        /// category. Returns null when nothing qualifies.
        /// </summary>
        public static Growable FindNearestReady(Vector3 position, ResourceCategory? category = null,
                                                float maxDistance = float.PositiveInfinity)
        {
            Growable best = null;
            float bestSqr = maxDistance >= float.PositiveInfinity
                ? float.PositiveInfinity
                : maxDistance * maxDistance;

            for (int i = 0; i < _ready.Count; i++)
            {
                var g = _ready[i];
                if (g == null) continue;
                if (category.HasValue && g.Category != category.Value) continue;

                float sqr = (g.transform.position - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = g;
            }

            return best;
        }

        /// <summary>Fills <paramref name="results"/> with harvestable growables of a category. Clears it first.</summary>
        public static void GetReady(List<Growable> results, ResourceCategory? category = null)
        {
            if (results == null) return;
            results.Clear();

            for (int i = 0; i < _ready.Count; i++)
            {
                var g = _ready[i];
                if (g == null) continue;
                if (category.HasValue && g.Category != category.Value) continue;
                results.Add(g);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            _ready.Clear();
        }
    }
}

using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Farm-wide event bus. Every growable also exposes the same events locally
    /// (<see cref="Growable.Planted"/> and friends) — use those when you care about one plot,
    /// use these when a system cares about all of them: UI counters, quests, audio, statistics,
    /// and the autonomous character deciding what to do next.
    /// </summary>
    public static class FarmingEvents
    {
        /// <summary>Something was planted. Fires after the growable is fully initialised.</summary>
        public static event Action<Growable> Planted;

        /// <summary>A growth tick landed: the growable moved onto the stage index passed along.</summary>
        public static event Action<Growable, int> StageAdvanced;

        /// <summary>Reached its final stage and can now be harvested.</summary>
        public static event Action<Growable> Ready;

        /// <summary>Harvested. The yield has already been pushed to <see cref="FarmingRuntime.Sink"/>.</summary>
        public static event Action<Growable, HarvestResult> Harvested;

        /// <summary>Sat ripe past its wither timeout and spoiled.</summary>
        public static event Action<Growable> Withered;

        /// <summary>Emptied — harvested without regrow, withered away, or cleared by hand.</summary>
        public static event Action<Growable> Cleared;

        /// <summary>
        /// Two growables merged. First argument is the survivor (already levelled up),
        /// second is the one being destroyed — read what you need from it now.
        /// </summary>
        public static event Action<Growable, Growable> Merged;

        internal static void RaisePlanted(Growable g) => Safe(Planted, g, nameof(Planted));
        internal static void RaiseReady(Growable g) => Safe(Ready, g, nameof(Ready));
        internal static void RaiseWithered(Growable g) => Safe(Withered, g, nameof(Withered));
        internal static void RaiseCleared(Growable g) => Safe(Cleared, g, nameof(Cleared));

        internal static void RaiseStageAdvanced(Growable g, int stage)
        {
            var handler = StageAdvanced;
            if (handler == null) return;
            try { handler(g, stage); }
            catch (Exception e) { Debug.LogException(e, g); }
        }

        internal static void RaiseMerged(Growable survivor, Growable absorbed)
        {
            var handler = Merged;
            if (handler == null) return;
            try { handler(survivor, absorbed); }
            catch (Exception e) { Debug.LogException(e, survivor); }
        }

        internal static void RaiseHarvested(Growable g, in HarvestResult result)
        {
            var handler = Harvested;
            if (handler == null) return;
            try { handler(g, result); }
            catch (Exception e) { Debug.LogException(e, g); }
        }

        // One misbehaving subscriber must not stop the rest of the farm from ticking.
        private static void Safe(Action<Growable> handler, Growable g, string label)
        {
            if (handler == null) return;
            try { handler(g); }
            catch (Exception e)
            {
                Debug.LogError("[Farming] Ошибка в подписчике " + label, g);
                Debug.LogException(e, g);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Planted = null;
            StageAdvanced = null;
            Ready = null;
            Harvested = null;
            Withered = null;
            Cleared = null;
            Merged = null;
        }
    }
}

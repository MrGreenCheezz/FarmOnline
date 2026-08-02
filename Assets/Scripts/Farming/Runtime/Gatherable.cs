using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>How the player takes it.</summary>
    public enum GatherMode
    {
        /// <summary>One unit per click. Fast, twitchy, good for things that move.</summary>
        Click = 0,
        /// <summary>Hold the button to fill a bar. Slower per unit, but pays more per node.</summary>
        Hold = 1
    }

    /// <summary>
    /// Something the player picks up by hand, as opposed to everything the farmer does on his own.
    /// <para>
    /// This is the counterweight to the farmer's autonomy. He runs the farm while you are away; a
    /// gatherable only ever yields to someone actually sitting there. That is what gives the night
    /// shift a point — the farm does not stop because he sleeps, it changes hands.
    /// </para>
    /// <para>
    /// Yield goes straight to world storage rather than through the farmer's backpack: the player is
    /// not standing anywhere, so there is nothing to carry home, and routing it through a sleeping
    /// character would just mean the night's work sits in his pockets until morning.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Gatherable")]
    public sealed class Gatherable : MonoBehaviour
    {
        [Header("Что даёт")]
        [SerializeField] private ResourceDefinition _resource;

        [Tooltip("Сколько всего единиц в этом узле.")]
        [SerializeField, Min(1)] private int _amount = 3;

        [Header("Как берётся")]
        [SerializeField] private GatherMode _mode = GatherMode.Click;

        [Tooltip("Для Hold: сколько секунд удержания на одну единицу.")]
        [SerializeField, Min(0.05f)] private float _holdSeconds = 0.7f;

        [Tooltip("Высота «центра» при наведении курсора.")]
        [SerializeField, Min(0f)] private float _aimHeight = 0.35f;

        [Tooltip("Убирать объект, когда всё собрано.")]
        [SerializeField] private bool _removeWhenEmpty = true;

        private int _remaining;
        private float _progress;
        private bool _started;

        internal int RegistryIndex = -1;

        /// <summary>Units were taken. Second argument is how many.</summary>
        public event Action<Gatherable, int> Gathered;

        /// <summary>Nothing left. Fires once, before the object is removed.</summary>
        public event Action<Gatherable> Depleted;

        public ResourceDefinition Resource => _resource;
        public GatherMode Mode => _mode;
        public float AimHeight => _aimHeight;
        public int Remaining => _remaining;
        public bool IsEmpty => _remaining <= 0;

        /// <summary>Progress toward the next unit while holding, 0..1. Zero in click mode.</summary>
        public float Progress01 => _mode == GatherMode.Hold ? Mathf.Clamp01(_progress / _holdSeconds) : 0f;

        private void Awake() => EnsureStarted();

        private void OnEnable()
        {
            EnsureStarted();
            GatherableRegistry.Register(this);
        }

        private void OnDisable() => GatherableRegistry.Unregister(this);

        private void EnsureStarted()
        {
            if (_started) return;
            _started = true;
            _remaining = Mathf.Max(1, _amount);
        }

        /// <summary>Set up a freshly spawned node. Call before it is shown.</summary>
        public void Configure(ResourceDefinition resource, int amount, GatherMode mode)
        {
            _resource = resource;
            _amount = Mathf.Max(1, amount);
            _mode = mode;
            _remaining = _amount;
            _progress = 0f;
            _started = true;
        }

        /// <summary>Take one unit. Click mode only — returns false when there is nothing left.</summary>
        public bool Collect()
        {
            if (IsEmpty) return false;
            Take(1);
            return true;
        }

        /// <summary>
        /// Keep holding. Returns true on the frames where a unit actually came out.
        /// Progress is kept on the node, not on the input, so letting go and coming back
        /// does not silently reset the work already done.
        /// </summary>
        public bool Hold(float deltaTime)
        {
            if (IsEmpty || deltaTime <= 0f) return false;

            _progress += deltaTime;
            if (_progress < _holdSeconds) return false;

            int units = Mathf.FloorToInt(_progress / _holdSeconds);
            _progress -= units * _holdSeconds;

            Take(Mathf.Min(units, _remaining));
            return true;
        }

        private void Take(int units)
        {
            if (units <= 0) return;

            _remaining -= units;

            if (_resource != null)
                FarmingRuntime.Sink.Add(_resource, units);

            Raise(Gathered, units);

            if (_remaining > 0) return;

            var handler = Depleted;
            if (handler != null)
            {
                try { handler(this); }
                catch (Exception e) { Debug.LogException(e, this); }
            }

            if (_removeWhenEmpty) Destroy(gameObject);
        }

        private void Raise(Action<Gatherable, int> handler, int amount)
        {
            if (handler == null) return;
            try { handler(this, amount); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }

    /// <summary>Live index of everything the player can pick up. Same O(1) swap-removal as the rest.</summary>
    public static class GatherableRegistry
    {
        private static readonly List<Gatherable> _all = new List<Gatherable>(32);

        public static IReadOnlyList<Gatherable> All => _all;
        public static int Count => _all.Count;

        internal static void Register(Gatherable g)
        {
            if (g == null || g.RegistryIndex >= 0) return;
            g.RegistryIndex = _all.Count;
            _all.Add(g);
        }

        internal static void Unregister(Gatherable g)
        {
            if (g == null) return;

            int i = g.RegistryIndex;
            if (i < 0 || i >= _all.Count || _all[i] != g) { g.RegistryIndex = -1; return; }

            int last = _all.Count - 1;
            _all[i] = _all[last];
            if (_all[i] != null) _all[i].RegistryIndex = i;
            _all.RemoveAt(last);
            g.RegistryIndex = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all.Clear();
    }
}

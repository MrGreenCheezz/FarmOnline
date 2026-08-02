using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Something that wants a callback at a specific point in time.</summary>
    public interface IGrowthScheduled
    {
        void OnScheduledDue(double now);
    }

    /// <summary>
    /// Single timer for the whole farm.
    /// <para>
    /// Growables never run their own <c>Update</c>. Each one registers once and asks to be woken
    /// at the moment its next stage is due; the scheduler keeps those wake-ups in a binary min-heap
    /// and each frame touches only the entries that have actually come due. Idle cost is one
    /// comparison per frame no matter how many thousands of plots exist.
    /// </para>
    /// <para>
    /// Cancelling is lazy: every handle carries a version, bumped on each (re)schedule, so stale
    /// heap entries are recognised and dropped when popped instead of being searched for and removed.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GrowthScheduler : MonoBehaviour
    {
        private struct Entry
        {
            public double Due;
            public int Handle;
            public int Version;
        }

        public const int InvalidHandle = -1;

        private static GrowthScheduler _instance;
        private static bool _quitting;

        private readonly List<Entry> _heap = new List<Entry>(128);
        private readonly List<IGrowthScheduled> _targets = new List<IGrowthScheduled>(128);
        private readonly List<int> _versions = new List<int>(128);
        private readonly Stack<int> _freeHandles = new Stack<int>();

        /// <summary>
        /// Ceiling on wake-ups processed per frame. Stops a huge time jump (offline catch-up,
        /// Time.timeScale spike) from stalling one frame; the rest resume next frame.
        /// </summary>
        [SerializeField, Min(1)] private int _maxWakeUpsPerFrame = 256;

        public int PendingCount => _heap.Count;
        public int RegisteredCount => _targets.Count - _freeHandles.Count;

        /// <summary>
        /// The scheduler if one already exists, without creating it. Use this during teardown —
        /// spawning a GameObject while a scene unloads makes Unity complain.
        /// </summary>
        public static GrowthScheduler Existing => _instance;

        public static GrowthScheduler Instance
        {
            get
            {
                if (_instance != null || _quitting || !Application.isPlaying) return _instance;

                var go = new GameObject("[GrowthScheduler]");
                _instance = go.AddComponent<GrowthScheduler>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnApplicationQuit() => _quitting = true;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public int Register(IGrowthScheduled target)
        {
            if (target == null) return InvalidHandle;

            if (_freeHandles.Count > 0)
            {
                int reused = _freeHandles.Pop();
                _targets[reused] = target;
                _versions[reused]++;          // invalidate anything left over from the previous owner
                return reused;
            }

            _targets.Add(target);
            _versions.Add(0);
            return _targets.Count - 1;
        }

        public void Unregister(int handle)
        {
            if (!IsValid(handle)) return;
            _targets[handle] = null;
            _versions[handle]++;
            _freeHandles.Push(handle);
        }

        /// <summary>Wake <paramref name="handle"/> at <paramref name="dueTime"/>, replacing any pending wake-up.</summary>
        public void Schedule(int handle, double dueTime)
        {
            if (!IsValid(handle)) return;
            _versions[handle]++;
            Push(new Entry { Due = dueTime, Handle = handle, Version = _versions[handle] });
        }

        /// <summary>Drop the pending wake-up for <paramref name="handle"/> without unregistering it.</summary>
        public void Cancel(int handle)
        {
            if (!IsValid(handle)) return;
            _versions[handle]++;
        }

        private bool IsValid(int handle) => handle >= 0 && handle < _targets.Count;

        private void Update()
        {
            if (_heap.Count == 0) return;

            double now = FarmingRuntime.Now;
            int budget = _maxWakeUpsPerFrame;

            while (_heap.Count > 0 && _heap[0].Due <= now && budget > 0)
            {
                Entry e = Pop();

                // Stale entry: the handle was rescheduled, cancelled or recycled after this was queued.
                if (e.Version != _versions[e.Handle]) continue;

                var target = _targets[e.Handle];
                if (target == null) continue;

                budget--;
                target.OnScheduledDue(now);
            }
        }

        // ---- binary min-heap over Entry.Due ----

        private void Push(Entry e)
        {
            _heap.Add(e);
            int i = _heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_heap[parent].Due <= _heap[i].Due) break;
                (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
                i = parent;
            }
        }

        private Entry Pop()
        {
            Entry top = _heap[0];
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            last--;

            int i = 0;
            while (true)
            {
                int left = (i << 1) + 1;
                int right = left + 1;
                int smallest = i;

                if (left <= last && _heap[left].Due < _heap[smallest].Due) smallest = left;
                if (right <= last && _heap[right].Due < _heap[smallest].Due) smallest = right;
                if (smallest == i) break;

                (_heap[smallest], _heap[i]) = (_heap[i], _heap[smallest]);
                i = smallest;
            }

            return top;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _quitting = false;
        }
    }
}

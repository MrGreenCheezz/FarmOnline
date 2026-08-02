using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// One plot / pen / vein: something that grows through stages and can be harvested.
    /// <para>
    /// Holds no timer of its own. Progress is derived from the timestamp it was planted at, so the
    /// state is correct no matter how much time passes between wake-ups — a plot that slept through
    /// four stages catches up in a single call. The only per-frame work in the whole system happens
    /// in <see cref="GrowthScheduler"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Growable")]
    public sealed class Growable : MonoBehaviour, IGrowthScheduled
    {
        [SerializeField] private GrowableDefinition _definition;

        [Tooltip("Merge level. Two level-N growables merge into one level-N+1; level scales the yield.")]
        [SerializeField, Min(1)] private int _level = 1;

        [Tooltip("Multiplies growth speed. Buffs, tools and buildings hook in here.")]
        [SerializeField, Min(0.01f)] private float _growthSpeed = 1f;

        [SerializeField] private bool _plantOnStart = true;

        private GrowthPhase _phase = GrowthPhase.Empty;
        private int _stageIndex;
        private double _plantedAt;
        private double _readyAt;
        private int _handle = GrowthScheduler.InvalidHandle;

        // Slot bookkeeping owned by GrowableRegistry — keeps its list operations O(1).
        internal int RegistryIndex = -1;
        internal int ReadyIndex = -1;

        #region Per-instance events

        /// <summary>A new growth cycle began. Also fires when a regrowing plot restarts after harvest.</summary>
        public event Action<Growable> Planted;

        /// <summary>Growth tick: moved onto the stage index passed along.</summary>
        public event Action<Growable, int> StageAdvanced;

        /// <summary>Reached the final stage and can be harvested.</summary>
        public event Action<Growable> Ready;

        /// <summary>Harvested; the yield is already in the sink.</summary>
        public event Action<Growable, HarvestResult> Harvested;

        /// <summary>Spoiled after sitting ripe too long.</summary>
        public event Action<Growable> Withered;

        /// <summary>Went empty.</summary>
        public event Action<Growable> Cleared;

        /// <summary>Levelled up by absorbing the growable passed along, which is about to be destroyed.</summary>
        public event Action<Growable, Growable> Merged;

        #endregion

        #region State

        public GrowableDefinition Definition => _definition;
        public GrowthPhase Phase => _phase;
        public int StageIndex => _stageIndex;
        public int StageCount => _definition != null ? _definition.StageCount : 0;
        public bool IsReady => _phase == GrowthPhase.Ready;
        public bool IsEmpty => _phase == GrowthPhase.Empty;
        public double PlantedAt => _plantedAt;
        public ResourceCategory Category => _definition != null ? _definition.Category : ResourceCategory.Crop;

        public int Level
        {
            get => _level;
            set => _level = Mathf.Max(1, value);
        }

        /// <summary>
        /// Growth rate multiplier. Changing it mid-growth keeps the progress already made —
        /// the plant timestamp is rebased rather than the plot restarting.
        /// </summary>
        public float GrowthSpeed
        {
            get => _growthSpeed;
            set
            {
                float v = Mathf.Max(0.01f, value);
                if (Mathf.Approximately(v, _growthSpeed)) return;

                if (_phase == GrowthPhase.Growing)
                {
                    double now = FarmingRuntime.Now;
                    double done = (now - _plantedAt) * _growthSpeed;   // growth-seconds already banked
                    _growthSpeed = v;
                    _plantedAt = now - done / v;
                }
                else
                {
                    _growthSpeed = v;
                }

                ScheduleNext();
            }
        }

        /// <summary>0..1 across the whole chain, for progress bars.</summary>
        public float Progress01
        {
            get
            {
                if (_phase == GrowthPhase.Ready || _phase == GrowthPhase.Withered) return 1f;
                if (_phase != GrowthPhase.Growing || _definition == null) return 0f;

                double total = _definition.TotalGrowTime;
                if (total <= 0.0) return 1f;
                return Mathf.Clamp01((float)(ElapsedGrowth(FarmingRuntime.Now) / total));
            }
        }

        /// <summary>Real seconds until harvestable. 0 when ready, -1 when nothing is growing.</summary>
        public double TimeUntilReady
        {
            get
            {
                if (_phase == GrowthPhase.Ready) return 0.0;
                if (_phase != GrowthPhase.Growing || _definition == null) return -1.0;

                double remaining = _definition.TotalGrowTime - ElapsedGrowth(FarmingRuntime.Now);
                return Math.Max(0.0, remaining / _growthSpeed);
            }
        }

        /// <summary>Real seconds until the next stage change. -1 when not growing.</summary>
        public double TimeUntilNextStage
        {
            get
            {
                if (_phase != GrowthPhase.Growing || _definition == null) return -1.0;

                double boundary = _definition.StageStartTime(_stageIndex + 1);
                double remaining = boundary - ElapsedGrowth(FarmingRuntime.Now);
                return Math.Max(0.0, remaining / _growthSpeed);
            }
        }

        #endregion

        #region Unity lifecycle

        private void OnEnable()
        {
            GrowableRegistry.Register(this);

            var scheduler = GrowthScheduler.Instance;
            if (scheduler != null) _handle = scheduler.Register(this);

            // Time may have moved on while disabled — catch up before resuming.
            if (_phase == GrowthPhase.Growing) AdvanceTo(FarmingRuntime.Now);
            else if (_phase == GrowthPhase.Ready) GrowableRegistry.SetReady(this, true);

            ScheduleNext();
        }

        private void Start()
        {
            if (_plantOnStart && _phase == GrowthPhase.Empty && _definition != null) Plant();
        }

        private void OnDisable()
        {
            // Existing, not Instance: never spawn the scheduler while the scene is tearing down.
            var scheduler = GrowthScheduler.Existing;
            if (scheduler != null && _handle != GrowthScheduler.InvalidHandle) scheduler.Unregister(_handle);
            _handle = GrowthScheduler.InvalidHandle;

            GrowableRegistry.Unregister(this);
        }

        #endregion

        #region Public API

        /// <summary>Plant the assigned definition at the current level.</summary>
        public void Plant() => Plant(_definition, _level);

        /// <summary>Plant <paramref name="definition"/> from the first stage.</summary>
        public void Plant(GrowableDefinition definition, int level)
        {
            if (definition == null)
            {
                Debug.LogWarning("[Farming] Посадка без GrowableDefinition", this);
                return;
            }

            if (definition.StageCount == 0)
            {
                Debug.LogWarning("[Farming] У '" + definition.Id + "' нет ни одной стадии роста", this);
                return;
            }

            _definition = definition;
            _level = Mathf.Max(1, level);
            StartCycle(0);
        }

        /// <summary>
        /// Harvest if ripe. The yield goes to <see cref="FarmingRuntime.Sink"/>, then the plot either
        /// restarts (definitions with Regrows) or empties.
        /// </summary>
        public bool TryHarvest(out HarvestResult result) => TryHarvest(out result, null);

        /// <summary>
        /// Harvest into a specific sink. A character carrying the crop home passes its backpack here
        /// so the yield lands in the world storage only once it has actually been delivered.
        /// </summary>
        /// <param name="into">Destination for the yield. Null routes it to <see cref="FarmingRuntime.Sink"/>.</param>
        public bool TryHarvest(out HarvestResult result, IResourceSink into)
        {
            result = default;
            if (_phase != GrowthPhase.Ready || _definition == null) return false;

            int amount = _definition.YieldFor(_level);
            result = new HarvestResult(this, _definition.YieldResource, amount, _level);

            (into ?? FarmingRuntime.Sink).Add(result.Resource, result.Amount);

            GrowableRegistry.SetReady(this, false);
            Raise(Harvested, result);
            FarmingEvents.RaiseHarvested(this, result);

            if (_definition.Regrows) StartCycle(_definition.RegrowStage);
            else ClearInternal();

            return true;
        }

        /// <summary>Convenience overload for callers that don't need the details.</summary>
        public bool TryHarvest() => TryHarvest(out _);

        /// <summary>Empty the plot from any state.</summary>
        public void Clear()
        {
            if (_phase == GrowthPhase.Empty) return;
            GrowableRegistry.SetReady(this, false);
            ClearInternal();
        }

        /// <summary>
        /// Can <paramref name="other"/> be merged into this one? Same crop, same level, both
        /// actually planted — the rule the whole progression rests on, so it lives here rather
        /// than in whatever happens to be dragging things around.
        /// </summary>
        public bool CanMergeWith(Growable other)
        {
            if (other == null || other == this) return false;
            if (_definition == null || other._definition != _definition) return false;
            if (_level != other._level) return false;
            return _phase != GrowthPhase.Empty && other._phase != GrowthPhase.Empty;
        }

        /// <summary>
        /// Absorb <paramref name="other"/>: this plot goes up one level and the other is destroyed.
        /// <para>
        /// Growth progress is deliberately kept, not reset. Merging is meant to be a pure gain —
        /// charging the player a fresh growth cycle for it would make the core action feel like a
        /// setback. If balance later needs a cost, this is the one line to change.
        /// </para>
        /// </summary>
        public bool TryMergeWith(Growable other)
        {
            if (!CanMergeWith(other)) return false;

            _level++;

            Raise(Merged, other);
            FarmingEvents.RaiseMerged(this, other);

            other.Clear();
            Destroy(other.gameObject);
            return true;
        }

        /// <summary>Skip straight to ripe. For boosters, cheats and tests.</summary>
        public void ForceReady()
        {
            if (_definition == null || _phase == GrowthPhase.Ready) return;
            if (_phase == GrowthPhase.Empty || _phase == GrowthPhase.Withered) StartCycle(0);

            double now = FarmingRuntime.Now;
            _plantedAt = now - _definition.TotalGrowTime / _growthSpeed;
            AdvanceTo(now);
        }

        #endregion

        #region Internals

        private double ElapsedGrowth(double now) => (now - _plantedAt) * _growthSpeed;

        /// <summary>Begin a growth cycle at <paramref name="fromStage"/> (0 = fresh, higher = regrow).</summary>
        private void StartCycle(int fromStage)
        {
            int stage = Mathf.Clamp(fromStage, 0, _definition.LastStageIndex);
            double now = FarmingRuntime.Now;

            // Rebase the timestamp so "elapsed" already covers the stages we're skipping.
            _plantedAt = now - _definition.StageStartTime(stage) / _growthSpeed;
            _stageIndex = stage;
            _phase = GrowthPhase.Growing;

            Raise(Planted);
            FarmingEvents.RaisePlanted(this);

            if (stage >= _definition.LastStageIndex) EnterReady(now);
            ScheduleNext();
        }

        /// <summary>Catch the plot up to wherever <paramref name="now"/> says it should be.</summary>
        private void AdvanceTo(double now)
        {
            if (_definition == null || _phase != GrowthPhase.Growing) return;

            int target = _definition.StageAtElapsed(ElapsedGrowth(now));
            int last = _definition.LastStageIndex;

            while (_stageIndex < target)
            {
                _stageIndex++;
                Raise(StageAdvanced, _stageIndex);
                FarmingEvents.RaiseStageAdvanced(this, _stageIndex);

                if (_stageIndex >= last)
                {
                    EnterReady(now);
                    return;
                }
            }
        }

        private void EnterReady(double now)
        {
            _phase = GrowthPhase.Ready;
            _stageIndex = _definition.LastStageIndex;

            // Exact moment of ripening, not the moment we noticed — keeps wither timing honest.
            _readyAt = _plantedAt + _definition.TotalGrowTime / _growthSpeed;
            if (_readyAt > now) _readyAt = now;

            GrowableRegistry.SetReady(this, true);
            Raise(Ready);
            FarmingEvents.RaiseReady(this);
        }

        private void Wither()
        {
            _phase = GrowthPhase.Withered;
            GrowableRegistry.SetReady(this, false);

            Raise(Withered);
            FarmingEvents.RaiseWithered(this);

            ClearInternal();
        }

        private void ClearInternal()
        {
            bool remove = _definition != null && _definition.RemoveWhenEmpty;

            _phase = GrowthPhase.Empty;
            _stageIndex = 0;
            CancelWakeUp();

            Raise(Cleared);
            FarmingEvents.RaiseCleared(this);

            // Иначе на поле копятся невидимые пустые грядки: они остаются в реестре, носят
            // плашку уровня и перехватывают клики, хотя для игрока их уже нет.
            if (remove && Application.isPlaying) Destroy(gameObject);
        }

        /// <summary>Ask the scheduler to wake us at the next moment something actually changes.</summary>
        private void ScheduleNext()
        {
            var scheduler = GrowthScheduler.Instance;
            if (scheduler == null || _handle == GrowthScheduler.InvalidHandle) return;

            if (_phase == GrowthPhase.Growing && _definition != null)
            {
                double boundary = _definition.StageStartTime(_stageIndex + 1);
                scheduler.Schedule(_handle, _plantedAt + boundary / _growthSpeed);
                return;
            }

            if (_phase == GrowthPhase.Ready && _definition != null && _definition.WitherAfter > 0f)
            {
                scheduler.Schedule(_handle, _readyAt + _definition.WitherAfter);
                return;
            }

            scheduler.Cancel(_handle);
        }

        private void CancelWakeUp()
        {
            var scheduler = GrowthScheduler.Instance;
            if (scheduler != null && _handle != GrowthScheduler.InvalidHandle) scheduler.Cancel(_handle);
        }

        void IGrowthScheduled.OnScheduledDue(double now)
        {
            switch (_phase)
            {
                case GrowthPhase.Growing:
                    AdvanceTo(now);
                    ScheduleNext();
                    break;

                case GrowthPhase.Ready:
                    if (_definition != null && _definition.WitherAfter > 0f) Wither();
                    break;
            }
        }

        // Subscriber exceptions must not break the plot that raised the event.
        private void Raise(Action<Growable> handler)
        {
            if (handler == null) return;
            try { handler(this); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void Raise(Action<Growable, int> handler, int arg)
        {
            if (handler == null) return;
            try { handler(this, arg); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void Raise(Action<Growable, Growable> handler, Growable arg)
        {
            if (handler == null) return;
            try { handler(this, arg); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void Raise(Action<Growable, HarvestResult> handler, in HarvestResult arg)
        {
            if (handler == null) return;
            try { handler(this, arg); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        #endregion
    }
}

using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// The production half of a building: pulls raw resources out of world storage and puts refined
    /// ones back. Sits next to a <see cref="Building"/> and reads its level, so a workshop is
    /// upgraded by the same ladder as everything else instead of growing a second progression.
    /// <para>
    /// Runs on <see cref="GrowthScheduler"/>, not Update. A farm ends up with a lot of these and
    /// each one only needs waking when its batch is due — same reasoning as growables.
    /// </para>
    /// <para>
    /// Inputs are taken at the start of a batch, not the end, so the player sees the cost the moment
    /// work begins. What is in the machine is refunded if the workshop is switched off mid-batch —
    /// resources must never quietly evaporate.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Building))]
    [AddComponentMenu("Farm/Workshop")]
    public sealed class Workshop : MonoBehaviour, IGrowthScheduled
    {
        /// <summary>Seconds before looking for work again when nothing could be started.</summary>
        private const double IdleRetry = 2.0;

        private Building _building;
        private int _handle = GrowthScheduler.InvalidHandle;

        private WorkshopRecipe _running;
        private double _startedAt;
        private double _readyAt;

        /// <summary>A batch came out: the recipe and how many units were actually stored.</summary>
        public event Action<Workshop, WorkshopRecipe, int> Produced;

        /// <summary>Work started or stopped. Second argument is the recipe, null when idle.</summary>
        public event Action<Workshop, WorkshopRecipe> WorkChanged;

        public Building Building => _building != null ? _building : (_building = GetComponent<Building>());
        public WorkshopRecipe Running => _running;
        public bool IsWorking => _running != null;

        /// <summary>How far the current batch has come, 0..1. Zero when idle.</summary>
        public float Progress01
        {
            get
            {
                if (_running == null) return 0f;
                double span = _readyAt - _startedAt;
                if (span <= 0.0) return 1f;
                return Mathf.Clamp01((float)((FarmingRuntime.Now - _startedAt) / span));
            }
        }

        /// <summary>Seconds one batch of <paramref name="recipe"/> takes at the current level.</summary>
        public double BatchSeconds(WorkshopRecipe recipe)
        {
            if (recipe == null) return 0.0;
            float speed = Mathf.Max(0.01f, Building != null ? Building.Output : 1f);
            return Mathf.Max(0.1f, recipe.Seconds) / speed;
        }

        private void OnEnable()
        {
            _building = GetComponent<Building>();

            var scheduler = GrowthScheduler.Instance;
            if (scheduler == null) return;   // не в Play Mode — работать нечему

            _handle = scheduler.Register(this);
            scheduler.Schedule(_handle, FarmingRuntime.Now);
        }

        private void OnDisable()
        {
            RefundRunning();

            var scheduler = GrowthScheduler.Existing;
            if (scheduler != null) scheduler.Unregister(_handle);
            _handle = GrowthScheduler.InvalidHandle;
        }

        void IGrowthScheduled.OnScheduledDue(double now)
        {
            if (_running != null) Finish(now);
            if (!TryStart(now)) Sleep(now + IdleRetry);
        }

        // ---- цикл ----

        private void Finish(double now)
        {
            var recipe = _running;
            _running = null;

            var storage = FarmingRuntime.Sink as IInventory;
            int stored = storage != null ? storage.TryAdd(recipe.Output, recipe.OutputAmount) : 0;

            // Склад может отказать (когда появится лимит) — тогда партия пропадает, но
            // молча этого делать нельзя.
            if (stored < recipe.OutputAmount)
                Debug.LogWarning("[Workshop] " + name + ": склад не принял " +
                                 (recipe.OutputAmount - stored) + " " + recipe.Output.DisplayName, this);

            Raise(Produced, recipe, stored);
            Raise(WorkChanged, null);
        }

        private bool TryStart(double now)
        {
            var recipe = PickRecipe();
            if (recipe == null) return false;

            var storage = FarmingRuntime.Sink as IInventory;
            if (storage == null) return false;

            int taken = storage.TryRemove(recipe.Input, recipe.InputAmount);
            if (taken < recipe.InputAmount)
            {
                // Кто-то успел забрать сырьё между проверкой и списанием — вернуть и подождать.
                if (taken > 0) storage.TryAdd(recipe.Input, taken);
                return false;
            }

            _running = recipe;
            _startedAt = now;
            _readyAt = now + BatchSeconds(recipe);

            Sleep(_readyAt);
            Raise(WorkChanged, recipe);
            return true;
        }

        /// <summary>
        /// Best batch that can be started right now: the most valuable output the level allows and
        /// the store can pay for. Value rather than order, so unlocking a better recipe upgrades the
        /// workshop's behaviour without anyone reordering the list.
        /// </summary>
        private WorkshopRecipe PickRecipe()
        {
            var definition = Building != null ? Building.Definition : null;
            if (definition == null) return null;

            var recipes = definition.Recipes;
            if (recipes == null) return null;

            var storage = FarmingRuntime.Sink as IInventory;
            if (storage == null) return null;

            int level = Building.Level;
            WorkshopRecipe best = null;
            int bestValue = 0;

            for (int i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i];
                if (recipe == null || !recipe.IsValid) continue;
                if (level < recipe.UnlockLevel) continue;
                if (storage.GetAmount(recipe.Input) < recipe.InputAmount) continue;

                int value = recipe.OutputValue;
                if (best != null && value <= bestValue) continue;

                best = recipe;
                bestValue = value;
            }

            return best;
        }

        private void RefundRunning()
        {
            if (_running == null) return;

            var storage = FarmingRuntime.Sink as IInventory;
            if (storage != null) storage.TryAdd(_running.Input, _running.InputAmount);

            _running = null;
            Raise(WorkChanged, null);
        }

        private void Sleep(double until)
        {
            var scheduler = GrowthScheduler.Existing;
            if (scheduler != null) scheduler.Schedule(_handle, until);
        }

        private void Raise(Action<Workshop, WorkshopRecipe> handler, WorkshopRecipe recipe)
        {
            if (handler == null) return;
            try { handler(this, recipe); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void Raise(Action<Workshop, WorkshopRecipe, int> handler, WorkshopRecipe recipe, int amount)
        {
            if (handler == null) return;
            try { handler(this, recipe, amount); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }
}

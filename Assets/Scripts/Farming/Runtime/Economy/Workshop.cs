using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Производственная половина постройки: забирает сырьё со склада мира и кладёт обратно
    /// переработанное. Стоит рядом с <see cref="Building"/> и читает его уровень, поэтому
    /// мастерская качается той же лестницей, что и всё остальное, а не растит вторую прогрессию.
    /// <para>
    /// Работает на <see cref="GrowthScheduler"/>, не в Update. Мастерских на ферме набирается
    /// много, и каждую нужно будить только к готовности партии — то же рассуждение, что у грядок.
    /// </para>
    /// <para>
    /// Сырьё списывается в начале партии, а не в конце, — игрок видит цену в момент старта
    /// работы. Что лежит «в станке», возвращается, если мастерскую выключили посреди партии:
    /// ресурсы не имеют права тихо испаряться.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Building))]
    [AddComponentMenu("Farm/Workshop")]
    public sealed class Workshop : MonoBehaviour, IGrowthScheduled
    {
        /// <summary>Через сколько секунд снова искать работу, когда ничего не удалось начать.</summary>
        private const double IdleRetry = 2.0;

        private Building _building;
        private int _handle = GrowthScheduler.InvalidHandle;

        private WorkshopRecipe _running;
        private double _startedAt;
        private double _readyAt;

        // Мастеровой рядом (этап 2 колонии). Метка со сроком годности, как у застолбления
        // уборки: житель продлевает её каждый тик у станка, брошенная протухает сама — у
        // ухода полдюжины путей, и ловить каждый значило бы утекать. Кто именно стоит,
        // станок не знает: сборка Farm.Farming не видит жителей, ей хватает множителя.
        private float _tendSpeed = 1f;
        private double _tendUntil;

        /// <summary>Партия вышла: рецепт и сколько единиц реально легло на склад.</summary>
        public event Action<Workshop, WorkshopRecipe, int> Produced;

        /// <summary>Работа началась или остановилась. Второй аргумент — рецепт, null при простое.</summary>
        public event Action<Workshop, WorkshopRecipe> WorkChanged;

        public Building Building => _building != null ? _building : (_building = GetComponent<Building>());
        public WorkshopRecipe Running => _running;
        public bool IsWorking => _running != null;

        /// <summary>Насколько продвинулась текущая партия, 0..1. Ноль при простое.</summary>
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

        /// <summary>Секунды на одну партию <paramref name="recipe"/> при текущем уровне.</summary>
        public double BatchSeconds(WorkshopRecipe recipe)
        {
            if (recipe == null) return 0.0;
            float speed = Mathf.Max(0.01f, Building != null ? Building.Output : 1f);
            return Mathf.Max(0.1f, recipe.Seconds) / speed;
        }

        /// <summary>
        /// Отметить мастерового у станка: множитель скорости партий на ближайшие
        /// <paramref name="holdSeconds"/>. Продлевается каждым тиком присутствия.
        /// </summary>
        public void SetTendSpeed(float factor, double holdSeconds = 2.0)
        {
            _tendSpeed = Mathf.Max(1f, factor);
            _tendUntil = FarmingRuntime.Now + holdSeconds;
        }

        /// <summary>Текущий множитель мастерового. Единица, когда у станка никого.</summary>
        public float TendSpeed => FarmingRuntime.Now < _tendUntil ? _tendSpeed : 1f;

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

            // Полный склад откажет — тогда партия пропадает, но молча этого делать нельзя.
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

            // Буст мастерового фиксируется на старте партии и не пересчитывается в
            // середине: часам реального времени нельзя врать задним числом, а партии
            // короткие — пришедший к станку увидит эффект со следующей же.
            _readyAt = now + BatchSeconds(recipe) / TendSpeed;

            Sleep(_readyAt);
            Raise(WorkChanged, recipe);
            return true;
        }

        /// <summary>
        /// Лучшая партия, которую можно начать прямо сейчас: самый ценный выход из доступных
        /// уровню и посильных складу. По ценности, а не по порядку — открытие рецепта получше
        /// улучшает поведение мастерской само, без перестановки списка.
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

        // ---- сохранение ----

        /// <summary>
        /// Снимок недоделанной партии. Наработанные секунды, а не таймстамп, — как у грядок:
        /// оффлайн-догон добавит вызывающий при чтении. False — мастерская простаивает.
        /// </summary>
        public bool CaptureRunning(out string inputId, out string outputId, out double elapsed)
        {
            inputId = null;
            outputId = null;
            elapsed = 0.0;

            if (_running == null || _running.Input == null || _running.Output == null) return false;

            inputId = _running.Input.Id;
            outputId = _running.Output.Id;
            elapsed = System.Math.Max(0.0, FarmingRuntime.Now - _startedAt);
            return true;
        }

        /// <summary>
        /// Вернуть партию из сохранения. Сырьё НЕ списывается — оно уже списано в момент
        /// старта и в снимке склада его нет; списать второй раз значило бы брать двойную цену.
        /// Перезревшая за отлучку партия дойдёт сама: планировщик разбудит немедленно.
        /// </summary>
        public void RestoreRunning(string inputId, string outputId, double elapsed)
        {
            var definition = Building != null ? Building.Definition : null;
            var recipes = definition != null ? definition.Recipes : null;
            if (recipes == null) return;

            WorkshopRecipe found = null;
            for (int i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i];
                if (recipe == null || recipe.Input == null || recipe.Output == null) continue;
                if (recipe.Input.Id == inputId && recipe.Output.Id == outputId) { found = recipe; break; }
            }

            // Рецепт исчез из ассета — честно сказать и отпустить: сырьё этой партии
            // потеряно вместе с рецептом, молча проглотить это нельзя.
            if (found == null)
            {
                Debug.LogWarning("[Workshop] " + name + ": рецепт " + inputId + "→" + outputId +
                                 " из сейва не найден — партия пропала", this);
                return;
            }

            double now = FarmingRuntime.Now;
            _running = found;
            _startedAt = now - System.Math.Max(0.0, elapsed);
            _readyAt = _startedAt + BatchSeconds(found);

            Sleep(System.Math.Max(now, _readyAt));
            Raise(WorkChanged, found);
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

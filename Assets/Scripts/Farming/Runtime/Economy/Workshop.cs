using System;
using System.Collections.Generic;
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
            int stored = storage != null
                ? storage.TryAdd(recipe.Output, recipe.OutputAmount, _runningGrade)
                : 0;

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

            // Списываем строку за строкой и откатываем всё разом при первой же недостаче:
            // у составного рецепта частичное списание оставило бы игрока без муки и без
            // хлеба — цена взята, товара нет.
            int lines = recipe.InputCount;
            var taken = _takenGrades;
            taken.Clear();

            for (int i = 0; i < lines; i++)
            {
                var resource = recipe.InputResourceAt(i);
                int need = recipe.InputAmountAt(i);

                if (TakeLine(storage, resource, need, taken)) continue;

                Refund(storage, taken);
                return false;
            }

            // Сорт партии — худший среди взятого: цепочка не бывает лучше слабого звена,
            // и отборная доска из отборного клёна с обычным клеем была бы обманом ожидания.
            _runningGrade = ResourceGrade.Prime;
            for (int i = 0; i < taken.Count; i++)
                if (taken[i].Grade < _runningGrade) _runningGrade = taken[i].Grade;
            if (taken.Count == 0) _runningGrade = ResourceGrade.Common;

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
                if (!HasAllInputs(storage, recipe)) continue;

                int value = recipe.OutputValue;
                if (best != null && value <= bestValue) continue;

                best = recipe;
                bestValue = value;
            }

            return best;
        }

        /// <summary>Что уже взято под текущую партию — для отката и для сорта выхода.</summary>
        private readonly List<InventoryEntry> _takenGrades = new List<InventoryEntry>(4);

        /// <summary>Сорт идущей партии: он же станет сортом выхода.</summary>
        private ResourceGrade _runningGrade = ResourceGrade.Common;

        /// <summary>
        /// Взять строку сырья, начиная с ЛУЧШЕГО сорта. Именно лучшее, а не дешёвое:
        /// игрок, поливший клён девять циклов, ждёт отборную доску, и станок, аккуратно
        /// обошедший его отборное сырьё ради обычного, читался бы как поломка. Ценность
        /// при этом не теряется — маржа станка переносит надбавку в выход.
        /// </summary>
        private static bool TakeLine(IInventory storage, ResourceDefinition resource, int need,
                                     List<InventoryEntry> taken)
        {
            var order = ResourceGrades.All;
            int left = need;

            for (int g = order.Length - 1; g >= 0 && left > 0; g--)
            {
                int got = storage.TryRemove(resource, left, order[g]);
                if (got <= 0) continue;

                taken.Add(new InventoryEntry(resource, got, order[g]));
                left -= got;
            }

            return left <= 0;
        }

        /// <summary>Вернуть на склад всё, что успели взять, — каждую стопку своим сортом.</summary>
        private static void Refund(IInventory storage, List<InventoryEntry> taken)
        {
            for (int i = 0; i < taken.Count; i++)
                storage.TryAdd(taken[i].Resource, taken[i].Amount, taken[i].Grade);

            taken.Clear();
        }

        /// <summary>Лежит ли на складе всё сырьё рецепта разом.</summary>
        private static bool HasAllInputs(IInventory storage, WorkshopRecipe recipe)
        {
            int lines = recipe.InputCount;
            for (int i = 0; i < lines; i++)
                if (storage.GetAmount(recipe.InputResourceAt(i)) < recipe.InputAmountAt(i)) return false;

            return true;
        }

        /// <summary>
        /// Чего не хватает станку, чтобы взяться за самый ценный доступный уровню рецепт, —
        /// или null, когда дело только в пустом складе целиком. Существует ради правила
        /// заметности: составной рецепт стоит молча ровно так же, как простой, но причин
        /// у простоя стало больше одной, и «сырья бы» перестало быть ответом.
        /// </summary>
        public ResourceDefinition MissingInput()
        {
            var definition = Building != null ? Building.Definition : null;
            var recipes = definition != null ? definition.Recipes : null;
            var storage = FarmingRuntime.Sink as IInventory;
            if (recipes == null || storage == null) return null;

            int level = Building.Level;

            ResourceDefinition missing = null;
            int bestValue = 0;

            for (int i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i];
                if (recipe == null || !recipe.IsValid || level < recipe.UnlockLevel) continue;

                // Интересен самый ценный рецепт, которому не хватает ОДНОЙ строки: он и есть
                // «почти можем». Рецепт, где нет ничего, назвать нечем — это просто пустой склад.
                ResourceDefinition lacking = null;
                int lines = recipe.InputCount;
                bool single = true;

                for (int line = 0; line < lines; line++)
                {
                    var resource = recipe.InputResourceAt(line);
                    if (storage.GetAmount(resource) >= recipe.InputAmountAt(line)) continue;

                    if (lacking != null) { single = false; break; }
                    lacking = resource;
                }

                if (!single || lacking == null) continue;
                if (recipe.OutputValue <= bestValue) continue;

                bestValue = recipe.OutputValue;
                missing = lacking;
            }

            return missing;
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

            if (_running == null || !_running.IsValid) return false;

            // Формат снимка не тронут ради составных рецептов: в inputId уезжает ПЕРВАЯ
            // строка сырья, а узнаётся партия по выходу — он у станка уникален. Менять
            // формат ради второго ингредиента значило бы сломать чужие партии в сейвах
            // ради поля, которое и так не решает.
            inputId = _running.InputResourceAt(0) != null ? _running.InputResourceAt(0).Id : null;
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

            // Ищем по выходу — он у станка уникален, — а первой строкой сырья лишь уточняем
            // при совпадении выходов. Так партия переживает и превращение простого рецепта
            // в составной: сырьё уже списано, и терять её из-за нового ингредиента нельзя.
            WorkshopRecipe found = null;
            for (int i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i];
                if (recipe == null || !recipe.IsValid || recipe.Output.Id != outputId) continue;

                found = recipe;

                var first = recipe.InputResourceAt(0);
                if (first != null && first.Id == inputId) break;
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

            // Возвращаем ровно те стопки, что взяли, — со своими сортами. Список пуст,
            // если партия пришла из сейва: там сортов взятого нет, и вернуть их неоткуда
            // (сырьё в снимке уже списано). Тогда возвращаем обычным — потеря надбавки
            // за уход честнее исчезновения сырья.
            var storage = FarmingRuntime.Sink as IInventory;
            if (storage != null)
            {
                if (_takenGrades.Count > 0)
                {
                    Refund(storage, _takenGrades);
                }
                else
                {
                    int lines = _running.InputCount;
                    for (int i = 0; i < lines; i++)
                        storage.TryAdd(_running.InputResourceAt(i), _running.InputAmountAt(i));
                }
            }

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

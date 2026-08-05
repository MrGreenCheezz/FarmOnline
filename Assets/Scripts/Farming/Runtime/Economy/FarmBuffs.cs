using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Одно окно, через которое вся игра спрашивает «насколько это место сейчас усилено».
    /// <para>
    /// Именно место: усилители действуют в радиусе (<see cref="BuildingDefinition.AuraRadiusAt"/>),
    /// и постройка — это решение «куда», а не строчка в списке покупок. Ветряк у дальних жил
    /// и ветряк у домашних грядок — разные фермы. Радиус 0 — эффект на всю ферму: так живёт
    /// то, что глобально по природе (ячейки склада).
    /// </para>
    /// <para>
    /// В одной точке из одинаковых аур берётся <b>лучшая</b>, а не сумма — то же правило,
    /// что у рынков. Зато две ауры в разных углах фермы честно кроют каждая свой угол:
    /// у второго экземпляра теперь есть смысл, и это смысл размещения, а не сложения.
    /// </para>
    /// </summary>
    public static class FarmBuffs
    {
        /// <summary>Ниже этой доли нужды не замедляются, сколько усилителей ни ставь.</summary>
        private const float MinNeedsDrain = 0.25f;

        /// <summary>
        /// Надбавки пересчитались. Кому мало опроса по требованию — подписывайся:
        /// склад и грядки должны узнать сразу, а не в момент следующего сбора.
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// Лучшая надбавка данного вида, достающая до точки. Категория — чьей ауры ищем:
        /// null для аур, которые касаются фермера, а не растимого.
        /// </summary>
        public static float BonusAt(FarmBoost boost, Vector3 position, ResourceCategory? category = null)
        {
            if (boost == FarmBoost.None) return 0f;

            float best = 0f;
            var all = BuildingRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Definition == null) continue;
                if (b.Definition.Boost != boost) continue;
                if (category.HasValue && !b.Definition.AuraCovers(category.Value)) continue;

                float radius = b.Definition.AuraRadiusAt(b.Level);
                if (radius > 0f)
                {
                    Vector3 offset = b.transform.position - position;
                    offset.y = 0f;   // аура — круг на земле, высота рельефа её не рвёт
                    if (offset.sqrMagnitude > radius * radius) continue;
                }

                if (b.Output > best) best = b.Output;
            }

            return best;
        }

        /// <summary>Во сколько раз быстрее растёт растимое этой категории в этой точке.</summary>
        public static float GrowthSpeedAt(Vector3 position, ResourceCategory category) =>
            1f + BonusAt(FarmBoost.GrowthSpeed, position, category);

        /// <summary>Во сколько раз больше даёт сбор с этой грядки.</summary>
        public static float HarvestYieldAt(Vector3 position, ResourceCategory category) =>
            1f + BonusAt(FarmBoost.HarvestYield, position, category);

        /// <summary>Во сколько раз быстрее ходит фермер, стоящий в этой точке.</summary>
        public static float MoveSpeedAt(Vector3 position) =>
            1f + BonusAt(FarmBoost.MoveSpeed, position);

        /// <summary>Множитель расхода нужд фермера в этой точке. Ноль недостижим намеренно.</summary>
        public static float NeedsDrainAt(Vector3 position) =>
            Mathf.Max(MinNeedsDrain, 1f - BonusAt(FarmBoost.Vigor, position));

        /// <summary>Защищено ли спелое в этой точке от увядания.</summary>
        public static bool WitherGuardedAt(Vector3 position, ResourceCategory category) =>
            BonusAt(FarmBoost.WitherGuard, position, category) > 0f;

        /// <summary>
        /// Сколько ячеек добавлено складу. Глобален по природе: ячейки лежат на складе,
        /// а не разбросаны по полю, поэтому радиус силоса — 0 и позиция не спрашивается.
        /// </summary>
        public static int StorageSlots => Mathf.Max(0, Mathf.RoundToInt(GlobalBonus(FarmBoost.StorageSlots)));

        /// <summary>Лучшая надбавка вида без оглядки на позицию — для глобальных по природе эффектов.</summary>
        private static float GlobalBonus(FarmBoost boost)
        {
            float best = 0f;
            var all = BuildingRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Definition == null) continue;
                if (b.Definition.Boost != boost) continue;
                if (b.Output > best) best = b.Output;
            }
            return best;
        }

        /// <summary>Человеческая подпись эффекта для UI и заметок уровней.</summary>
        public static string Describe(FarmBoost boost, float amount)
        {
            switch (boost)
            {
                case FarmBoost.GrowthSpeed: return "+" + Percent(amount) + "% к скорости роста";
                case FarmBoost.HarvestYield: return "+" + Percent(amount) + "% к урожаю";
                case FarmBoost.MoveSpeed: return "+" + Percent(amount) + "% к скорости фермера";
                case FarmBoost.StorageSlots: return "+" + Mathf.RoundToInt(amount) + " ячеек склада";
                case FarmBoost.Vigor: return "нужды садятся на " + Percent(amount) + "% медленнее";
                case FarmBoost.WitherGuard: return "спелое не портится";
                default: return "";
            }
        }

        private static int Percent(float fraction) => Mathf.RoundToInt(fraction * 100f);

        // ---- рассылка ----

        /// <summary>
        /// Пересчитать и разослать. Зовётся при любом изменении построек и после каждого
        /// отпускания ноши: перетащенная грядка могла войти в ауру или выйти из неё —
        /// как и перетащенный ветряк мог уехать от всех своих грядок.
        /// </summary>
        public static void Refresh()
        {
            ApplyToPlots();

            var handler = Changed;
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// Разложить скорость роста по грядкам — каждой по её месту и категории. Толкаем,
        /// а не опрашиваем: у грядки нет покадрового тика — она живёт на таймстампе посадки,
        /// и подмешать множитель при чтении нельзя, не сломав накопленный прогресс. Сеттер
        /// <see cref="Growable.GrowthSpeed"/> умеет менять скорость с сохранением прогресса.
        /// </summary>
        private static void ApplyToPlots()
        {
            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                if (plot == null) continue;
                plot.ApplyGrowthBuff(GrowthSpeedAt(plot.transform.position, plot.Category));
            }
        }

        private static void OnDragChanged(Transform dragged)
        {
            // Интересен момент отпускания: пока несут, позиции переходные.
            if (dragged == null) Refresh();
        }

        /// <summary>
        /// BeforeSceneLoad, а не SubsystemRegistration: на той же ступени реестры обнуляют
        /// свои события, а порядок вызовов внутри ступени не определён — подписка могла бы
        /// быть стёрта сразу после установки.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            Changed = null;

            BuildingRegistry.Changed -= Refresh;
            BuildingRegistry.Changed += Refresh;

            DragFocus.Changed -= OnDragChanged;
            DragFocus.Changed += OnDragChanged;
        }
    }
}

using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Опыт и уровень игрока — вторая ось прогрессии поверх золота.
    /// <para>
    /// Золото перестаёт быть целью, как только его хватает на всё; опыт же не тратится
    /// и потому не кончается как цель. Уровень открывает ступени в магазине и слоты заказов —
    /// то есть отвечает не «что я могу купить», а «до чего я дорос».
    /// </para>
    /// <para>
    /// Опыт дают действия, а не время: сбор, слияние, заказ, уход. Ферма, оставленная
    /// расти сама по себе, опыт не копит — иначе уровень мерил бы календарь, а не игрока.
    /// </para>
    /// </summary>
    public static class FarmExperience
    {
        /// <summary>За сданный заказ. Крупно: заказ — самое осмысленное действие дня.</summary>
        public const int PerOrder = 20;

        /// <summary>За слияние. Пульс главной механики.</summary>
        public const int PerMerge = 4;

        /// <summary>
        /// За переход ступени слиянием — умноженное на НОВУЮ ступень. Событие на порядки
        /// реже рядового слияния (сорок вложенных грядок), и опыт обязан это признавать.
        /// </summary>
        public const int PerAscendPerTier = 10;

        /// <summary>За полив или подкормку.</summary>
        public const int PerCare = 1;

        /// <summary>Потолок уровня. Не столько дизайн, сколько страховка кривой от переполнения.</summary>
        public const int MaxLevel = 60;

        public static int TotalXp { get; private set; }

        public static int Level { get; private set; } = 1;

        /// <summary>Опыт, набранный внутри текущего уровня.</summary>
        public static int IntoLevel { get; private set; }

        /// <summary>Сколько опыта стоит текущий уровень целиком.</summary>
        public static int LevelCost { get; private set; } = CostOf(1);

        /// <summary>Опыт пришёл — HUD перерисовывает полосу.</summary>
        public static event Action Changed;

        /// <summary>Уровень взят. Аргумент — новый уровень; по нему звучит фанфара и пишется строка.</summary>
        public static event Action<int> LevelUp;

        /// <summary>
        /// Сколько стоит уровень <paramref name="level"/> — от него к следующему.
        /// <para>
        /// Степень 1.5 — медленнее квадрата: уровни дорожают ощутимо, но поздние остаются
        /// достижимыми неделями, а не годами. Первый стоит 40 — берётся за одну сессию
        /// новичка, и лестница успевает объяснить себя до того, как попросит терпения.
        /// </para>
        /// </summary>
        public static int CostOf(int level) =>
            Mathf.Max(10, Mathf.RoundToInt(40f * Mathf.Pow(Mathf.Max(1, level), 1.5f) / 10f) * 10);

        /// <summary>
        /// Какой уровень игрока нужен ступени <paramref name="tier"/> в магазине.
        /// <para>
        /// Два уровня на ступень: игрок, живущий заказами и уходом, обгоняет эту лестницу
        /// золотом, и ворота почти не видны; заметны они лишь тому, кто пытается перескочить
        /// прогрессию кошельком — ради этого и стоят.
        /// </para>
        /// </summary>
        public static int LevelForTier(int tier) => Mathf.Max(1, (Mathf.Max(1, tier) - 1) * 2);

        /// <summary>Начислить опыт. Отрицательное молча отбрасывается: опыт не тратится.</summary>
        public static void Add(int amount)
        {
            if (amount <= 0) return;

            TotalXp += amount;
            Recount(raiseLevelUp: true);
            Raise(Changed);
        }

        /// <summary>Вернуть из сохранения. Тихо: залп фанфар за старые уровни никому не нужен.</summary>
        public static void Restore(int totalXp)
        {
            TotalXp = Mathf.Max(0, totalXp);
            Recount(raiseLevelUp: false);
            Raise(Changed);
        }

        /// <summary>
        /// Досчитать уровень по суммарному опыту. Уровень — производная от опыта, а не второе
        /// сохраняемое число: два числа, обязанные сходиться, однажды разойдутся.
        /// </summary>
        private static void Recount(bool raiseLevelUp)
        {
            int level = 1;
            int rest = TotalXp;

            while (level < MaxLevel && rest >= CostOf(level))
            {
                rest -= CostOf(level);
                level++;
            }

            int before = Level;
            Level = level;
            IntoLevel = rest;
            LevelCost = CostOf(level);

            if (!raiseLevelUp || level <= before) return;

            var handler = LevelUp;
            if (handler == null) return;
            try { handler(level); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void OnHarvested(Growable plot, HarvestResult result)
        {
            // Ступень в цене: корзина редиски и корзина адаманта — разный труд.
            int tier = result.Resource != null ? result.Resource.Tier : 1;
            Add(1 + tier);
        }

        private static void OnMerged(Growable survivor, Growable absorbed) => Add(PerMerge);

        private static void OnTierAscended(Growable survivor, GrowableDefinition from)
        {
            int tier = survivor != null && survivor.Definition != null && survivor.Definition.YieldResource != null
                ? survivor.Definition.YieldResource.Tier
                : 1;
            Add(PerAscendPerTier * Mathf.Max(1, tier));
        }

        private static void OnCared(Growable plot) => Add(PerCare);

        private static void Raise(Action handler)
        {
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// BeforeSceneLoad, а не SubsystemRegistration: на той же ступени FarmingEvents
        /// обнуляет подписчиков, и порядок внутри ступени не определён (как у FarmProgress).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            TotalXp = 0;
            Level = 1;
            IntoLevel = 0;
            LevelCost = CostOf(1);
            Changed = null;
            LevelUp = null;

            FarmingEvents.Harvested -= OnHarvested;
            FarmingEvents.Harvested += OnHarvested;
            FarmingEvents.Merged -= OnMerged;
            FarmingEvents.Merged += OnMerged;
            FarmingEvents.TierAscended -= OnTierAscended;
            FarmingEvents.TierAscended += OnTierAscended;

            FarmWater.Poured -= OnCared;
            FarmWater.Poured += OnCared;
            FarmFertilizer.Applied -= OnCared;
            FarmFertilizer.Applied += OnCared;
            // Корм — такой же уход, как полив и подкормка: ждать его столько же, а опыта
            // не давать значило бы сказать «уход за скотиной не считается».
            FarmFeed.Fed -= OnCared;
            FarmFeed.Fed += OnCared;
        }
    }
}

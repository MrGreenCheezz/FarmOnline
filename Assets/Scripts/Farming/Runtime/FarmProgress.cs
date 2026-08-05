using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Сколько ферма прожила и наработала — общефермовые счётчики для всего, что открывается
    /// «со временем»: замыслов жителей, будущих гостей и прочих ступеней взросления фермы.
    /// <para>
    /// Счётчики нарочно общие, а не чьи-то личные: «ферма достаточно взрослая для скамейки» —
    /// свойство фермы, а не биография конкретного работника. Появятся новые жители — они
    /// разделят этот же прогресс, а не начнут с нуля.
    /// </para>
    /// </summary>
    public static class FarmProgress
    {
        /// <summary>Сколько единиц урожая ферма собрала за всю партию — кем угодно и чем угодно.</summary>
        public static int TotalHarvested { get; private set; }

        /// <summary>Сколько слияний сделал игрок. Пульс главной механики.</summary>
        public static int TotalMerges { get; private set; }

        /// <summary>Номер текущего дня, с единицы. Без часов в сцене — первый день.</summary>
        public static int Day => DayNightCycle.Instance != null ? DayNightCycle.Instance.Day : 1;

        /// <summary>Вернуть прожитое из сохранения. День хранят часы — он приходит вместе с ними.</summary>
        public static void RestoreState(int totalHarvested, int totalMerges)
        {
            TotalHarvested = Mathf.Max(0, totalHarvested);
            TotalMerges = Mathf.Max(0, totalMerges);
        }

        private static void OnHarvested(Growable plot, HarvestResult result) =>
            TotalHarvested += Mathf.Max(0, result.Amount);

        private static void OnMerged(Growable survivor, Growable absorbed) => TotalMerges++;

        /// <summary>
        /// BeforeSceneLoad, а не SubsystemRegistration: на той же ступени FarmingEvents
        /// обнуляет подписчиков, и порядок внутри ступени не определён.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            TotalHarvested = 0;
            TotalMerges = 0;

            FarmingEvents.Harvested -= OnHarvested;
            FarmingEvents.Harvested += OnHarvested;
            FarmingEvents.Merged -= OnMerged;
            FarmingEvents.Merged += OnMerged;
        }
    }
}

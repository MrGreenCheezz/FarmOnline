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

        // ---- счётчики онлайна (этап 8): социальная половина игры не имела ни одной вехи ----

        /// <summary>Максимум друзей, которое игрок видел в своём списке.</summary>
        public static int FriendsSeen { get; private set; }

        /// <summary>Сколько чужих грядок игрок собрал в гостях за всё время.</summary>
        public static int HelpGiven { get; private set; }

        /// <summary>Сколько подарков отправлено друзьям.</summary>
        public static int GiftsSent { get; private set; }

        /// <summary>Сколько лотов выставлено на рынок игроков.</summary>
        public static int MarketLots { get; private set; }

        public static void NoteFriendsSeen(int count)
        {
            if (count <= FriendsSeen) return;
            FriendsSeen = count;
            FarmAchievements.Check();
        }

        /// <summary>
        /// Помощь копится дома, не в гостях: гостевая сцена живёт на ЧУЖОМ FarmProgress
        /// (RestoreState чужого снимка), и инкремент там утонул бы при возвращении.
        /// Гость откладывает счёт в PlayerPrefs (GuestMode), дом вливает при загрузке.
        /// </summary>
        public static void AddHelpGiven(int amount)
        {
            if (amount <= 0) return;
            HelpGiven += amount;
            FarmAchievements.Check();
        }

        public static void NoteGiftSent()
        {
            GiftsSent++;
            FarmAchievements.Check();
        }

        public static void NoteMarketLot()
        {
            MarketLots++;
            FarmAchievements.Check();
        }

        /// <summary>Номер текущего дня, с единицы. Без часов в сцене — первый день.</summary>
        public static int Day => DayNightCycle.Instance != null ? DayNightCycle.Instance.Day : 1;

        /// <summary>Вернуть прожитое из сохранения. День хранят часы — он приходит вместе с ними.</summary>
        public static void RestoreState(int totalHarvested, int totalMerges,
                                        int friendsSeen = 0, int helpGiven = 0,
                                        int giftsSent = 0, int marketLots = 0)
        {
            TotalHarvested = Mathf.Max(0, totalHarvested);
            TotalMerges = Mathf.Max(0, totalMerges);
            FriendsSeen = Mathf.Max(0, friendsSeen);
            HelpGiven = Mathf.Max(0, helpGiven);
            GiftsSent = Mathf.Max(0, giftsSent);
            MarketLots = Mathf.Max(0, marketLots);
        }

        private static void OnHarvested(Growable plot, HarvestResult result) =>
            TotalHarvested += Mathf.Max(0, result.Amount);

        private static void OnMerged(Growable survivor, Growable absorbed) => TotalMerges++;

        // Переход ступени — тоже слияние для счётчика: игрок сделал тот же жест,
        // и вехи «сведи N грядок» обязаны его засчитывать.
        private static void OnTierAscended(Growable survivor, GrowableDefinition from) => TotalMerges++;

        /// <summary>
        /// BeforeSceneLoad, а не SubsystemRegistration: на той же ступени FarmingEvents
        /// обнуляет подписчиков, и порядок внутри ступени не определён.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            TotalHarvested = 0;
            TotalMerges = 0;
            FriendsSeen = 0;
            HelpGiven = 0;
            GiftsSent = 0;
            MarketLots = 0;

            FarmingEvents.Harvested -= OnHarvested;
            FarmingEvents.Harvested += OnHarvested;
            FarmingEvents.Merged -= OnMerged;
            FarmingEvents.Merged += OnMerged;
            FarmingEvents.TierAscended -= OnTierAscended;
            FarmingEvents.TierAscended += OnTierAscended;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>За чем следит достижение.</summary>
    public enum AchievementCounter
    {
        Harvested = 0,
        Merges = 1,
        FarmLevel = 2,
        OrdersFilled = 3,
        Gold = 4,

        // Этап 8: у социальной половины игры и у уровня игрока не было ни одной вехи.
        PlayerLevel = 5,
        FriendsSeen = 6,
        HelpGiven = 7,
        GiftsSent = 8,
        MarketLots = 9,
    }

    /// <summary>
    /// Веха фермы: что-то, что игрок и так сделает, названное вслух и оплаченное.
    /// <para>
    /// Достижения нарочно не заводят своих счётчиков и не просят игрока ходить особым путём —
    /// они висят на том, что игра считает и так (<see cref="FarmProgress"/>, уровень фермы,
    /// заказы, кошелёк). Смысл в другом: без них ферма молчит о том, что игрок растёт, и
    /// сотый собранный урожай ничем не отличается от первого.
    /// </para>
    /// </summary>
    public readonly struct Achievement
    {
        public readonly string Id;
        public readonly string Title;
        public readonly AchievementCounter Counter;
        public readonly int Goal;
        public readonly int Gold;

        public Achievement(string id, string title, AchievementCounter counter, int goal, int gold)
        {
            Id = id;
            Title = title;
            Counter = counter;
            Goal = goal;
            Gold = gold;
        }
    }

    /// <summary>
    /// Список вех и выдача наград. Проверяется по событиям фермы, а не в Update: считать пороги
    /// каждый кадр значило бы платить процессором за то, что меняется десять раз за вечер.
    /// </summary>
    public static class FarmAchievements
    {
        /// <summary>
        /// Лестница вех. Пороги подобраны так, чтобы первая давалась в первый же вечер, а
        /// последняя оставалась целью на недели: веха, которую невозможно достать, не мотивирует,
        /// а веха, которую выдают за вход, ничего не значит.
        /// </summary>
        private static readonly Achievement[] All =
        {
            new Achievement("first_harvest",  "Первый урожай",        AchievementCounter.Harvested,     1,     50),
            new Achievement("harvest_100",    "Сто корзин",           AchievementCounter.Harvested,   100,    400),
            new Achievement("harvest_1000",   "Тысяча корзин",        AchievementCounter.Harvested,  1000,   3000),
            new Achievement("harvest_10000",  "Десять тысяч корзин",  AchievementCounter.Harvested, 10000,  25000),

            new Achievement("first_merge",    "Первое слияние",       AchievementCounter.Merges,        1,     50),
            new Achievement("merge_50",       "Полсотни пар",         AchievementCounter.Merges,       50,    600),
            new Achievement("merge_500",      "Мастер слияний",       AchievementCounter.Merges,      500,   6000),

            new Achievement("farm_2",         "Ферма подросла",       AchievementCounter.FarmLevel,     2,    300),
            new Achievement("farm_4",         "Хозяин поля",          AchievementCounter.FarmLevel,     4,   4000),
            new Achievement("farm_6",         "Вся долина",           AchievementCounter.FarmLevel,     6,  40000),
            new Achievement("farm_8",         "Холмы и перелески",    AchievementCounter.FarmLevel,     8, 300000),
            new Achievement("farm_10",        "Земля до горизонта",   AchievementCounter.FarmLevel,    10, 1500000),

            new Achievement("order_1",        "Первый заказ",         AchievementCounter.OrdersFilled,  1,    100),
            new Achievement("order_25",       "Поставщик деревни",    AchievementCounter.OrdersFilled, 25,   2500),
            new Achievement("order_100",      "Кормилец округи",      AchievementCounter.OrdersFilled, 100, 12000),

            // Название обязано сходиться с порогом в той же строке: «первая тысяча» при десяти
            // тысячах — единственное место в интерфейсе, где текст опровергался соседним числом.
            new Achievement("gold_10000",     "Десять тысяч в кубышке", AchievementCounter.Gold,  10000,   1000),

            // Кривая золота продолжается вслед за экономикой: раньше лестница обрывалась
            // на десяти тысячах, когда одна грядка шестой ступени стоила сорок шесть
            // (аудит 06.08.2026). Награда — доли процента цели: веха звучит, не платит.
            new Achievement("gold_100k",      "Сто тысяч в кубышке",  AchievementCounter.Gold,    100000,   2000),
            new Achievement("gold_1m",        "Первый миллион",       AchievementCounter.Gold,   1000000,  15000),
            new Achievement("gold_10m",       "Хозяйство на широкую ногу", AchievementCounter.Gold, 10000000, 120000),
            new Achievement("gold_100m",      "Легенда долины",       AchievementCounter.Gold, 100000000, 800000),

            // Уровень игрока: до этого его не отмечало ничто, а после 12-го он не давал
            // ничего вовсе — теперь на 20-м и 35-м открываются слоты заказов (FarmOrders).
            new Achievement("level_10",       "Десятый уровень",      AchievementCounter.PlayerLevel, 10,   2000),
            new Achievement("level_20",       "Двадцатый уровень",    AchievementCounter.PlayerLevel, 20,  20000),
            new Achievement("level_35",       "Тридцать пятый",       AchievementCounter.PlayerLevel, 35, 100000),

            // Социальная половина игры — друзья, помощь, подарки, рынок — не имела ни
            // одной вехи, хотя это половина того, ради чего ферма онлайн.
            new Achievement("friend_1",       "Первый друг",          AchievementCounter.FriendsSeen, 1,    300),
            new Achievement("help_1",         "Первая помощь другу",  AchievementCounter.HelpGiven,   1,    300),
            new Achievement("help_50",        "Полсотни добрых дел",  AchievementCounter.HelpGiven,  50,   3000),
            new Achievement("gift_10",        "Десять подарков",      AchievementCounter.GiftsSent,  10,   1000),
            new Achievement("market_1",       "Первый лот на рынке",  AchievementCounter.MarketLots,  1,    300),
        };

        private static readonly HashSet<string> Earned = new HashSet<string>();

        /// <summary>Сколько заказов сдано за партию — свой счётчик, его больше никто не ведёт.</summary>
        public static int OrdersFilled { get; private set; }

        /// <summary>Веха взята: показать игроку. Награда уже начислена.</summary>
        public static event Action<Achievement> Earned_;

        public static IReadOnlyList<Achievement> List => All;

        public static bool Has(string id) => Earned.Contains(id);
        public static int EarnedCount => Earned.Count;

        /// <summary>Насколько игрок продвинулся к вехе — для полосы прогресса.</summary>
        public static int Progress(in Achievement achievement)
        {
            switch (achievement.Counter)
            {
                case AchievementCounter.Harvested: return FarmProgress.TotalHarvested;
                case AchievementCounter.Merges: return FarmProgress.TotalMerges;
                case AchievementCounter.FarmLevel: return FarmLevels.Current;
                case AchievementCounter.OrdersFilled: return OrdersFilled;
                case AchievementCounter.Gold: return Wallet.Instance != null ? Wallet.Instance.Gold : 0;
                case AchievementCounter.PlayerLevel: return FarmExperience.Level;
                case AchievementCounter.FriendsSeen: return FarmProgress.FriendsSeen;
                case AchievementCounter.HelpGiven: return FarmProgress.HelpGiven;
                case AchievementCounter.GiftsSent: return FarmProgress.GiftsSent;
                case AchievementCounter.MarketLots: return FarmProgress.MarketLots;
                default: return 0;
            }
        }

        /// <summary>
        /// Пересчитать пороги и выдать всё, что заслужено. Зовётся по событиям фермы; вызов
        /// лишний раз безвреден — выданное второй раз не выдаётся.
        /// </summary>
        public static void Check()
        {
            // В гостях счётчики чужие: помощь другу не должна приносить вехи за его урожай.
            if (GuestMode.IsGuest) return;

            // Во время восстановления счётчики уже большие, а Earned ещё пуст (сейв читается
            // позже уровня фермы): проверка выдала бы фантомные «Веха взята» с золотом,
            // которое тут же затёр бы Wallet.RestoreState. Следующее живое событие проверит.
            if (FarmingRuntime.Restoring) return;

            HookWallet();

            for (int i = 0; i < All.Length; i++)
            {
                var achievement = All[i];
                if (Earned.Contains(achievement.Id)) continue;
                if (Progress(achievement) < achievement.Goal) continue;

                Earned.Add(achievement.Id);

                var wallet = Wallet.Instance;
                if (wallet != null && achievement.Gold > 0) wallet.Add(achievement.Gold);

                var handler = Earned_;
                if (handler == null) continue;
                try { handler(achievement); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>Заказ сдан — счётчик и проверка вех.</summary>
        public static void NoteOrderFilled()
        {
            OrdersFilled++;
            Check();
        }

        public static string[] CaptureState()
        {
            var list = new string[Earned.Count];
            Earned.CopyTo(list);
            return list;
        }

        /// <summary>
        /// Вернуть взятое из сохранения. Тихо: события не поднимаются, иначе вход в игру
        /// обернулся бы залпом поздравлений за то, что случилось неделю назад.
        /// </summary>
        public static void RestoreState(string[] earned, int ordersFilled)
        {
            Earned.Clear();
            if (earned != null)
                foreach (var id in earned)
                    if (!string.IsNullOrEmpty(id)) Earned.Add(id);

            OrdersFilled = Mathf.Max(0, ordersFilled);
        }

        private static void OnHarvested(Growable plot, HarvestResult result) => Check();
        private static void OnMerged(Growable survivor, Growable absorbed) => Check();
        private static void OnTierAscended(Growable survivor, GrowableDefinition from) => Check();
        private static void OnFarmLevel(int level) => Check();
        private static void OnWalletChanged(Wallet wallet, int delta) => Check();
        private static void OnLevelUp(int level) => Check();

        // Кошелёк — instance и рождается со сценой, поэтому подписка ленивая из Check.
        // Без неё «Десять тысяч в кубышке» зависала бы на полной полосе: золото с рынка,
        // из подарка или ежедневной награды порог не проверяло (сбор и слияние — не
        // единственные источники). Реэнтрантность безопасна: Earned.Add стоит до Wallet.Add.
        private static Wallet _walletHooked;
        private static void HookWallet()
        {
            var wallet = Wallet.Instance;
            if (wallet == null || wallet == _walletHooked) return;
            wallet.Changed -= OnWalletChanged;
            wallet.Changed += OnWalletChanged;
            _walletHooked = wallet;
        }

        // BeforeSceneLoad по той же причине, что и у FarmProgress: на ступени
        // SubsystemRegistration FarmingEvents чистит подписчиков, и порядок внутри не определён.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            Earned.Clear();
            OrdersFilled = 0;
            Earned_ = null;
            _walletHooked = null;

            FarmingEvents.Harvested -= OnHarvested;
            FarmingEvents.Harvested += OnHarvested;
            FarmingEvents.Merged -= OnMerged;
            FarmingEvents.Merged += OnMerged;
            FarmingEvents.TierAscended -= OnTierAscended;
            FarmingEvents.TierAscended += OnTierAscended;
            FarmLevels.Changed -= OnFarmLevel;
            FarmLevels.Changed += OnFarmLevel;
            FarmExperience.LevelUp -= OnLevelUp;
            FarmExperience.LevelUp += OnLevelUp;
        }
    }
}

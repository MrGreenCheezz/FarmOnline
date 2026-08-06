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
        private static void OnFarmLevel(int level) => Check();

        // BeforeSceneLoad по той же причине, что и у FarmProgress: на ступени
        // SubsystemRegistration FarmingEvents чистит подписчиков, и порядок внутри не определён.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            Earned.Clear();
            OrdersFilled = 0;
            Earned_ = null;

            FarmingEvents.Harvested -= OnHarvested;
            FarmingEvents.Harvested += OnHarvested;
            FarmingEvents.Merged -= OnMerged;
            FarmingEvents.Merged += OnMerged;
            FarmLevels.Changed -= OnFarmLevel;
            FarmLevels.Changed += OnFarmLevel;
        }
    }
}

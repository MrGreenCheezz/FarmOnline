using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Корм для скотины: мешки копятся в постройке-кормушке по реальным часам, игрок относит
    /// их животным, и накормленное отдаёт вдвое больше.
    /// <para>
    /// Близнец <see cref="FarmWater"/> и намеренно: у растений своя забота (полить — вырастет
    /// быстрее), у скотины своя (накормить — отдаст больше), и обе читаются одинаково, потому
    /// что устроены одинаково. Общий запас на двоих был бы дешевле в коде и хуже в игре: одно
    /// ведро на грядку и корову превращает уход в «потратить единицу», а не в решение.
    /// </para>
    /// <para>
    /// Почему запас, а не предмет склада: ровно по той же причине, что у подкормки — предмет
    /// немедленно обрастает связями (продать, унести фермером, положить в заказ), а серверная
    /// проверка из одного числа превращается в сверку со складом.
    /// </para>
    /// </summary>
    public static class FarmFeed
    {
        /// <summary>
        /// Сколько реальных секунд копится один мешок. Медленнее ведра: корм не ускоряет
        /// ожидание, а удваивает выдачу — за него платят временем, а не решением «куда лить».
        /// </summary>
        public const double SecondsPerCharge = 3.0 * 3600.0;

        /// <summary>Вместимость кормушки первого уровня.</summary>
        public const int BaseCapacity = 4;

        /// <summary>Сколько мешков добавляет каждый следующий уровень постройки.</summary>
        public const int CapacityPerLevel = 2;

        /// <summary>
        /// Во сколько раз больше отдаёт накормленное животное. Целое и ровно два — по той же
        /// причине, что у подкормки: базовый урожай почти всюду единица, и дробная прибавка
        /// сгорела бы в округлении.
        /// </summary>
        public const int YieldMultiplier = 2;

        private static int _charges;
        private static double _filledAt;

        /// <summary>Запас изменился — HUD перерисовывает счётчик на кнопке корма.</summary>
        public static event Action Changed;

        /// <summary>Животное накормлено. Слушает всё, что рисует, звучит и сохраняет.</summary>
        public static event Action<Growable> Fed;

        /// <summary>Накормить не вышло, и вот почему: сборка фермы не видит интерфейса.</summary>
        public static event Action<string, Vector3> Refused;

        /// <summary>
        /// Накормить животное. Единственная точка, где сходятся запас, состояние загона
        /// и объяснение отказа.
        /// </summary>
        public static bool Feed(Growable plot)
        {
            if (plot == null) return false;

            Vector3 at = plot.transform.position;

            if (!plot.CanFeed)
            {
                // Три разных «нет» — три разных слова: кормить нечего, уже кормлено,
                // и «это не скотина» (ведром по корове ошибаются так же часто, как кормом
                // по грядке — и оба раза игрок должен понять, что взял не то).
                Refuse(plot.Category != ResourceCategory.Livestock
                        ? "корм — для скотины"
                        : plot.Fed ? "уже кормлено" : "кормить некого", at);
                return false;
            }

            if (!HasFeeder)
            {
                Refuse("нужен хлев", at);
                return false;
            }

            if (!TrySpend())
            {
                double next = SecondsToNext;
                Refuse(next > 0.0
                    ? "корма нет — мешок через " + FormatWait(next)
                    : "корма нет", at);
                return false;
            }

            if (!plot.TryFeed())
            {
                // Загон отказался уже после списания — мешок возвращаем: потраченное впустую
                // игрок заметит, а причину не узнает никогда.
                _charges++;
                Raise();
                return false;
            }

            var handler = Fed;
            if (handler == null) return true;
            try { handler(plot); }
            catch (Exception e) { Debug.LogException(e); }
            return true;
        }

        /// <summary>Срок словами, округление вверх: «через 5 с» надёжнее «через 0 м».</summary>
        private static string FormatWait(double seconds)
        {
            if (seconds < 60.0) return Mathf.CeilToInt((float)seconds) + " с";
            if (seconds < 3600.0) return Mathf.CeilToInt((float)(seconds / 60.0)) + " мин";
            return Mathf.CeilToInt((float)(seconds / 3600.0)) + " ч";
        }

        private static void Refuse(string reason, Vector3 at)
        {
            var handler = Refused;
            if (handler == null) return;
            try { handler(reason, at); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Сколько мешков в кормушке прямо сейчас.</summary>
        public static int Charges
        {
            get { Tick(); return _charges; }
        }

        /// <summary>Потолок запаса. Ноль означает, что кормушки на ферме нет.</summary>
        public static int Capacity
        {
            get
            {
                int level = FeederLevel();
                return level <= 0 ? 0 : BaseCapacity + (level - 1) * CapacityPerLevel;
            }
        }

        /// <summary>Есть ли на ферме постройка, дающая корм.</summary>
        public static bool HasFeeder => FeederLevel() > 0;

        /// <summary>Через сколько реальных секунд появится следующий мешок. -1, когда полно или кормушки нет.</summary>
        public static double SecondsToNext
        {
            get
            {
                Tick();
                if (_charges >= Capacity || Capacity <= 0) return -1.0;
                return Math.Max(0.0, SecondsPerCharge - (FarmingRuntime.Now - _filledAt));
            }
        }

        /// <summary>Взять мешок. False — брать нечего, и отказ обязан быть слышен вызывающему.</summary>
        public static bool TrySpend()
        {
            Tick();
            if (_charges <= 0) return false;

            // Часы идут с момента, когда в кормушке появилось место: полная не копит,
            // иначе вернувшийся через неделю получил бы недельный запас разом.
            if (_charges >= Capacity) _filledAt = FarmingRuntime.Now;

            _charges--;
            Raise();
            return true;
        }

        /// <summary>Вернуть запас из сохранения.</summary>
        public static void Restore(int charges, double filledAt)
        {
            _charges = Mathf.Max(0, charges);
            _filledAt = filledAt > 0.0 ? filledAt : FarmingRuntime.Now;
            Tick();
            Raise();
        }

        /// <summary>Что положить в сохранение.</summary>
        public static void Capture(out int charges, out double filledAt)
        {
            Tick();
            charges = _charges;
            filledAt = _filledAt;
        }

        /// <summary>Новая партия начинает с полной кормушки — первый день не про ожидание.</summary>
        public static void ResetForNewFarm()
        {
            _charges = BaseCapacity;
            _filledAt = FarmingRuntime.Now;
            Raise();
        }

        private static void Tick()
        {
            double now = FarmingRuntime.Now;

            if (_filledAt <= 0.0) { _filledAt = now; return; }

            int capacity = Capacity;
            if (capacity <= 0) return;

            if (_charges >= capacity) { _filledAt = now; return; }

            int gained = (int)((now - _filledAt) / SecondsPerCharge);
            if (gained <= 0) return;

            int before = _charges;
            _charges = Mathf.Min(capacity, _charges + gained);

            // Остаток времени не сгорает: сдвигаем отметку ровно на выданное, а не на «сейчас».
            _filledAt += gained * SecondsPerCharge;
            if (_charges >= capacity) _filledAt = now;

            if (_charges != before) Raise();
        }

        /// <summary>
        /// Уровень лучшей постройки-кормушки. Признак — отдельный флаг в определении, а не
        /// служба: хлев служит аурой роста живности, и отнимать её ради корма значило бы
        /// менять одну механику на другую вместо того, чтобы добавить.
        /// </summary>
        private static int FeederLevel()
        {
            int best = 0;
            var all = BuildingRegistry.All;

            for (int i = 0; i < all.Count; i++)
            {
                var building = all[i];
                if (building == null || building.Definition == null) continue;
                if (!building.Definition.GivesFeed) continue;

                if (building.Level > best) best = building.Level;
            }

            return best;
        }

        private static void Raise()
        {
            var handler = Changed;
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload.
        // Та же ступень, что у FarmWater: подписчики (HUD, опыт, автосейв) переподписываются
        // на BeforeSceneLoad, и обнуление на другой ступени тихо оставило бы их ни с чем.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Полная кормушка, а не пустая — по той же причине, что полный колодец:
            // о механике не узнают из отказа «корма нет».
            _charges = BaseCapacity;
            _filledAt = 0.0;
            Changed = null;
            Fed = null;
            Refused = null;
        }
    }
}

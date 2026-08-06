using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Вода в колодце: дефицитный запас, которым игрок поливает грядки.
    /// <para>
    /// Дефицитный — это и есть вся механика. Бесплатный полив «сколько хочешь» не был бы
    /// решением: он всегда доступен и всегда правилен, то есть это не выбор, а налог —
    /// не нажал, потерял. Шесть вёдер в сутки заставляют выбирать, на что их потратить,
    /// и выбор тем интереснее, чем разношёрстнее ферма.
    /// </para>
    /// <para>
    /// Копится по серверным часам и <b>в отсутствие игрока</b> — ровно за этим и возвращаются
    /// в такую игру. Копится только при живом колодце: постройка перестаёт быть украшением
    /// и становится условием.
    /// </para>
    /// <para>
    /// Пересчёт ленивый, по обращению: у запаса нет покадрового тика, а «сколько накапало»
    /// однозначно выводится из двух чисел — момента последнего начисления и текущего времени.
    /// Так же живёт и сама грядка, считающая рост от таймстампа посадки.
    /// </para>
    /// </summary>
    public static class FarmWater
    {
        /// <summary>
        /// Сколько реальных секунд копится одно ведро.
        /// <para>
        /// Было 4 часа — по живому отзыву это читалось как стена, а не как ритм: игрок,
        /// потративший колодец, уходил до вечера. Два часа дают дюжину вёдер в сутки —
        /// решать, куда лить, всё ещё приходится, но зайти после обеда уже есть зачем.
        /// </para>
        /// </summary>
        public const double SecondsPerCharge = 2.0 * 3600.0;

        /// <summary>Вместимость колодца первого уровня.</summary>
        public const int BaseCapacity = 6;

        /// <summary>Сколько вёдер добавляет каждый следующий уровень колодца.</summary>
        public const int CapacityPerLevel = 3;

        /// <summary>
        /// Какую долю цикла засчитывает одно ведро. Доля, а не секунды: ведро обязано быть
        /// одинаково осмысленным и на пятиминутной редиске, и на двенадцатичасовом дереве.
        /// </summary>
        public const float CycleFraction = 0.12f;

        private static int _charges;
        private static double _filledAt;

        /// <summary>Запас изменился — HUD перерисовывает счётчик.</summary>
        public static event Action Changed;

        /// <summary>Грядку полили. Слушает всё, что рисует и звучит.</summary>
        public static event Action<Growable> Poured;

        /// <summary>
        /// Полить не вышло, и вот почему. Событие, а не тихий <c>false</c>: жест игрока,
        /// провалившийся без единого знака, — дефект по правилам проекта, а сборка фермы
        /// не видит интерфейса и сама сказать не может.
        /// </summary>
        public static event Action<string, Vector3> Refused;

        /// <summary>
        /// Полить грядку из колодца. Единственная точка, где сходятся запас воды, состояние
        /// грядки и объяснение отказа, — чтобы вызывающему не приходилось знать три правила
        /// и придумывать формулировки самому.
        /// </summary>
        public static bool Pour(Growable plot)
        {
            if (plot == null) return false;

            Vector3 at = plot.transform.position;

            if (!plot.CanWater)
            {
                Refuse(plot.Watered ? "уже полито" : "поливать нечего", at);
                return false;
            }

            if (!HasWell)
            {
                Refuse("нужен колодец", at);
                return false;
            }

            if (!TrySpend())
            {
                // Отказ обязан объяснять, что делать дальше: голое «колодец пуст» читается
                // как поломка, а срок превращает его в обещание.
                double next = SecondsToNext;
                Refuse(next > 0.0
                    ? "колодец пуст — ведро через " + FormatWait(next)
                    : "колодец пуст", at);
                return false;
            }

            if (!plot.TryWater(CycleFraction))
            {
                // Грядка отказалась уже после того, как ведро списано, — возвращаем.
                // Списанное впустую ведро игрок заметит, а причину не узнает никогда.
                _charges++;
                Raise();
                return false;
            }

            var handler = Poured;
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

        /// <summary>Сколько вёдер в колодце прямо сейчас.</summary>
        public static int Charges
        {
            get { Tick(); return _charges; }
        }

        /// <summary>Потолок запаса. Ноль означает, что колодца на ферме нет.</summary>
        public static int Capacity
        {
            get
            {
                int level = WellLevel();
                return level <= 0 ? 0 : BaseCapacity + (level - 1) * CapacityPerLevel;
            }
        }

        /// <summary>Есть ли на ферме колодец. Без него вода не берётся ниоткуда.</summary>
        public static bool HasWell => WellLevel() > 0;

        /// <summary>Через сколько реальных секунд нальётся следующее ведро. -1, когда полно или колодца нет.</summary>
        public static double SecondsToNext
        {
            get
            {
                Tick();
                if (_charges >= Capacity || Capacity <= 0) return -1.0;
                return Math.Max(0.0, SecondsPerCharge - (FarmingRuntime.Now - _filledAt));
            }
        }

        /// <summary>Взять ведро. False — брать нечего, и отказ обязан быть слышен вызывающему.</summary>
        public static bool TrySpend()
        {
            Tick();
            if (_charges <= 0) return false;

            // Часы начинают идти с момента, когда в колодце появилось место: полный колодец
            // не копит, иначе вернувшийся через неделю получил бы недельный запас разом.
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

        /// <summary>Новая партия начинает с полного колодца — первый день не должен быть про ожидание.</summary>
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
            // Иначе заход в игру раз в три часа сбрасывал бы недолитое ведро каждый раз.
            _filledAt += gained * SecondsPerCharge;
            if (_charges >= capacity) _filledAt = now;

            if (_charges != before) Raise();
        }

        private static int WellLevel()
        {
            int best = 0;
            var all = BuildingRegistry.All;

            for (int i = 0; i < all.Count; i++)
            {
                var building = all[i];
                if (building == null || building.Definition == null) continue;
                if (building.Definition.Service != BuildingService.Well) continue;

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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Полный колодец, а не пустой: это состояние новой партии — сейв перезапишет
            // своим. Пустой по умолчанию уже стоил первого впечатления: игрок узнавал
            // о поливе из отказа «колодец пуст» и четыре часа не мог попробовать механику.
            _charges = BaseCapacity;
            _filledAt = 0.0;
            Changed = null;
            Poured = null;
            Refused = null;
        }
    }
}

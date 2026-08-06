using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Подкормка: второй вид ухода за грядкой. Полив торгует временем, подкормка — урожаем.
    /// <para>
    /// Запас, а не предмет на складе, и это осознанно. Ресурс склада немедленно обрастает
    /// связями, которых уходу не нужно ни одной: его можно продать, его унесёт фермер, его
    /// потребует заказ. Запас же делает ровно одно дело и проверяется на сервере одним числом.
    /// </para>
    /// <para>
    /// Не копится сам по себе — в отличие от воды. Вода идёт из колодца, то есть из места,
    /// а подкормка из дела: её платят за сданный заказ. Поэтому ферма, на которую только
    /// заходят собрать, воду накапливает, а подкормку — нет.
    /// </para>
    /// </summary>
    public static class FarmFertilizer
    {
        /// <summary>
        /// Во сколько раз больше даёт подкормленная грядка.
        /// <para>
        /// Ровно вдвое, и меньше нельзя: у почти всех культур <c>baseYield</c> равен единице,
        /// а сбор округляет — множитель 1.25 дал бы ту же единицу, то есть игрок потратил бы
        /// дефицитную подкормку и не увидел ничего. Тем же уже болеют ауры амбара и пасеки.
        /// </para>
        /// </summary>
        public const int YieldMultiplier = 2;

        /// <summary>Сколько подкормки даёт один сданный заказ.</summary>
        public const int PerOrder = 1;

        /// <summary>Больше этого не накопить: запас — расходник, а не вторая валюта.</summary>
        public const int Capacity = 12;

        private static int _charges;

        /// <summary>Запас изменился — HUD перерисовывает счётчик.</summary>
        public static event Action Changed;

        /// <summary>Грядку подкормили.</summary>
        public static event Action<Growable> Applied;

        /// <summary>Подкормить не вышло, и вот почему.</summary>
        public static event Action<string, Vector3> Refused;

        public static int Charges => _charges;

        /// <summary>Выдать подкормку — за сданный заказ или подарком друга.</summary>
        public static void Grant(int amount)
        {
            if (amount <= 0) return;

            int before = _charges;
            _charges = Mathf.Min(Capacity, _charges + amount);
            if (_charges != before) Raise(Changed);
        }

        /// <summary>
        /// Подкормить грядку. Единственная точка, где сходятся запас, состояние грядки
        /// и объяснение отказа.
        /// </summary>
        public static bool Apply(Growable plot)
        {
            if (plot == null) return false;

            Vector3 at = plot.transform.position;

            if (!plot.CanFertilize)
            {
                Refuse(plot.Fertilized ? "уже подкормлено" : "подкармливать нечего", at);
                return false;
            }

            if (_charges <= 0)
            {
                Refuse("нет подкормки", at);
                return false;
            }

            if (!plot.TryFertilize()) return false;

            _charges--;
            Raise(Changed);

            var handler = Applied;
            if (handler == null) return true;
            try { handler(plot); }
            catch (Exception e) { Debug.LogException(e); }
            return true;
        }

        /// <summary>Вернуть запас из сохранения.</summary>
        public static void Restore(int charges)
        {
            _charges = Mathf.Clamp(charges, 0, Capacity);
            Raise(Changed);
        }

        private static void Refuse(string reason, Vector3 at)
        {
            var handler = Refused;
            if (handler == null) return;
            try { handler(reason, at); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void Raise(Action handler)
        {
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _charges = 0;
            Changed = null;
            Applied = null;
            Refused = null;
        }
    }
}

using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Общефермовая шина событий. Каждая грядка поднимает те же события и локально
    /// (<see cref="Growable.Planted"/> и остальные) — подписывайся на грядку, когда важна
    /// одна, и сюда, когда системе нужны все сразу: счётчики UI, квесты, звук, статистика
    /// и автономный персонаж, решающий, чем заняться.
    /// </summary>
    public static class FarmingEvents
    {
        /// <summary>Что-то посажено. Поднимается после полной инициализации грядки.</summary>
        public static event Action<Growable> Planted;

        /// <summary>Тик роста: грядка перешла на переданный индекс стадии.</summary>
        public static event Action<Growable, int> StageAdvanced;

        /// <summary>Дошло до последней стадии, теперь можно собирать.</summary>
        public static event Action<Growable> Ready;

        /// <summary>Собрано. Урожай уже отправлен в <see cref="FarmingRuntime.Sink"/>.</summary>
        public static event Action<Growable, HarvestResult> Harvested;

        /// <summary>Опустело — собрано без отрастания или очищено вручную.</summary>
        public static event Action<Growable> Cleared;

        /// <summary>
        /// Две грядки слились. Первый аргумент — выживший (уровень уже поднят),
        /// второй — уничтожаемый: читай из него нужное прямо сейчас.
        /// </summary>
        public static event Action<Growable, Growable> Merged;

        internal static void RaisePlanted(Growable g) => Safe(Planted, g, nameof(Planted));
        internal static void RaiseReady(Growable g) => Safe(Ready, g, nameof(Ready));
        internal static void RaiseCleared(Growable g) => Safe(Cleared, g, nameof(Cleared));

        internal static void RaiseStageAdvanced(Growable g, int stage)
        {
            var handler = StageAdvanced;
            if (handler == null) return;
            try { handler(g, stage); }
            catch (Exception e) { Debug.LogException(e, g); }
        }

        internal static void RaiseMerged(Growable survivor, Growable absorbed)
        {
            var handler = Merged;
            if (handler == null) return;
            try { handler(survivor, absorbed); }
            catch (Exception e) { Debug.LogException(e, survivor); }
        }

        internal static void RaiseHarvested(Growable g, in HarvestResult result)
        {
            var handler = Harvested;
            if (handler == null) return;
            try { handler(g, result); }
            catch (Exception e) { Debug.LogException(e, g); }
        }

        // Один сломавшийся подписчик не должен останавливать тикание остальной фермы.
        private static void Safe(Action<Growable> handler, Growable g, string label)
        {
            if (handler == null) return;
            try { handler(g); }
            catch (Exception e)
            {
                Debug.LogError("[Farming] Ошибка в подписчике " + label, g);
                Debug.LogException(e, g);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Planted = null;
            StageAdvanced = null;
            Ready = null;
            Harvested = null;
            Cleared = null;
            Merged = null;
        }
    }
}

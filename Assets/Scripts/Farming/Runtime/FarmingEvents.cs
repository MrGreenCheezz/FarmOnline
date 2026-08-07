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

        /// <summary>Стройплощадка достроилась в настоящую вещь (театр труда, этап 3).</summary>
        public static event Action<Transform> Constructed;

        /// <summary>
        /// Две грядки слились. Первый аргумент — выживший (уровень уже поднят),
        /// второй — уничтожаемый: читай из него нужное прямо сейчас.
        /// </summary>
        public static event Action<Growable, Growable> Merged;

        /// <summary>
        /// Слияние двух грядок 20 уровня переродило выжившую в следующую ступень линии.
        /// Второй аргумент — прежнее определение. Обычный <see cref="Merged"/> при этом
        /// НЕ поднимается: у перехода свои текст, звук и опыт, а двойное событие дало бы
        /// два всплывающих текста друг на друге.
        /// </summary>
        public static event Action<Growable, GrowableDefinition> TierAscended;

        /// <summary>
        /// Игрок бросил грядку на несливаемую пару того же вида (потолок 20, вершина линии).
        /// Отказ обязан быть слышен — UI показывает причину в точке события.
        /// </summary>
        public static event Action<string, Vector3> MergeRefused;

        /// <summary>
        /// Игрок ткнул пустой рукой в то, что ещё не готово, — грядка отвечает сроком
        /// («поспеет через 2 ч»). Это не отказ, а ответ на вопрос «когда?»: молчание
        /// на прямой жест игрок читает как поломку.
        /// </summary>
        public static event Action<string, Vector3> Notice;

        internal static void RaisePlanted(Growable g) => Safe(Planted, g, nameof(Planted));
        internal static void RaiseReady(Growable g) => Safe(Ready, g, nameof(Ready));
        internal static void RaiseCleared(Growable g) => Safe(Cleared, g, nameof(Cleared));

        internal static void RaiseConstructed(Transform built)
        {
            var handler = Constructed;
            if (handler == null || built == null) return;
            try { handler(built); }
            catch (Exception e) { Debug.LogException(e, built); }
        }

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

        internal static void RaiseTierAscended(Growable survivor, GrowableDefinition from)
        {
            var handler = TierAscended;
            if (handler == null) return;
            try { handler(survivor, from); }
            catch (Exception e) { Debug.LogException(e, survivor); }
        }

        internal static void RaiseMergeRefused(string reason, Vector3 at)
        {
            var handler = MergeRefused;
            if (handler == null || string.IsNullOrEmpty(reason)) return;
            try { handler(reason, at); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Публичное: зовёт слой взаимодействия, он не видит UI напрямую.</summary>
        public static void RaiseNotice(string text, Vector3 at)
        {
            var handler = Notice;
            if (handler == null || string.IsNullOrEmpty(text)) return;
            try { handler(text, at); }
            catch (Exception e) { Debug.LogException(e); }
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
            Constructed = null;
            Merged = null;
            TierAscended = null;
            MergeRefused = null;
            Notice = null;
        }
    }
}

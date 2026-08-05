using System;
using UnityEngine;

namespace Farm.Net
{
    /// <summary>
    /// Строка сетевого состояния для HUD. Правило проекта: отказ системы обязан быть
    /// заметным — каждый сетевой сбой проходит здесь, и молчаливого «не получилось» в сети
    /// не существует. HUD подписывается на <see cref="Changed"/> и просто показывает строку.
    /// </summary>
    public static class NetStatus
    {
        /// <summary>Текущая строка состояния. Пустая — сказать нечего.</summary>
        public static string Line { get; private set; } = "";

        /// <summary>
        /// Строка говорит об отказе, а не о ходе дел. Нужна показывающему: отказ обязан быть
        /// заметным, а разбирать текст по началу «сеть:» — гадание, которое переживёт первую
        /// же переформулировку сообщения.
        /// </summary>
        public static bool Bad { get; private set; }

        /// <summary>Строка сменилась. Несёт новое значение <see cref="Line"/>.</summary>
        public static event Action<string> Changed;

        /// <summary>Показать состояние. Пустая строка гасит прежнее сообщение.</summary>
        public static void Set(string line) => Raise(line, false);

        /// <summary>
        /// Объявить сбой: строка в HUD плюс предупреждение в консоль — в вебе консоль браузера
        /// остаётся единственным окном внутрь собранной игры.
        /// </summary>
        public static void Fail(string context, string detail)
        {
            string line = "сеть: " + context + " — " + detail;
            Raise(line, true);
            Debug.LogWarning("[Net] " + line);
        }

        private static void Raise(string line, bool bad)
        {
            Line = line ?? "";
            Bad = bad;

            // Подписчик из UI не должен уронить сетевой код: его ошибка — его ошибка.
            try
            {
                Changed?.Invoke(Line);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload — чистим явно,
        // иначе второй запуск стартует со вчерашней строкой и мёртвыми подписчиками.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Line = "";
            Bad = false;
            Changed = null;
        }
    }
}

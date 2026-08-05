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

        /// <summary>Строка сменилась. Несёт новое значение <see cref="Line"/>.</summary>
        public static event Action<string> Changed;

        /// <summary>Показать состояние. Пустая строка гасит прежнее сообщение.</summary>
        public static void Set(string line)
        {
            Line = line ?? "";

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

        /// <summary>
        /// Объявить сбой: строка в HUD плюс предупреждение в консоль — в вебе консоль браузера
        /// остаётся единственным окном внутрь собранной игры.
        /// </summary>
        public static void Fail(string context, string detail)
        {
            string line = "сеть: " + context + " — " + detail;
            Set(line);
            Debug.LogWarning("[Net] " + line);
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload — чистим явно,
        // иначе второй запуск стартует со вчерашней строкой и мёртвыми подписчиками.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Line = "";
            Changed = null;
        }
    }
}

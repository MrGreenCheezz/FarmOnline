using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Узел ручного сбора, который игрок держит прямо сейчас. Один слот на всю ферму.
    /// <para>
    /// Живёт в сборке фермы по той же причине, что и <see cref="BuildingSelection"/>: слой
    /// взаимодействия решает, за что схватились, UI решает, что нарисовать, и ссылаться друг
    /// на друга они не должны. Общий однослотовый фокус — самое маленькое, что их соединяет.
    /// </para>
    /// <para>
    /// Нужен ради одной вещи: у режима удержания есть прогресс, но без индикатора игрок держит
    /// кнопку вслепую и не знает, сколько ещё осталось.
    /// </para>
    /// </summary>
    public static class GatherFocus
    {
        private static Gatherable _current;

        /// <summary>Удерживаемый узел или null. Уничтоженный объект отдаётся настоящим null.</summary>
        public static Gatherable Current => _current != null ? _current : null;

        /// <summary>Фокус сменился. Аргумент — новый узел, null при отпускании.</summary>
        public static event Action<Gatherable> Changed;

        public static void Set(Gatherable node)
        {
            // Ссылка на уничтоженный объект для C# не равна настоящему null, но перегруженный
            // оператор Unity считает её null. Приводим к настоящему null, иначе повторный захват
            // того же места не изменил бы фокус.
            if (node == null) node = null;
            if (ReferenceEquals(_current, node)) return;

            _current = node;

            var handler = Changed;
            if (handler == null) return;
            try { handler(node); }
            catch (Exception e) { Debug.LogException(e, node); }
        }

        public static void Clear() => Set(null);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            Changed = null;
        }
    }
}

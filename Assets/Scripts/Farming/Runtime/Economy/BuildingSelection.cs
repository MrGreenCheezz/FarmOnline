using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Какую постройку игрок сейчас рассматривает. Один слот на всю ферму.
    /// <para>
    /// Живёт в сборке фермы, а не в UI, потому что две половины выбора сидят по разные
    /// стороны графа сборок: слой взаимодействия решает, по чему кликнули, UI решает,
    /// что нарисовать, и друг на друга они не ссылаются. Общий однослотовый выбор —
    /// самое маленькое, что их соединяет, не создавая зависимости ни в одну сторону.
    /// </para>
    /// </summary>
    public static class BuildingSelection
    {
        private static Building _current;

        /// <summary>
        /// Выбранная постройка или null. После уничтожения постройки возвращает null,
        /// так что вызывающему не приходится помнить про «фальшивый null» Unity.
        /// </summary>
        public static Building Current => _current != null ? _current : null;

        /// <summary>Выбор сменился. Аргумент — новый выбор, null при снятии.</summary>
        public static event Action<Building> Changed;

        public static void Select(Building building)
        {
            // Ссылка на уничтоженный объект для C# не равна настоящему null, но перегруженный
            // оператор Unity считает её null. Приводим к настоящему null, иначе повторный клик
            // по уже снесённой постройке не изменил бы выбор.
            if (building == null) building = null;
            if (ReferenceEquals(_current, building)) return;

            _current = building;

            var handler = Changed;
            if (handler == null) return;
            try { handler(building); }
            catch (Exception e) { Debug.LogException(e, building); }
        }

        public static void Clear() => Select(null);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            Changed = null;
        }
    }
}

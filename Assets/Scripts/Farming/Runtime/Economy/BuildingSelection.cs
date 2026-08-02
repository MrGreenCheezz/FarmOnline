using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Which building the player is currently inspecting. One slot, farm-wide.
    /// <para>
    /// It lives in the farming assembly rather than in the UI because the two halves of a selection
    /// sit on opposite sides of the assembly graph: the interaction layer decides what was clicked,
    /// the UI decides what to draw, and neither references the other. A shared one-slot selection is
    /// the smallest thing that joins them without inventing a dependency in either direction.
    /// </para>
    /// </summary>
    public static class BuildingSelection
    {
        private static Building _current;

        /// <summary>
        /// Selected building, or null. Reports null once the building is destroyed, so a caller
        /// never has to think about Unity's fake-null.
        /// </summary>
        public static Building Current => _current != null ? _current : null;

        /// <summary>Selection changed. Argument is the new selection, null when cleared.</summary>
        public static event Action<Building> Changed;

        public static void Select(Building building)
        {
            // Нормализуем разрушенный объект, иначе повторный клик по нему не поменяет выбор.
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

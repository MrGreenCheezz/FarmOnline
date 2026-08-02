using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Метка «это игрок может поднять и переставить» — постройки, декорации, всё,
    /// что не <see cref="Growable"/> (те перетаскиваются уже потому, что числятся
    /// в <see cref="GrowableRegistry"/>).
    /// <para>
    /// Нарочно только маркер с реестром, без кода ввода: перетаскивание живёт в слое
    /// взаимодействия, а здесь оно притащило бы зависимость от ввода в сборку фермы
    /// ради одного флажка.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Movable")]
    public sealed class Movable : MonoBehaviour
    {
        [Tooltip("На какой высоте считать «центр» объекта при наведении курсора.")]
        [SerializeField, Min(0f)] private float _aimHeight = 0.6f;

        [Tooltip("Можно ли двигать. Выключи, чтобы прибить объект к месту.")]
        [SerializeField] private bool _canMove = true;

        internal int RegistryIndex = -1;

        public float AimHeight => _aimHeight;
        public bool CanMove { get => _canMove; set => _canMove = value; }

        private void OnEnable() => MovableRegistry.Register(this);
        private void OnDisable() => MovableRegistry.Unregister(this);
    }

    /// <summary>Живой список всего перетаскиваемого, кроме грядок. Тот же O(1) swap-removal, что и везде.</summary>
    public static class MovableRegistry
    {
        private static readonly List<Movable> _all = new List<Movable>(32);

        public static IReadOnlyList<Movable> All => _all;
        public static int Count => _all.Count;

        internal static void Register(Movable m)
        {
            if (m == null || m.RegistryIndex >= 0) return;
            m.RegistryIndex = _all.Count;
            _all.Add(m);
        }

        internal static void Unregister(Movable m)
        {
            if (m == null) return;

            int i = m.RegistryIndex;
            if (i < 0 || i >= _all.Count || _all[i] != m) { m.RegistryIndex = -1; return; }

            int last = _all.Count - 1;
            _all[i] = _all[last];
            if (_all[i] != null) _all[i].RegistryIndex = i;
            _all.RemoveAt(last);
            m.RegistryIndex = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all.Clear();
    }
}

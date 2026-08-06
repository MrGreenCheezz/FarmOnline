using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Characters
{
    /// <summary>
    /// Живой список всех жителей фермы. Сегодня в нём один фермер — но всё, что раньше искало
    /// «того самого фермера» через FindFirstObjectByType, теперь спрашивает здесь, и появление
    /// второго жителя перестало быть переписыванием: деревня начинается с реестра.
    /// <para>
    /// Тот же O(1) swap-removal, что у грядок и построек. <see cref="Primary"/> — первый
    /// зарегистрированный: сценовый фермер регистрируется раньше любых будущих пришлых,
    /// и однофермерский UI осмысленно показывает именно его.
    /// </para>
    /// </summary>
    public static class FarmerRegistry
    {
        private static readonly List<FarmerAgent> _all = new List<FarmerAgent>(4);

        /// <summary>Назначенный житель №1. Null — берётся первый зарегистрированный.</summary>
        private static FarmerAgent _designated;

        public static IReadOnlyList<FarmerAgent> All => _all;
        public static int Count => _all.Count;

        /// <summary>
        /// Житель №1 — за ним следит однофермерский UI, в него ложатся сейвы эпохи одного
        /// фермера. Назначение ростера главнее порядка регистрации: порядок — это порядок
        /// OnEnable, а его Unity не обещает (замер 06.08.2026 дал обратный при прямом
        /// порядке в иерархии). Уничтоженный назначенец сам отпадает юнити-null'ом.
        /// </summary>
        public static FarmerAgent Primary =>
            _designated != null ? _designated : _all.Count > 0 ? _all[0] : null;

        /// <summary>
        /// Назначить житель №1 явно. Зовёт <see cref="ColonyRoster"/> первым делом партии:
        /// состав колонии — решение данных, а не гонки колбэков.
        /// </summary>
        internal static void Designate(FarmerAgent agent)
        {
            if (_designated == agent) return;
            _designated = agent;
            RaiseChanged();
        }

        /// <summary>Состав жителей изменился. Дирижёры отклика перевешивают подписки здесь.</summary>
        public static event Action Changed;

        internal static void Register(FarmerAgent agent)
        {
            if (agent == null || agent.RegistryIndex >= 0) return;
            agent.RegistryIndex = _all.Count;
            _all.Add(agent);
            RaiseChanged();
        }

        internal static void Unregister(FarmerAgent agent)
        {
            if (agent == null) return;

            int i = agent.RegistryIndex;
            if (i < 0 || i >= _all.Count || _all[i] != agent) { agent.RegistryIndex = -1; return; }

            int last = _all.Count - 1;
            _all[i] = _all[last];
            if (_all[i] != null) _all[i].RegistryIndex = i;
            _all.RemoveAt(last);
            agent.RegistryIndex = -1;
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            _designated = null;
            Changed = null;
        }
    }
}

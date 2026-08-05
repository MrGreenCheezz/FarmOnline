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

        public static IReadOnlyList<FarmerAgent> All => _all;
        public static int Count => _all.Count;

        /// <summary>Первый житель — тот, за кем следит однофермерский UI.</summary>
        public static FarmerAgent Primary => _all.Count > 0 ? _all[0] : null;

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
            Changed = null;
        }
    }
}

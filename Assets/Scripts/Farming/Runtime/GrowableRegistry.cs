using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Живой указатель на все грядки сцены, плюс отдельный список готовых к сбору.
    /// <para>
    /// Это опорная поверхность запросов для автономного персонажа: вместо сканирования
    /// мира он спрашивает «что спелого рядом со мной?» и получает ответ, не тронув ни
    /// одну ещё растущую грядку. Оба списка используют swap-removal с кэшированным
    /// индексом, поэтому регистрация, выход и смена готовности — всё O(1).
    /// </para>
    /// </summary>
    public static class GrowableRegistry
    {
        private static readonly List<Growable> _all = new List<Growable>(256);
        private static readonly List<Growable> _ready = new List<Growable>(64);

        public static IReadOnlyList<Growable> All => _all;

        /// <summary>Всё, что сейчас можно собрать. Не держи между кадрами — список мутируется на месте.</summary>
        public static IReadOnlyList<Growable> Ready => _ready;

        public static int Count => _all.Count;
        public static int ReadyCount => _ready.Count;

        internal static void Register(Growable g)
        {
            if (g == null || g.RegistryIndex >= 0) return;
            g.RegistryIndex = _all.Count;
            _all.Add(g);
        }

        internal static void Unregister(Growable g)
        {
            if (g == null) return;
            SetReady(g, false);

            int i = g.RegistryIndex;
            if (i < 0 || i >= _all.Count || _all[i] != g) { g.RegistryIndex = -1; return; }

            int last = _all.Count - 1;
            _all[i] = _all[last];
            if (_all[i] != null) _all[i].RegistryIndex = i;
            _all.RemoveAt(last);
            g.RegistryIndex = -1;
        }

        internal static void SetReady(Growable g, bool ready)
        {
            if (g == null) return;

            if (ready)
            {
                if (g.ReadyIndex >= 0) return;
                g.ReadyIndex = _ready.Count;
                _ready.Add(g);
                return;
            }

            int i = g.ReadyIndex;
            if (i < 0 || i >= _ready.Count || _ready[i] != g) { g.ReadyIndex = -1; return; }

            int last = _ready.Count - 1;
            _ready[i] = _ready[last];
            if (_ready[i] != null) _ready[i].ReadyIndex = i;
            _ready.RemoveAt(last);
            g.ReadyIndex = -1;
        }

        /// <summary>
        /// Ближайшая к <paramref name="position"/> спелая грядка, при желании — только одной
        /// категории. Возвращает null, когда подходящих нет.
        /// </summary>
        public static Growable FindNearestReady(Vector3 position, ResourceCategory? category = null,
                                                float maxDistance = float.PositiveInfinity)
        {
            Growable best = null;
            float bestSqr = maxDistance >= float.PositiveInfinity
                ? float.PositiveInfinity
                : maxDistance * maxDistance;

            for (int i = 0; i < _ready.Count; i++)
            {
                var g = _ready[i];
                if (g == null) continue;
                if (category.HasValue && g.Category != category.Value) continue;

                float sqr = (g.transform.position - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = g;
            }

            return best;
        }

        /// <summary>Заполняет <paramref name="results"/> спелыми грядками категории. Сначала очищает список.</summary>
        public static void GetReady(List<Growable> results, ResourceCategory? category = null)
        {
            if (results == null) return;
            results.Clear();

            for (int i = 0; i < _ready.Count; i++)
            {
                var g = _ready[i];
                if (g == null) continue;
                if (category.HasValue && g.Category != category.Value) continue;
                results.Add(g);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            _ready.Clear();
        }
    }
}

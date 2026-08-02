using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Seeds the farm with things to pick up after dark, and clears them at dawn.
    /// <para>
    /// The night used to be dead time: the farmer sleeps, nothing grows faster, and the player has
    /// nothing to do but wait for morning. Idle games are allowed quiet stretches, but a stretch
    /// where being present is worth exactly nothing teaches the player to close the game — and that
    /// is the one habit an idle game cannot afford.
    /// </para>
    /// <para>
    /// So the night is not the farmer's shift, it is the player's. What appears here yields only to
    /// hand, never to him, which is also why it is worth more per unit than anything he collects on
    /// his own: it costs attention rather than time.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Night Harvest")]
    public sealed class NightHarvest : MonoBehaviour
    {
        [Serializable]
        public sealed class Kind
        {
            public GameObject Prefab;
            public ResourceDefinition Resource;

            [Tooltip("Сколько единиц в одном узле.")]
            [Min(1)] public int AmountMin = 2;
            [Min(1)] public int AmountMax = 4;

            public GatherMode Mode = GatherMode.Click;

            [Tooltip("Относительная частота.")]
            [Min(0f)] public float Weight = 1f;
        }

        [SerializeField] private Kind[] _kinds = Array.Empty<Kind>();

        [Header("Сколько и где")]
        [Tooltip("Сколько узлов появляется за ночь.")]
        [SerializeField, Min(0)] private int _count = 18;

        [Tooltip("Радиус появления. Держится в пределах фермы: гоняться за светлячками " +
                 "по всему лесу — работа, а не отдых.")]
        [SerializeField, Min(1f)] private float _radius = 11f;

        [Tooltip("Не ближе этого к грядкам и постройкам, чтобы клик не уходил не туда.")]
        [SerializeField, Min(0f)] private float _clearance = 1.2f;

        [Tooltip("Убирать несобранное с рассветом. Выключи, чтобы копилось.")]
        [SerializeField] private bool _clearAtDawn = true;

        private readonly List<Gatherable> _spawned = new List<Gatherable>();
        private Transform _holder;
        private bool _nightActive;

        /// <summary>How many nodes are out right now.</summary>
        public int Active
        {
            get
            {
                int alive = 0;
                for (int i = 0; i < _spawned.Count; i++)
                    if (_spawned[i] != null) alive++;
                return alive;
            }
        }

        /// <summary>
        /// Sync with the clock every frame instead of subscribing to its events.
        /// <para>
        /// Both components sit on the same object, so <c>OnEnable</c> order decides whether the
        /// clock's singleton exists yet when this one looks for it — and when it does not, the
        /// subscription silently never happens and the night stays empty forever. Comparing state
        /// costs one bool per frame and cannot be missed, whichever way Unity orders them.
        /// </para>
        /// </summary>
        private void Update()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;

            if (cycle.IsNight == _nightActive) return;

            if (cycle.IsNight) Spawn();
            else
            {
                _nightActive = false;
                if (_clearAtDawn) Clear();
            }
        }

        /// <summary>Scatter a night's worth of nodes. Public so a test can call it without waiting.</summary>
        public void Spawn()
        {
            if (_nightActive) return;
            _nightActive = true;

            Clear();

            if (_kinds == null || _kinds.Length == 0 || _count <= 0) return;

            float totalWeight = 0f;
            foreach (var kind in _kinds)
                if (kind != null && kind.Prefab != null) totalWeight += Mathf.Max(0f, kind.Weight);

            if (totalWeight <= 0f)
            {
                Debug.LogWarning("[Ночь] Нечего рассыпать — не задано ни одного вида", this);
                return;
            }

            _holder = new GameObject("NightHarvest").transform;
            _holder.SetParent(transform, false);

            var bounds = FarmBounds.Instance;
            Vector3 center = bounds != null ? bounds.Center : transform.position;
            float radius = bounds != null ? Mathf.Min(_radius, bounds.UsableRadius) : _radius;

            int attempts = _count * 6;
            int placed = 0;

            while (placed < _count && attempts-- > 0)
            {
                float t = UnityEngine.Random.value;
                float r = Mathf.Sqrt(t) * radius;
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;

                var point = center + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                if (!IsClear(point)) continue;

                var kind = Pick(totalWeight);
                if (kind == null) break;

                var instance = Instantiate(kind.Prefab, point,
                    Quaternion.Euler(0f, UnityEngine.Random.value * 360f, 0f), _holder);

                var node = instance.GetComponent<Gatherable>();
                if (node == null) node = instance.AddComponent<Gatherable>();

                node.Configure(kind.Resource,
                    UnityEngine.Random.Range(kind.AmountMin, Mathf.Max(kind.AmountMin, kind.AmountMax) + 1),
                    kind.Mode);

                _spawned.Add(node);
                placed++;
            }

            if (placed > 0) Debug.Log("[Ночь] выставлено сборов: " + placed, this);
        }

        public void Clear()
        {
            if (_holder != null)
            {
                if (Application.isPlaying) Destroy(_holder.gameObject);
                else DestroyImmediate(_holder.gameObject);
            }

            _holder = null;
            _spawned.Clear();
        }

        private Kind Pick(float totalWeight)
        {
            float roll = UnityEngine.Random.value * totalWeight;

            foreach (var kind in _kinds)
            {
                if (kind == null || kind.Prefab == null) continue;

                roll -= Mathf.Max(0f, kind.Weight);
                if (roll <= 0f) return kind;
            }

            return null;
        }

        private bool IsClear(Vector3 point)
        {
            float sqr = _clearance * _clearance;

            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
                if (plots[i] != null && Flat(plots[i].transform.position - point) < sqr) return false;

            var movables = MovableRegistry.All;
            for (int i = 0; i < movables.Count; i++)
                if (movables[i] != null && Flat(movables[i].transform.position - point) < sqr) return false;

            return true;
        }

        private static float Flat(Vector3 delta)
        {
            delta.y = 0f;
            return delta.sqrMagnitude;
        }
    }
}

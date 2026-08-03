using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// С темнотой рассыпает по ферме то, что можно подобрать, и убирает это на рассвете.
    /// <para>
    /// Раньше ночь была мёртвым временем: фермер спит, ничего не растёт быстрее, игроку остаётся
    /// ждать утра. Инкременталу позволены тихие отрезки, но отрезок, где присутствие не стоит
    /// ровно ничего, учит игрока закрывать игру — а это единственная привычка, которую
    /// инкрементал позволить себе не может.
    /// </para>
    /// <para>
    /// Поэтому ночь — смена не фермера, а игрока. Появившееся здесь отдаётся только рукам и
    /// никогда ему, потому оно и дороже за единицу, чем всё, что он собирает сам: оно стоит
    /// внимания, а не времени.
    /// </para>
    /// <para>
    /// Узлы не выкладываются одним рывком: часть появляется с темнотой, остальное подсыпается
    /// порциями до рассвета. Ночь, выложенная целиком в первую минуту, снова превращается
    /// в ожидание — только теперь с пустым полем.
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

            [Tooltip("Радиус разброса именно этого вида. 0 — взять общий радиус компонента.\n" +
                     "Светлячкам он нужен заметно шире фермы: их ценность не в добыче, а в том, " +
                     "что ночной мир ожил. Плотная россыпь под ногами превращает атмосферу в задачу.")]
            [Min(0f)] public float Radius;

            [Tooltip("Не сеять ближе этого к центру. Позволяет вынести вид наружу, оставив " +
                     "у дома только то, что игрок действительно собирает.")]
            [Min(0f)] public float InnerRadius;

            [Tooltip("Держать внутри границ фермы. Выключи, чтобы вид уходил и за забор.")]
            public bool KeepInsideFarm = true;
        }

        [SerializeField] private Kind[] _kinds = Array.Empty<Kind>();

        [Header("Сколько и где")]
        [Tooltip("Сколько узлов появляется за всю ночь.")]
        [SerializeField, Min(0)] private int _count = 18;

        [Tooltip("Сколько выкладывается сразу с наступлением темноты. Остальное подсыпается " +
                 "порциями: у ночи должен быть повод вернуться к ней через несколько минут.")]
        [SerializeField, Min(0)] private int _initialCount = 6;

        [Tooltip("Через сколько секунд подсыпать следующую порцию.")]
        [SerializeField, Min(1f)] private float _refillInterval = 25f;

        [Tooltip("Сколько узлов в одной порции.")]
        [SerializeField, Min(1)] private int _refillBatch = 2;

        [Tooltip("Общий радиус появления для видов, у которых свой не задан.")]
        [SerializeField, Min(1f)] private float _radius = 11f;

        [Tooltip("Не ближе этого к грядкам и постройкам, чтобы клик не уходил не туда.")]
        [SerializeField, Min(0f)] private float _clearance = 1.2f;

        [Tooltip("Убирать несобранное с рассветом. Выключи, чтобы копилось.")]
        [SerializeField] private bool _clearAtDawn = true;

        private readonly List<Gatherable> _spawned = new List<Gatherable>();
        private Transform _holder;
        private bool _nightActive;
        private int _spawnedTonight;
        private float _refillTimer;

        /// <summary>Сколько узлов лежит прямо сейчас.</summary>
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

        /// <summary>Сколько узлов уже выложено за эту ночь, включая собранные.</summary>
        public int SpawnedTonight => _spawnedTonight;

        /// <summary>
        /// Сверяемся с часами каждый кадр, а не подписываемся на их события.
        /// <para>
        /// Оба компонента сидят на одном объекте, и порядок их <c>OnEnable</c> решает, существует
        /// ли уже синглтон часов, когда этот его ищет, — а когда нет, подписка молча не происходит
        /// и ночь остаётся пустой навсегда. Сравнение состояния стоит один bool за кадр и не может
        /// быть пропущено, в каком бы порядке Unity их ни поднял.
        /// </para>
        /// </summary>
        private void Update()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;

            if (cycle.IsNight != _nightActive)
            {
                if (cycle.IsNight) BeginNight();
                else EndNight();
                return;
            }

            if (_nightActive) Refill();
        }

        // ---- ночной цикл ----

        private void BeginNight()
        {
            _nightActive = true;
            _spawnedTonight = 0;
            _refillTimer = _refillInterval;

            Clear();
            CreateHolder();

            int first = _initialCount > 0 ? Mathf.Min(_initialCount, _count) : _count;
            int placed = Spawn(first);

            if (placed > 0) Debug.Log("[Ночь] выставлено сборов: " + placed, this);
        }

        private void EndNight()
        {
            _nightActive = false;
            if (_clearAtDawn) Clear();
        }

        /// <summary>Подсыпать очередную порцию, если время пришло и лимит ночи не выбран.</summary>
        private void Refill()
        {
            if (_spawnedTonight >= _count) return;

            _refillTimer -= Time.deltaTime;
            if (_refillTimer > 0f) return;
            _refillTimer = _refillInterval;

            if (_holder == null) CreateHolder();
            Spawn(Mathf.Min(_refillBatch, _count - _spawnedTonight));
        }

        /// <summary>Выложить ночную порцию целиком. Публичный, чтобы тест мог вызвать не дожидаясь ночи.</summary>
        public void SpawnAll()
        {
            if (!_nightActive) BeginNight();
            Spawn(_count - _spawnedTonight);
        }

        // ---- расстановка ----

        /// <summary>Выкладывает до <paramref name="amount"/> узлов. Возвращает, сколько встало.</summary>
        private int Spawn(int amount)
        {
            if (amount <= 0 || _kinds == null || _kinds.Length == 0) return 0;

            float totalWeight = 0f;
            foreach (var kind in _kinds)
                if (kind != null && kind.Prefab != null) totalWeight += Mathf.Max(0f, kind.Weight);

            if (totalWeight <= 0f)
            {
                Debug.LogWarning("[Ночь] Нечего рассыпать — не задано ни одного вида", this);
                return 0;
            }

            var bounds = FarmBounds.Instance;
            Vector3 center = bounds != null ? bounds.Center : transform.position;

            int placed = 0;
            for (int i = 0; i < amount; i++)
            {
                // Вид выбираем первым: радиус разброса теперь принадлежит виду, а не компоненту,
                // и без этого светлячок не смог бы уйти за забор, оставив росу у дома.
                var kind = Pick(totalWeight);
                if (kind == null) break;
                if (!TryFindPoint(kind, center, bounds, out Vector3 point)) continue;

                var instance = Instantiate(kind.Prefab, point,
                    Quaternion.Euler(0f, UnityEngine.Random.value * 360f, 0f), _holder);

                var node = instance.GetComponent<Gatherable>();
                if (node == null) node = instance.AddComponent<Gatherable>();

                node.Configure(kind.Resource,
                    UnityEngine.Random.Range(kind.AmountMin, Mathf.Max(kind.AmountMin, kind.AmountMax) + 1),
                    kind.Mode);

                _spawned.Add(node);
                placed++;
                _spawnedTonight++;
            }

            return placed;
        }

        private bool TryFindPoint(Kind kind, Vector3 center, FarmBounds bounds, out Vector3 point)
        {
            float outer = kind.Radius > 0f ? kind.Radius : _radius;
            if (kind.KeepInsideFarm && bounds != null) outer = Mathf.Min(outer, bounds.UsableRadius);

            float inner = Mathf.Clamp(kind.InnerRadius, 0f, Mathf.Max(0f, outer - 0.5f));

            for (int attempt = 0; attempt < 12; attempt++)
            {
                // Корень — иначе точки сгущаются к центру: площадь кольца растёт как квадрат
                // радиуса, а равномерный радиус этого не знает.
                float t = UnityEngine.Random.value;
                float r = Mathf.Sqrt(Mathf.Lerp(inner * inner, outer * outer, t));
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;

                var candidate = center + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                if (!IsClear(candidate)) continue;

                // Ложимся по рельефу: светлячки разлетаются далеко за забор, где земля уже
                // идёт волнами, и на постоянной высоте половина роя висела бы над склоном.
                candidate.y = center.y + FarmingRuntime.Ground.SampleHeight(candidate);

                point = candidate;
                return true;
            }

            point = default;
            return false;
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
            _spawnedTonight = 0;
        }

        private void CreateHolder()
        {
            _holder = new GameObject("NightHarvest").transform;
            _holder.SetParent(transform, false);
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

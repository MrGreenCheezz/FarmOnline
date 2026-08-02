using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Juice
{
    /// <summary>
    /// Рассыпает декоративную траву, цветы и грибы по земле, чтобы ферма стояла в мире,
    /// а не на пустой плоскости.
    /// <para>
    /// Расстановка по сиду, а не случайная: поле, перестраивающее себя при каждом запуске,
    /// читается как баг, а плотность не настроить, когда два запуска не сравнить.
    /// </para>
    /// <para>
    /// Кустики — обычные GameObject и никогда не должны помечаться static. Батчинг сливает меши
    /// в один transform, а шейдер ветра берёт фазу растения из позиции его объекта — слитая
    /// листва качалась бы строем, а это ровно то, от чего ветер и спасает.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Foliage Scatter")]
    public sealed class FoliageScatter : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public GameObject Prefab;

            [Tooltip("Относительная частота. 2 встречается вдвое чаще, чем 1.")]
            [Min(0f)] public float Weight = 1f;

            [Tooltip("Разброс размера. Одинаковые кустики выдают копипасту с одного взгляда.")]
            [Min(0.05f)] public float ScaleMin = 0.85f;
            [Min(0.05f)] public float ScaleMax = 1.25f;
        }

        [Header("Что сеем")]
        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        [Header("Где")]
        [Tooltip("Сколько всего кустиков.")]
        [SerializeField, Min(0)] private int _count = 320;

        [Tooltip("Внутренний радиус кольца засева. 0 — сеять и в середине фермы.")]
        [SerializeField, Min(0f)] private float _innerRadius;

        [Tooltip("Внешний радиус. Заметно больше фермы — мир не должен обрываться за забором.")]
        [SerializeField, Min(1f)] private float _outerRadius = 30f;

        [Tooltip("Во сколько раз реже сеять внутри фермы: там место под грядки.")]
        [SerializeField, Range(0f, 1f)] private float _insideFarmDensity = 0.35f;

        [Tooltip("Не подходить ближе этого к уже стоящим грядкам и постройкам.")]
        [SerializeField, Min(0f)] private float _clearance = 1.1f;

        [Header("Куртины")]
        [Tooltip("Сколько кустиков в среднем в одной куртине. 1 — сеять поодиночке.\n" +
                 "Камера смотрит на ферму издалека: одиночные травинки там читаются как шум, " +
                 "а пятна травы — как трава.")]
        [SerializeField, Min(1)] private int _clusterSize = 7;

        [Tooltip("Радиус куртины.")]
        [SerializeField, Min(0.1f)] private float _clusterRadius = 1.5f;

        [Header("Как")]
        [Tooltip("Один и тот же номер — одна и та же расстановка.")]
        [SerializeField] private int _seed = 20260802;

        [Tooltip("Случайный наклон, градусов. Идеально вертикальная трава выглядит расставленной.")]
        [SerializeField, Range(0f, 25f)] private float _tilt = 8f;

        [SerializeField] private float _groundY;

        private Transform _holder;

        /// <summary>Сколько кустиков реально встало. Меньше запрошенного, когда кончилось место.</summary>
        public int Placed { get; private set; }

        private void Start() => Rebuild();

        /// <summary>Стереть и рассыпать заново. Публичный — чтобы редактор уровня или загрузка могли перезапустить.</summary>
        public void Rebuild()
        {
            Clear();

            if (_entries == null || _entries.Length == 0 || _count <= 0) return;

            float totalWeight = 0f;
            foreach (var entry in _entries)
                if (entry != null && entry.Prefab != null) totalWeight += Mathf.Max(0f, entry.Weight);

            if (totalWeight <= 0f)
            {
                Debug.LogWarning("[Foliage] Нечего сеять — не задано ни одного префаба", this);
                return;
            }

            _holder = new GameObject("Foliage").transform;
            _holder.SetParent(transform, false);

            var random = new System.Random(_seed);
            var bounds = FarmBounds.Instance;
            Vector3 center = bounds != null ? bounds.Center : transform.position;
            float farmRadius = bounds != null ? bounds.UsableRadius : 0f;

            float inner = Mathf.Min(_innerRadius, _outerRadius);
            int attempts = _count * 4;   // потолок, чтобы плотная ферма не крутила цикл вечно

            while (Placed < _count && attempts-- > 0)
            {
                // Корень радиуса — иначе точки сгущаются к центру: площадь кольца растёт
                // как квадрат, а равномерный радиус этого не знает.
                float t = (float)random.NextDouble();
                float radius = Mathf.Sqrt(Mathf.Lerp(inner * inner, _outerRadius * _outerRadius, t));
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;

                var seed = center + new Vector3(Mathf.Cos(angle) * radius, _groundY, Mathf.Sin(angle) * radius);

                if (radius < farmRadius && random.NextDouble() > _insideFarmDensity) continue;
                if (!IsClear(seed)) continue;

                // Вид выбираем на куртину, а не на кустик: пятно из одной травы читается
                // как заросль, а пятно из вперемешку — снова как рябь.
                var entry = PickEntry(random, totalWeight);
                int size = _clusterSize <= 1 ? 1 : Mathf.Max(1, _clusterSize / 2 + random.Next(_clusterSize));

                for (int i = 0; i < size && Placed < _count; i++)
                {
                    Vector3 point = seed;
                    if (i > 0)
                    {
                        float spread = Mathf.Sqrt((float)random.NextDouble()) * _clusterRadius;
                        float around = (float)random.NextDouble() * Mathf.PI * 2f;
                        point += new Vector3(Mathf.Cos(around) * spread, 0f, Mathf.Sin(around) * spread);

                        if (!IsClear(point)) continue;
                    }

                    Place(entry, point, random);
                    Placed++;
                }
            }

            if (Placed < _count)
                Debug.Log("[Foliage] Размещено " + Placed + " из " + _count + " — не нашлось свободного места", this);
        }

        public void Clear()
        {
            if (_holder != null)
            {
                if (Application.isPlaying) Destroy(_holder.gameObject);
                else DestroyImmediate(_holder.gameObject);
            }

            _holder = null;
            Placed = 0;
        }

        private Entry PickEntry(System.Random random, float totalWeight)
        {
            float roll = (float)random.NextDouble() * totalWeight;

            foreach (var entry in _entries)
            {
                if (entry == null || entry.Prefab == null) continue;

                roll -= Mathf.Max(0f, entry.Weight);
                if (roll <= 0f) return entry;
            }

            return _entries[_entries.Length - 1];
        }

        private void Place(Entry entry, Vector3 point, System.Random random)
        {
            var rotation = Quaternion.Euler(
                ((float)random.NextDouble() * 2f - 1f) * _tilt,
                (float)random.NextDouble() * 360f,
                ((float)random.NextDouble() * 2f - 1f) * _tilt);

            var instance = Instantiate(entry.Prefab, point, rotation, _holder);
            instance.transform.localScale = Vector3.one *
                Mathf.Lerp(entry.ScaleMin, entry.ScaleMax, (float)random.NextDouble());

            // Декорация не участвует ни в перетаскивании, ни в слиянии, ни в физике.
            instance.isStatic = false;
        }

        /// <summary>Достаточно далеко от всего, что игрок поставил или с чем будет взаимодействовать.</summary>
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

        private void OnDrawGizmosSelected()
        {
            var bounds = FarmBounds.Instance;
            Vector3 center = bounds != null ? bounds.Center : transform.position;

            Gizmos.color = new Color(0.5f, 0.85f, 0.55f, 0.5f);
            DrawCircle(center + Vector3.up * _groundY, _outerRadius);

            if (_innerRadius > 0f)
            {
                Gizmos.color = new Color(0.85f, 0.6f, 0.4f, 0.5f);
                DrawCircle(center + Vector3.up * _groundY, _innerRadius);
            }
        }

        private static void DrawCircle(Vector3 center, float radius)
        {
            const int segments = 64;
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}

using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Забор по краю фермы, собранный кодом.
    /// <para>
    /// До уровней фермы кольцо стояло руками: 82 секции, разложенные по радиусу 13 раз и навсегда.
    /// Пока край не двигался, это было честно — но купленный уровень отодвигает землю, а забор,
    /// оставшийся на прежнем месте, превращает расширение из «ферма выросла» в «в арифметике стало
    /// больше». Граница обязана быть видна, иначе за неё нечего платить.
    /// </para>
    /// <para>
    /// Секции ставятся хордами по окружности: длина модели Kenney ровно 1 м, число секций — длина
    /// окружности, делённая на неё. Хорда чуть короче дуги, поэтому соседи слегка находят друг на
    /// друга — так и было в ручном кольце, и щелей на стыках это как раз не оставляет.
    /// </para>
    /// <para>
    /// Высота берётся из <see cref="FarmingRuntime.Ground"/>: земля идёт лёгкой волной и внутри
    /// фермы тоже, а забор, посаженный на постоянную <c>y</c>, местами уходил бы в грунт.
    /// Отсюда же требование к порядку — рельеф обязан выровнять площадку раньше, чем мы по ней
    /// сядем (см. <c>DefaultExecutionOrder</c> у <c>RollingGround</c>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Farm/Fence Ring")]
    public sealed class FenceRing : MonoBehaviour
    {
        [Header("Из чего")]
        [Tooltip("Обычная секция забора.")]
        [SerializeField] private GameObject _section;

        [Tooltip("Ворота. Пусто — кольцо будет глухим.")]
        [SerializeField] private GameObject _gate;

        [Header("Как")]
        [Tooltip("Длина секции вдоль забора, метров. У моделей Kenney ровно 1: по ней и считается " +
                 "число секций, так что число это не настройка, а следствие радиуса.")]
        [SerializeField, Min(0.05f)] private float _sectionLength = 1f;

        [Tooltip("Каждая N-я секция — ворота. 0 — без ворот вовсе.\n" +
                 "11 даёт восемь ворот на родной поляне: реже — забор читается как стена, чаще — как забор из одних калиток.")]
        [SerializeField, Min(0)] private int _gateEvery = 11;

        [Tooltip("По какому радиусу строить, если FarmBounds в сцене нет.")]
        [SerializeField, Min(1f)] private float _fallbackRadius = 13f;

        /// <summary>Сколько секций стоит сейчас. Наружу — чтобы проверку можно было написать числом.</summary>
        public int SectionCount { get; private set; }

        /// <summary>Радиус, по которому собрано текущее кольцо.</summary>
        public float BuiltRadius { get; private set; }

        private void OnEnable()
        {
            FarmBounds.RadiusChanged += Rebuild;
            Rebuild();
        }

        private void OnDisable()
        {
            FarmBounds.RadiusChanged -= Rebuild;
        }

        /// <summary>Собрать кольцо по текущему радиусу фермы.</summary>
        [ContextMenu("Перестроить забор")]
        public void Rebuild()
        {
            var bounds = FindBounds();
            Rebuild(bounds != null ? bounds.Radius : _fallbackRadius);
        }

        /// <summary>Собрать кольцо заданного радиуса. Старое сносится целиком — иначе прежний забор останется стоять внутри нового.</summary>
        public void Rebuild(float radius)
        {
            if (_section == null)
            {
                // Молчаливое «забора нет» читалось бы как потерянная сцена, а не как незаполненное поле.
                Debug.LogWarning("[Fence] Не задана секция забора — кольцо не собрано", this);
                return;
            }

            ClearSections();

            var bounds = FindBounds();
            Vector3 center = bounds != null ? bounds.Center : transform.position;

            float useRadius = Mathf.Max(1f, radius);
            int count = Mathf.Max(3, Mathf.RoundToInt(2f * Mathf.PI * useRadius / Mathf.Max(0.05f, _sectionLength)));
            float step = Mathf.PI * 2f / count;

            for (int i = 0; i < count; i++)
            {
                // Угол отсчитывается от +Z к +X — тогда поворот секции по оси Y численно равен
                // этому же углу, и ручное кольцо повторяется секция в секцию.
                float angle = i * step;
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                Vector3 point = center + outward * useRadius;
                point.y = FarmingRuntime.Ground.SampleHeight(point);

                bool isGate = _gate != null && _gateEvery > 0 && i % _gateEvery == 0;
                var prefab = isGate ? _gate : _section;

                var instance = Instantiate(prefab, transform);
                instance.transform.SetPositionAndRotation(point, Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f));
                instance.name = prefab.name + "_" + i;
            }

            SectionCount = count;
            BuiltRadius = useRadius;
        }

        private void ClearSections()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;

                // Destroy в игре срабатывает в конце кадра: гасим сразу, чтобы старое и новое
                // кольцо не простояли вместе даже один кадр.
                child.SetActive(false);

                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            SectionCount = 0;
        }

        /// <summary>
        /// Синглтон границ заполняется в Awake, а собирать забор может понадобиться и в редакторе,
        /// где Awake не звался, — тогда ищем по сцене.
        /// </summary>
        private static FarmBounds FindBounds()
        {
            var bounds = FarmBounds.Instance;
            if (bounds == null) bounds = FindFirstObjectByType<FarmBounds>();
            return bounds;
        }

        private void OnDrawGizmosSelected()
        {
            var bounds = FindBounds();
            Vector3 center = bounds != null ? bounds.Center : transform.position;
            float radius = bounds != null ? bounds.Radius : _fallbackRadius;

            Gizmos.color = new Color(0.85f, 0.7f, 0.45f, 0.9f);

            const int segments = 96;
            Vector3 prev = center + new Vector3(0f, 0f, radius);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}

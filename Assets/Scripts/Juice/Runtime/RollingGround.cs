using UnityEngine;
using Farm.Farming;

namespace Farm.Juice
{
    /// <summary>
    /// Земля с рельефом вместо ровной плоскости.
    /// <para>
    /// Плоскость выдаёт себя мгновенно: материал с шумом маскирует её вблизи, но силуэт на
    /// горизонте остаётся линейкой, и мир читается как декорация, положенная на стол.
    /// </para>
    /// <para>
    /// Ключевое ограничение — <b>внутри фермы земля обязана оставаться ровной</b>. Всё, что игра
    /// ставит и таскает, живёт на постоянной высоте: перетаскивание проецируется на плоскость,
    /// фермер и животные держат свою <c>y</c>, трава сеется на заданном уровне. Стоит поднять
    /// землю под грядкой — и грядка повиснет в воздухе или уйдёт в холм.
    /// </para>
    /// <para>
    /// Поэтому высота умножается на маску, которая равна нулю внутри рабочего радиуса и плавно
    /// нарастает наружу. Игровое поле остаётся честной плоскостью, а за забором мир идёт волнами —
    /// а именно он и занимает почти весь кадр.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Farm/Rolling Ground")]
    public sealed class RollingGround : MonoBehaviour, IGroundHeight
    {
        [Header("Размер")]
        [Tooltip("Сторона участка в мировых единицах. Масштаб объекта при этом должен быть 1.")]
        [SerializeField, Min(10f)] private float _size = 120f;

        [Tooltip("Сколько клеток по стороне. Больше — плавнее холмы и дороже меш.")]
        [SerializeField, Range(16, 300)] private int _resolution = 140;

        [Header("Ровная площадка")]
        [Tooltip("Радиус, внутри которого земля идеально плоская. Держи его не меньше рабочего " +
                 "радиуса FarmBounds, иначе грядки на краю фермы повиснут над склоном.")]
        [SerializeField, Min(0f)] private float _flatRadius = 13f;

        [Tooltip("За сколько единиц рельеф выходит на полную высоту после ровной площадки.")]
        [SerializeField, Min(1f)] private float _blend = 12f;

        [Header("Рельеф")]
        [Tooltip("Высота крупных холмов.")]
        [SerializeField, Min(0f)] private float _hillHeight = 2.6f;

        [Tooltip("Крупность холмов: меньше — шире и положе.")]
        [SerializeField, Min(0.001f)] private float _hillScale = 0.030f;

        [Tooltip("Высота мелкой неровности поверх холмов.")]
        [SerializeField, Min(0f)] private float _detailHeight = 0.45f;

        [SerializeField, Min(0.001f)] private float _detailScale = 0.12f;

        [Tooltip("Один и тот же номер — один и тот же рельеф.")]
        [SerializeField] private int _seed = 8041;

        [Tooltip("Обновлять и коллайдер. Нужен, только если по земле что-то стреляет лучами.")]
        [SerializeField] private bool _updateCollider = true;

        private Mesh _mesh;

        /// <summary>Высота земли в мировой точке. Пригодится, если что-то захочет лечь по рельефу.</summary>
        public float SampleHeight(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return Height(local.x, local.z);
        }

        private void OnEnable()
        {
            Rebuild();

            // Ядро кладёт вещи по земле через этот шов: ночной сбор, трава и всё, что появится
            // за пределами ровной площадки, иначе висело бы на постоянной высоте.
            FarmingRuntime.Ground = this;
        }

        private void OnDisable()
        {
            if (ReferenceEquals(FarmingRuntime.Ground, this)) FarmingRuntime.Ground = null;
        }

        private void OnValidate()
        {
            // Перестраиваем только у живого включённого объекта: OnValidate зовётся и при
            // импорте, когда трогать меш ещё нельзя.
            if (isActiveAndEnabled) Rebuild();
        }

        private void OnDestroy()
        {
            if (_mesh == null) return;

            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);

            _mesh = null;
        }

        /// <summary>Собрать меш заново. Публичный — чтобы можно было дёрнуть из редактора.</summary>
        [ContextMenu("Перестроить рельеф")]
        public void Rebuild()
        {
            int side = Mathf.Max(2, _resolution) + 1;
            int count = side * side;

            var vertices = new Vector3[count];
            var uv = new Vector2[count];
            var triangles = new int[(side - 1) * (side - 1) * 6];

            float step = _size / (side - 1);
            float half = _size * 0.5f;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;

                    float px = x * step - half;
                    float pz = z * step - half;

                    vertices[i] = new Vector3(px, Height(px, pz), pz);

                    // Те же 0..1 по всему участку, что у примитива Plane: настройки тайлинга
                    // в материале остаются рабочими, и земля выглядит как раньше.
                    uv[i] = new Vector2(x / (float)(side - 1), z / (float)(side - 1));
                }
            }

            int t = 0;
            for (int z = 0; z < side - 1; z++)
            {
                for (int x = 0; x < side - 1; x++)
                {
                    int i = z * side + x;

                    triangles[t++] = i;
                    triangles[t++] = i + side;
                    triangles[t++] = i + 1;

                    triangles[t++] = i + 1;
                    triangles[t++] = i + side;
                    triangles[t++] = i + side + 1;
                }
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "RollingGround" };
                // Не сохраняем в сцену: меш собирается заново при каждой загрузке и иначе
                // просто раздувал бы файл сцены на мегабайты.
                _mesh.hideFlags = HideFlags.DontSave;
            }

            _mesh.Clear();
            _mesh.indexFormat = count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            _mesh.vertices = vertices;
            _mesh.uv = uv;
            _mesh.triangles = triangles;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = _mesh;

            var collider = GetComponent<MeshCollider>();
            if (collider != null) collider.sharedMesh = _updateCollider ? _mesh : null;
        }

        /// <summary>Высота в точке участка. Ноль внутри ровной площадки.</summary>
        private float Height(float x, float z)
        {
            float distance = Mathf.Sqrt(x * x + z * z);
            if (distance <= _flatRadius) return 0f;

            // Плавная ступень, а не линейная: у линейной на границе площадки виден излом.
            float k = Mathf.Clamp01((distance - _flatRadius) / _blend);
            float mask = k * k * (3f - 2f * k);

            float offset = _seed * 0.137f;

            float hills = Signed(Mathf.PerlinNoise(x * _hillScale + offset, z * _hillScale + offset));
            float detail = Signed(Mathf.PerlinNoise(x * _detailScale + offset * 3f,
                                                    z * _detailScale + offset * 3f));

            return (hills * _hillHeight + detail * _detailHeight) * mask;
        }

        /// <summary>Шум Unity даёт 0..1, а холмам нужно уходить и вниз.</summary>
        private static float Signed(float noise01) => noise01 * 2f - 1f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.85f, 0.5f, 0.8f);
            DrawCircle(transform.position, _flatRadius);

            Gizmos.color = new Color(0.9f, 0.75f, 0.3f, 0.5f);
            DrawCircle(transform.position, _flatRadius + _blend);
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

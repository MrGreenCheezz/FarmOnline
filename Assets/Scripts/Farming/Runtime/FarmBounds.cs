using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Игровая территория. Всё, что игрок ставит или перетаскивает, удерживается внутри неё.
    /// <para>
    /// Мягкое правило в коде, а не коллайдеры: перетаскивание проецируется на плоскость
    /// земли, у которой нет краёв, — без этого грядку можно швырнуть за горизонт, где её
    /// не достать, а фермер радостно уйдёт туда за ней пешком.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    [AddComponentMenu("Farm/Farm Bounds")]
    public sealed class FarmBounds : MonoBehaviour
    {
        [Tooltip("Радиус игровой территории от центра этого объекта.")]
        [SerializeField, Min(1f)] private float _radius = 13f;

        [Tooltip("Насколько ближе к центру держать объекты, чтобы они не влезали в забор.")]
        [SerializeField, Min(0f)] private float _margin = 0.8f;

        public static FarmBounds Instance { get; private set; }

        public Vector3 Center => transform.position;
        public float Radius => _radius;
        public float UsableRadius => Mathf.Max(0.5f, _radius - _margin);

        private void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 delta = worldPosition - Center;
            delta.y = 0f;
            return delta.sqrMagnitude <= UsableRadius * UsableRadius;
        }

        /// <summary>Ближайшая точка внутри фермы. Высота не трогается.</summary>
        public Vector3 Clamp(Vector3 worldPosition)
        {
            Vector3 delta = worldPosition - Center;
            float y = worldPosition.y;
            delta.y = 0f;

            float radius = UsableRadius;
            if (delta.sqrMagnitude <= radius * radius) return worldPosition;

            Vector3 clamped = Center + delta.normalized * radius;
            clamped.y = y;
            return clamped;
        }

        /// <summary>Зажать через синглтон; если его нет — вернуть точку как есть.</summary>
        public static Vector3 ClampToFarm(Vector3 worldPosition) =>
            Instance != null ? Instance.Clamp(worldPosition) : worldPosition;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.75f, 0.3f, 0.9f);
            DrawCircle(Center, _radius);
            Gizmos.color = new Color(0.4f, 0.85f, 0.5f, 0.6f);
            DrawCircle(Center, UsableRadius);
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

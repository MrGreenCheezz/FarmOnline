using UnityEngine;
using Farm.Farming;

namespace Farm.Juice
{
    /// <summary>
    /// Кольцо радиуса действия постройки. Показывается, пока постройка выбрана кликом или
    /// пока её несут: решение «куда поставить» невозможно принять, не видя, куда достаёт.
    /// <para>
    /// Одно кольцо на ферму, а не по компоненту на постройку: одновременно игрок думает об
    /// одной, а спавнить LineRenderer в каждый купленный ветряк — плата за ничего.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Juice/Aura Ring")]
    public sealed class AuraRing : MonoBehaviour
    {
        /// <summary>Материал кольца в Resources — см. <see cref="Material"/>.</summary>
        private const string MaterialResource = "M_AuraRing";

        [Tooltip("Чем рисовать кольцо. Пусто — возьмётся Resources/M_AuraRing.")]
        [SerializeField] private Material _material;

        [Tooltip("Точек в окружности. Больше — глаже; кольцо одно, можно не экономить.")]
        [SerializeField, Range(16, 128)] private int _segments = 64;

        [SerializeField] private Color _color = new Color(0.55f, 0.9f, 0.6f, 0.85f);

        [SerializeField, Min(0.005f)] private float _width = 0.07f;

        [Tooltip("Насколько над рельефом вести линию, чтобы не тонуть в траве.")]
        [SerializeField, Min(0f)] private float _liftAboveGround = 0.12f;

        private LineRenderer _line;

        private void OnEnable()
        {
            var go = new GameObject("[AuraRing]") { hideFlags = HideFlags.HideInHierarchy };
            go.transform.SetParent(transform, false);

            _line = go.AddComponent<LineRenderer>();
            _line.loop = true;
            _line.useWorldSpace = true;
            _line.widthMultiplier = _width;

            var material = Material();
            if (material != null)
            {
                // Свой экземпляр: цвет кольца не должен править общий ассет.
                _line.material = new Material(material) { color = _color };
            }

            _line.startColor = _color;
            _line.endColor = _color;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.enabled = false;
        }

        /// <summary>
        /// Материал кольца. Берётся из поля или из Resources — но <b>никогда</b> через
        /// <c>Shader.Find</c>.
        /// <para>
        /// В редакторе доступны все шейдеры проекта, а в сборку попадают только те, на
        /// которые кто-то ссылается ассетом. Кольцо создаётся кодом, ссылки нет — и в билде
        /// <c>Shader.Find</c> молча возвращает null, а конструктор материала падает
        /// <c>ArgumentNullException</c> при первом же выделении постройки. В редакторе
        /// такую ошибку не увидеть ни разу.
        /// </para>
        /// </summary>
        private Material Material()
        {
            if (_material != null) return _material;

            _material = Resources.Load<Material>(MaterialResource);
            if (_material == null)
                Debug.LogError("[Juice] Нет Resources/" + MaterialResource + " — кольцо ауры не нарисуется", this);

            return _material;
        }

        private void OnDisable()
        {
            if (_line != null) Destroy(_line.gameObject);
            _line = null;
        }

        private void Update()
        {
            if (_line == null) return;

            var building = PickBuilding();
            float radius = building != null && building.Definition != null
                ? building.Definition.AuraRadiusAt(building.Level)
                : 0f;

            // Глобальным (радиус 0) кольцо не рисуем: круг на всю ферму — это не информация.
            if (building == null || radius <= 0f)
            {
                _line.enabled = false;
                return;
            }

            _line.enabled = true;
            DrawCircle(building.transform.position, radius);
        }

        /// <summary>Чьё кольцо показывать: несомое в руке важнее выбранного кликом.</summary>
        private static Building PickBuilding()
        {
            var dragged = DragFocus.Current;
            if (dragged != null)
            {
                var carried = dragged.GetComponent<Building>();
                if (carried != null) return carried;
            }

            return BuildingSelection.Current;
        }

        private void DrawCircle(Vector3 center, float radius)
        {
            _line.positionCount = _segments;

            for (int i = 0; i < _segments; i++)
            {
                float angle = i / (float)_segments * Mathf.PI * 2f;
                var point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                // По рельефу точка за точкой — кольцо ложится на холмы, а не режет их.
                point.y = FarmingRuntime.Ground.SampleHeight(point) + _liftAboveGround;
                _line.SetPosition(i, point);
            }
        }
    }
}

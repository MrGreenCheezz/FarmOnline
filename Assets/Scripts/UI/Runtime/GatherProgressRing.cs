using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Кольцо прогресса над узлом, который игрок держит рукой.
    /// <para>
    /// Режим удержания копит прогресс на самом узле, но до этого индикатора игрок держал кнопку
    /// вслепую: сколько ещё осталось, не показывал никто. Ожидание без обратной связи читается
    /// как «не работает» — и кнопку отпускают ровно перед тем, как единица бы выпала.
    /// </para>
    /// <para>
    /// Рисуется дугой через <see cref="Painter2D"/>, а не картинкой: кольцо должно быть чётким
    /// на любом разрешении, а спрайт под это пришлось бы держать в нескольких размерах.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Gather Progress Ring")]
    public sealed class GatherProgressRing : MonoBehaviour
    {
        [SerializeField] private Camera _camera;

        [Tooltip("На какой высоте над узлом висит кольцо, в мировых единицах.")]
        [SerializeField] private float _worldHeight = 0.55f;

        [Tooltip("Диаметр кольца в пикселях при высоте окна 1080; масштабируется под другие.")]
        [SerializeField, Min(16f)] private float _diameter = 52f;

        [Tooltip("Толщина линии в тех же пикселях.")]
        [SerializeField, Min(1f)] private float _thickness = 5f;

        [Tooltip("Цвет пустой дорожки.")]
        [SerializeField] private Color _trackColor = new Color(0f, 0f, 0f, 0.35f);

        [Tooltip("Цвет заполнения.")]
        [SerializeField] private Color _fillColor = new Color(0.75f, 0.92f, 1f, 0.95f);

        private VisualElement _layer;
        private VisualElement _ring;
        private float _progress;
        private float _scale = 1f;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            _layer = root?.Q<VisualElement>("gather-ring-layer");

            if (_camera == null) _camera = Camera.main;
            if (_layer == null) { enabled = false; return; }

            _ring = new VisualElement { name = "gather-ring" };
            _ring.pickingMode = PickingMode.Ignore;
            _ring.style.position = Position.Absolute;
            _ring.style.display = DisplayStyle.None;
            _ring.generateVisualContent += Draw;
            _layer.Add(_ring);
        }

        private void OnDisable()
        {
            if (_ring == null) return;

            _ring.generateVisualContent -= Draw;
            _ring.RemoveFromHierarchy();
            _ring = null;
        }

        // LateUpdate: узел к этому моменту уже сдвинут покачиванием, кольцо не отстаёт на кадр.
        private void LateUpdate()
        {
            if (_ring == null || _camera == null) return;

            var node = GatherFocus.Current;
            var panel = _layer.panel;

            if (node == null || panel == null)
            {
                Hide();
                return;
            }

            Vector3 world = node.transform.position + Vector3.up * (node.AimHeight + _worldHeight);

            // Позади камеры рисовать нечего — иначе кольцо «отразится» на другую сторону экрана.
            if (_camera.WorldToViewportPoint(world).z <= 0f) { Hide(); return; }

            _scale = Screen.height > 0 ? Mathf.Max(0.5f, Screen.height / 1080f) : 1f;
            float size = _diameter * _scale;

            Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, _camera);

            _ring.style.display = DisplayStyle.Flex;
            _ring.style.width = size;
            _ring.style.height = size;
            _ring.style.left = point.x - size * 0.5f;
            _ring.style.top = point.y - size * 0.5f;

            _progress = Mathf.Clamp01(node.Progress01);
            _ring.MarkDirtyRepaint();
        }

        private void Hide()
        {
            if (_ring != null) _ring.style.display = DisplayStyle.None;
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;

            float size = _diameter * _scale;
            float thickness = _thickness * _scale;
            float radius = (size - thickness) * 0.5f;
            if (radius <= 0f) return;

            var center = new Vector2(size * 0.5f, size * 0.5f);

            painter.lineWidth = thickness;
            painter.lineCap = LineCap.Round;

            // Пустая дорожка целиком: игрок должен видеть, сколько всего, а не только сделанное.
            painter.strokeColor = _trackColor;
            painter.BeginPath();
            painter.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.Stroke();

            if (_progress <= 0.001f) return;

            // От двенадцати часов по часовой стрелке — так читают любой круговой прогресс.
            painter.strokeColor = _fillColor;
            painter.BeginPath();
            painter.Arc(center, radius,
                        new Angle(-90f, AngleUnit.Degree),
                        new Angle(-90f + 360f * _progress, AngleUnit.Degree));
            painter.Stroke();
        }
    }
}

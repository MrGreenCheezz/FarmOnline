using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Вешает плашку уровня над каждой грядкой — игрок с одного взгляда видит, что с чем сливается.
    /// <para>
    /// Один слой с пулом меток вместо world-space канваса на каждую грядку: плашки должны быть
    /// выровнены по экрану и читаться под любым углом камеры, а канвас на грядку добавил бы
    /// рендерер и draw call тому, что всегда было двумя символами текста.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Plot Level Badges")]
    public sealed class PlotLevelBadges : MonoBehaviour
    {
        [SerializeField] private Camera _camera;

        [Tooltip("На какой высоте над грядкой висит плашка.")]
        [SerializeField] private float _worldHeight = 1.15f;

        [Tooltip("Дальше этого расстояния от камеры плашки не рисуются.")]
        [SerializeField, Min(1f)] private float _maxDistance = 45f;

        [Tooltip("Показывать плашки. Переключается кнопкой в HUD.")]
        [SerializeField] private bool _visible = true;

        private VisualElement _layer;
        private readonly List<Label> _pool = new List<Label>();

        /// <summary>
        /// Видны ли плашки. Выключенные не просто прячутся — вся покадровая работа
        /// пропускается: на большой ферме это десятки проекций в кадр впустую.
        /// </summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (!_visible) HideFrom(0);
            }
        }

        public void Toggle() => Visible = !_visible;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            _layer = root?.Q<VisualElement>("world-badges");

            if (_camera == null) _camera = Camera.main;
            if (_layer == null) enabled = false;
        }

        private void OnDisable() => HideFrom(0);

        // LateUpdate: грядки к этому моменту уже сдвинуты перетаскиванием, плашки не отстают на кадр.
        private void LateUpdate()
        {
            if (!_visible || _camera == null || _layer == null) return;

            var panel = _layer.panel;
            if (panel == null) return;

            var plots = GrowableRegistry.All;
            int shown = 0;
            float maxSqr = _maxDistance * _maxDistance;
            Vector3 camPos = _camera.transform.position;

            for (int i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                if (plot == null) continue;

                // Пустая грядка ничего не выращивает — уровень над ней бессмысленен.
                if (plot.Phase == GrowthPhase.Empty) continue;

                Vector3 world = plot.transform.position + Vector3.up * _worldHeight;
                if ((world - camPos).sqrMagnitude > maxSqr) continue;

                // Позади камеры рисовать нечего — иначе плашка «отразится» на другую сторону экрана.
                if (_camera.WorldToViewportPoint(world).z <= 0f) continue;

                var badge = GetBadge(shown);
                Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, _camera);

                badge.style.left = point.x - badge.resolvedStyle.width * 0.5f;
                badge.style.top = point.y - badge.resolvedStyle.height;

                int level = plot.Level;
                badge.text = level.ToString();
                ApplyLevelClass(badge, level);

                // Готовность подсвечиваем фоном, а не рамкой — рамка держит цвет уровня,
                // иначе, увидев «готово», игрок терял бы информацию о том, с чем это сливать.
                badge.EnableInClassList("badge--ready", plot.IsReady);

                shown++;
            }

            HideFrom(shown);
        }

        private Label GetBadge(int index)
        {
            while (_pool.Count <= index)
            {
                var created = new Label();
                created.AddToClassList("badge");
                created.pickingMode = PickingMode.Ignore;
                _layer.Add(created);
                _pool.Add(created);
            }

            var badge = _pool[index];
            badge.style.display = DisplayStyle.Flex;
            return badge;
        }

        private void HideFrom(int index)
        {
            for (int i = index; i < _pool.Count; i++) _pool[i].style.display = DisplayStyle.None;
        }

        private static void ApplyLevelClass(Label badge, int level)
        {
            int tier = Mathf.Clamp(level, 1, 5);
            for (int i = 1; i <= 5; i++) badge.EnableInClassList("badge--lvl" + i, i == tier);
        }
    }
}

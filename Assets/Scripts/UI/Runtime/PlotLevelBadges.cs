using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Floats a level badge over every plot so the player can see at a glance what merges with what.
    /// <para>
    /// One panel with pooled labels rather than a world-space canvas per plot: the badges must stay
    /// screen-aligned and readable at any camera angle, and a per-plot canvas would add a renderer
    /// and a draw call to something that is only ever two characters of text.
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

        private VisualElement _layer;
        private readonly List<Label> _pool = new List<Label>();

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
            if (_camera == null || _layer == null) return;

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

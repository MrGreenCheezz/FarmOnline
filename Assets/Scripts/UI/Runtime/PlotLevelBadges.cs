using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Вешает плашку над каждой грядкой: чем она занята и какого уровня — игрок с одного взгляда
    /// видит и что с чем сливается, и что вообще растёт.
    /// <para>
    /// Один слой с пулом плашек вместо world-space канваса на каждую грядку: плашки должны быть
    /// выровнены по экрану и читаться под любым углом камеры, а канвас на грядку добавил бы
    /// рендерер и draw call тому, что всегда было парой символов.
    /// </para>
    /// <para>
    /// Иконка появилась не для красоты. Восемь пород дерева стоят на ферме одной моделью,
    /// различаясь приглушённым оттенком, и одна цифра уровня над ними не отвечала на главный
    /// вопрос — <i>что это</i>. Иконка ресурса берётся та же, что в инвентаре, поэтому цвет
    /// фишки связывает грядку в мире со строкой на складе без единого слова.
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

        /// <summary>Одна плашка: строка из иконки ресурса и цифры уровня.</summary>
        private sealed class Badge
        {
            public VisualElement Root;
            public VisualElement Icon;
            public Label Level;
        }

        private VisualElement _layer;
        private readonly List<Badge> _pool = new List<Badge>();

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

                badge.Root.style.left = point.x - badge.Root.resolvedStyle.width * 0.5f;
                badge.Root.style.top = point.y - badge.Root.resolvedStyle.height;

                int level = plot.Level;
                badge.Level.text = level.ToString();
                ApplyLevelClass(badge.Root, level);

                // Иконка есть не у всего: у жилы или дерева без назначенного ресурса плашка
                // просто остаётся цифрой, а не показывает пустой квадрат.
                var icon = IconOf(plot);
                badge.Icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : StyleKeyword.None;
                badge.Icon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;

                // Готовность подсвечиваем фоном, а не рамкой — рамка держит цвет уровня,
                // иначе, увидев «готово», игрок терял бы информацию о том, с чем это сливать.
                badge.Root.EnableInClassList("badge--ready", plot.IsReady);

                shown++;
            }

            HideFrom(shown);
        }

        /// <summary>Иконка того, что грядка производит. Null — показывать нечего.</summary>
        private static Sprite IconOf(Growable plot)
        {
            var resource = plot.Definition != null ? plot.Definition.YieldResource : null;
            return resource != null ? resource.Icon : null;
        }

        private Badge GetBadge(int index)
        {
            while (_pool.Count <= index)
            {
                var root = new VisualElement();
                root.AddToClassList("badge");
                root.pickingMode = PickingMode.Ignore;

                var icon = new VisualElement();
                icon.AddToClassList("badge__icon");
                icon.pickingMode = PickingMode.Ignore;
                root.Add(icon);

                var level = new Label();
                level.AddToClassList("badge__level");
                level.pickingMode = PickingMode.Ignore;
                root.Add(level);

                _layer.Add(root);
                _pool.Add(new Badge { Root = root, Icon = icon, Level = level });
            }

            var badge = _pool[index];
            badge.Root.style.display = DisplayStyle.Flex;
            return badge;
        }

        private void HideFrom(int index)
        {
            for (int i = index; i < _pool.Count; i++) _pool[i].Root.style.display = DisplayStyle.None;
        }

        private static void ApplyLevelClass(VisualElement badge, int level)
        {
            int tier = Mathf.Clamp(level, 1, 5);
            for (int i = 1; i <= 5; i++) badge.EnableInClassList("badge--lvl" + i, i == tier);
        }
    }
}

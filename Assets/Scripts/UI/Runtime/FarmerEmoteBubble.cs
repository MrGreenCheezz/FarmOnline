using UnityEngine;
using UnityEngine.UIElements;
using Farm.Characters;

namespace Farm.UI
{
    /// <summary>
    /// Редкое облачко над фермером с одним символом: хлеб, кружка, «Zzz», нота.
    /// <para>
    /// Здесь раньше висел текст с тем, что фермер решил. Он читался как избыток: над головой
    /// постоянно что-то написано, и очень быстро это перестаёт замечаться. Символ работает
    /// иначе — он ловится боковым зрением, не требует чтения и потому может быть редким.
    /// Что именно фермер задумал, осталось видно в панели HUD, где этому и место.
    /// </para>
    /// <para>
    /// Предметы показываются иконками из тех же наборов, что и вся ферма, а отвлечённые знаки
    /// (нота, «Zzz») рисуются вектором: рисовать хлеб линиями некрасиво, а искать в ассетах
    /// ноту — незачем.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Farmer Emote Bubble")]
    public sealed class FarmerEmoteBubble : MonoBehaviour
    {
        [Tooltip("За кем следить. Пусто — найдётся первый фермер в сцене.")]
        [SerializeField] private FarmerAgent _farmer;

        [SerializeField] private Camera _camera;

        [Header("Символы")]
        [Tooltip("Хочет есть.")]
        [SerializeField] private Sprite _foodIcon;

        [Tooltip("Хочет пить.")]
        [SerializeField] private Sprite _drinkIcon;

        [Header("Вид")]
        [Tooltip("На какой высоте над персонажем висит облачко, в мировых единицах.")]
        [SerializeField, Min(0f)] private float _worldHeight = 2.05f;

        [Tooltip("Сколько секунд держится.")]
        [SerializeField, Min(0.5f)] private float _life = 2.8f;

        [Tooltip("На сколько всплывает за это время, в мировых единицах.")]
        [SerializeField, Min(0f)] private float _rise = 0.35f;

        [Tooltip("Размер облачка в пикселях при высоте окна 1080.")]
        [SerializeField, Min(16f)] private float _size = 44f;

        [Tooltip("Дальше этого расстояния от камеры облачко не рисуется.")]
        [SerializeField, Min(1f)] private float _maxDistance = 45f;

        [SerializeField] private Color _symbolColor = new Color(0.96f, 0.97f, 1f, 1f);

        private VisualElement _layer;
        private VisualElement _bubble;
        private VisualElement _icon;
        private VisualElement _symbol;

        private FarmerEmote _current = FarmerEmote.None;
        private float _age;
        private float _scale = 1f;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            _layer = root?.Q<VisualElement>("emote-layer");

            if (_camera == null) _camera = Camera.main;
            if (_farmer == null) _farmer = FindFirstObjectByType<FarmerAgent>();
            if (_layer == null) { enabled = false; return; }

            Build();

            if (_farmer != null) _farmer.Emoted += OnEmoted;
        }

        private void OnDisable()
        {
            if (_farmer != null) _farmer.Emoted -= OnEmoted;

            if (_bubble == null) return;
            _symbol.generateVisualContent -= DrawSymbol;
            _bubble.RemoveFromHierarchy();
            _bubble = null;
        }

        private void Build()
        {
            _bubble = new VisualElement { name = "farmer-emote" };
            _bubble.AddToClassList("emote");
            _bubble.pickingMode = PickingMode.Ignore;
            _bubble.style.display = DisplayStyle.None;

            _icon = new VisualElement { pickingMode = PickingMode.Ignore };
            _icon.AddToClassList("emote__icon");
            _bubble.Add(_icon);

            _symbol = new VisualElement { pickingMode = PickingMode.Ignore };
            _symbol.AddToClassList("emote__icon");
            _symbol.generateVisualContent += DrawSymbol;
            _bubble.Add(_symbol);

            _layer.Add(_bubble);
        }

        private void OnEmoted(FarmerAgent agent, FarmerEmote emote)
        {
            if (emote == FarmerEmote.None) return;

            _current = emote;
            _age = 0f;

            var sprite = SpriteFor(emote);
            bool drawn = sprite == null;

            _icon.style.display = drawn ? DisplayStyle.None : DisplayStyle.Flex;
            _symbol.style.display = drawn ? DisplayStyle.Flex : DisplayStyle.None;

            if (!drawn) _icon.style.backgroundImage = new StyleBackground(sprite);
            else _symbol.MarkDirtyRepaint();
        }

        private Sprite SpriteFor(FarmerEmote emote)
        {
            switch (emote)
            {
                case FarmerEmote.Food: return _foodIcon;
                case FarmerEmote.Drink: return _drinkIcon;
                default: return null;   // «Zzz» и нота рисуются вектором
            }
        }

        // LateUpdate: персонаж к этому моменту уже сдвинут, облачко не отстаёт на кадр.
        private void LateUpdate()
        {
            if (_bubble == null || _camera == null || _farmer == null) return;
            if (_current == FarmerEmote.None) return;

            _age += Time.deltaTime;
            if (_age >= _life)
            {
                _current = FarmerEmote.None;
                _bubble.style.display = DisplayStyle.None;
                return;
            }

            var panel = _layer.panel;
            if (panel == null) return;

            float k = _age / _life;
            Vector3 world = _farmer.transform.position
                          + Vector3.up * (_worldHeight + _rise * Rise01(k));

            if ((world - _camera.transform.position).sqrMagnitude > _maxDistance * _maxDistance ||
                _camera.WorldToViewportPoint(world).z <= 0f)
            {
                _bubble.style.display = DisplayStyle.None;
                return;
            }

            _scale = Screen.height > 0 ? Mathf.Max(0.5f, Screen.height / 1080f) : 1f;
            float size = _size * _scale;

            Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, _camera);

            _bubble.style.display = DisplayStyle.Flex;
            _bubble.style.width = size;
            _bubble.style.height = size;
            _bubble.style.left = point.x - size * 0.5f;
            _bubble.style.top = point.y - size;

            // Появляется рывком, тает плавно — так его замечают, но оно не мозолит глаз.
            float fade = k < 0.12f ? k / 0.12f : Mathf.Clamp01((1f - k) / 0.35f);
            _bubble.style.opacity = fade;

            // Размер облачка зависит от окна, поэтому рисованный знак надо обновлять вместе с ним.
            if (_symbol.style.display == DisplayStyle.Flex) _symbol.MarkDirtyRepaint();
        }

        /// <summary>Быстро вверх, потом дрейф — так читается всплывающее.</summary>
        private static float Rise01(float k) => 1f - (1f - k) * (1f - k);

        // ---- рисованные знаки ----

        private void DrawSymbol(MeshGenerationContext context)
        {
            // По фактическому размеру элемента, а не по расчётному: элемент занимает долю
            // облачка, и рисование в «пикселях облачка» вылезало бы за его края.
            var rect = context.visualElement.contentRect;
            float size = Mathf.Min(rect.width, rect.height);
            if (size <= 1f) return;

            var painter = context.painter2D;
            painter.strokeColor = _symbolColor;
            painter.fillColor = _symbolColor;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            if (_current == FarmerEmote.Sleep) DrawSleep(painter, size);
            else DrawNote(painter, size);
        }

        /// <summary>Три «z» лесенкой вверх — общепонятный знак сна.</summary>
        private static void DrawSleep(Painter2D painter, float size)
        {
            painter.lineWidth = Mathf.Max(1.5f, size * 0.055f);

            // Каждая следующая буква меньше и выше: получается «улетающий» ряд.
            DrawZ(painter, size * 0.16f, size * 0.62f, size * 0.30f);
            DrawZ(painter, size * 0.46f, size * 0.36f, size * 0.22f);
            DrawZ(painter, size * 0.68f, size * 0.16f, size * 0.15f);
        }

        private static void DrawZ(Painter2D painter, float x, float y, float side)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + side, y));
            painter.LineTo(new Vector2(x, y + side));
            painter.LineTo(new Vector2(x + side, y + side));
            painter.Stroke();
        }

        /// <summary>Нота: головка, стойка и флажок. Знак «человеку хорошо».</summary>
        private static void DrawNote(Painter2D painter, float size)
        {
            float headRadius = size * 0.15f;
            var head = new Vector2(size * 0.36f, size * 0.68f);
            float stemTop = size * 0.22f;
            float stemX = head.x + headRadius;

            painter.BeginPath();
            painter.Arc(head, headRadius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.Fill();

            painter.lineWidth = Mathf.Max(1.5f, size * 0.06f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(stemX, head.y));
            painter.LineTo(new Vector2(stemX, stemTop));
            painter.Stroke();

            // Флажок — короткая дуга вправо-вниз от верхушки стойки.
            painter.BeginPath();
            painter.MoveTo(new Vector2(stemX, stemTop));
            painter.BezierCurveTo(
                new Vector2(stemX + size * 0.22f, stemTop + size * 0.05f),
                new Vector2(stemX + size * 0.20f, stemTop + size * 0.16f),
                new Vector2(stemX + size * 0.10f, stemTop + size * 0.26f));
            painter.Stroke();
        }
    }
}

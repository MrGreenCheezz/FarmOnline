using UnityEngine;
using UnityEngine.UIElements;
using Farm.Characters;

namespace Farm.UI
{
    /// <summary>
    /// Пузырь над головой фермера с тем, что он решил, и как себя чувствует.
    /// <para>
    /// Это вторая половина «живости», без которой первая не работает. Умный выбор, сделанный
    /// молча и мгновенно, неотличим от случайного: игрок видит, что персонаж куда-то пошёл, но
    /// не видит, что он что-то решил. Пузырь показывает сам выбор, а короткая пауза в
    /// <see cref="FarmerAgent"/> даёт время его прочитать.
    /// </para>
    /// <para>
    /// Мысль держится на экране дольше, чем длится пауза: решения сменяются быстро, и пузырь,
    /// живущий ровно столько же, превратился бы в мельтешение.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Farmer Thought Bubble")]
    public sealed class FarmerThoughtBubble : MonoBehaviour
    {
        [Tooltip("За кем следить. Пусто — найдётся первый фермер в сцене.")]
        [SerializeField] private FarmerAgent _farmer;

        [SerializeField] private Camera _camera;

        [Tooltip("На какой высоте над персонажем висит пузырь, в мировых единицах.")]
        [SerializeField, Min(0f)] private float _worldHeight = 2.1f;

        [Tooltip("Сколько секунд держать мысль на экране.")]
        [SerializeField, Min(0.5f)] private float _life = 3.5f;

        [Tooltip("Дальше этого расстояния от камеры пузырь не рисуется.")]
        [SerializeField, Min(1f)] private float _maxDistance = 40f;

        [Tooltip("Показывать самочувствие второй строкой.")]
        [SerializeField] private bool _showMood = true;

        private VisualElement _layer;
        private VisualElement _bubble;
        private Label _thought;
        private Label _mood;
        private float _age;
        private bool _visible;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            _layer = root?.Q<VisualElement>("thought-layer");

            if (_camera == null) _camera = Camera.main;
            if (_farmer == null) _farmer = FindFirstObjectByType<FarmerAgent>();
            if (_layer == null) { enabled = false; return; }

            Build();

            if (_farmer != null) _farmer.ThoughtChanged += OnThoughtChanged;
        }

        private void OnDisable()
        {
            if (_farmer != null) _farmer.ThoughtChanged -= OnThoughtChanged;

            if (_bubble == null) return;
            _bubble.RemoveFromHierarchy();
            _bubble = null;
        }

        private void Build()
        {
            _bubble = new VisualElement { name = "farmer-thought" };
            _bubble.AddToClassList("thought");
            _bubble.pickingMode = PickingMode.Ignore;
            _bubble.style.display = DisplayStyle.None;

            _thought = new Label { pickingMode = PickingMode.Ignore };
            _bubble.Add(_thought);

            _mood = new Label { pickingMode = PickingMode.Ignore };
            _mood.AddToClassList("thought__mood");
            _bubble.Add(_mood);

            _layer.Add(_bubble);
        }

        private void OnThoughtChanged(FarmerAgent agent, string thought)
        {
            if (string.IsNullOrEmpty(thought)) return;

            _thought.text = thought;
            _age = 0f;
            _visible = true;
        }

        // LateUpdate: персонаж к этому моменту уже сдвинут, пузырь не отстаёт на кадр.
        private void LateUpdate()
        {
            if (_bubble == null || _camera == null || _farmer == null) return;

            if (!_visible) { _bubble.style.display = DisplayStyle.None; return; }

            _age += Time.deltaTime;
            if (_age >= _life)
            {
                _visible = false;
                _bubble.style.display = DisplayStyle.None;
                return;
            }

            var panel = _layer.panel;
            if (panel == null) return;

            Vector3 world = _farmer.transform.position + Vector3.up * _worldHeight;

            if ((world - _camera.transform.position).sqrMagnitude > _maxDistance * _maxDistance ||
                _camera.WorldToViewportPoint(world).z <= 0f)
            {
                _bubble.style.display = DisplayStyle.None;
                return;
            }

            if (_showMood)
            {
                _mood.text = _farmer.Mood;
                _mood.style.display = string.IsNullOrEmpty(_mood.text) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            else
            {
                _mood.style.display = DisplayStyle.None;
            }

            Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, _camera);

            _bubble.style.display = DisplayStyle.Flex;
            _bubble.style.left = point.x - _bubble.resolvedStyle.width * 0.5f;
            _bubble.style.top = point.y - _bubble.resolvedStyle.height;

            // Последнюю треть жизни растворяем — резкое исчезновение читается как сбой.
            float fade = Mathf.Clamp01((_life - _age) / (_life * 0.33f));
            _bubble.style.opacity = fade;
        }
    }
}

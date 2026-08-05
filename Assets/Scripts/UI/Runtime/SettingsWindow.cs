using UnityEngine;
using UnityEngine.UIElements;
using Farm.Juice;

namespace Farm.UI
{
    /// <summary>
    /// Настройки звука. Значения применяются по ходу движения ползунка — панель, которая
    /// срабатывает только по «применить», заставляет игрока угадывать, что он выбирает.
    /// <para>
    /// Сохранение живёт в <see cref="Sfx"/>, а не здесь: настройка обязана пережить закрытие
    /// окна, перезагрузку сцены и полную замену интерфейса. Оттуда же её видит и главное меню —
    /// это одна настройка на игру, а не две одинаковые в разных местах.
    /// </para>
    /// <para>
    /// Имена элементов вынесены в поля, чтобы то же окно работало и в меню, где своя разметка.
    /// Дублировать сорок строк биндинга ради второй сцены было бы обидно вдвойне: два экземпляра
    /// одной настройки — это два места, где она однажды разойдётся.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Settings Window")]
    public sealed class SettingsWindow : MonoBehaviour
    {
        [Header("Имена элементов в разметке")]
        [SerializeField] private string _overlayName = "settings-overlay";
        [SerializeField] private string _masterName = "volume-master";
        [SerializeField] private string _effectsName = "volume-effects";
        [SerializeField] private string _masterLabelName = "volume-master-label";
        [SerializeField] private string _effectsLabelName = "volume-effects-label";
        [SerializeField] private string _closeName = "settings-close";
        [SerializeField] private string _testName = "volume-test";

        [Tooltip("Кнопка, открывающая окно. Пусто — окно открывает кто-то снаружи (в игре это GameHud).")]
        [SerializeField] private string _openName = "";

        private VisualElement _overlay;
        private Slider _master;
        private Slider _effects;
        private Label _masterLabel;
        private Label _effectsLabel;

        /// <summary>Заливка дорожки: сколько набрано, видно самой дорожкой, а не только местом ручки.</summary>
        private VisualElement _masterFill;
        private VisualElement _effectsFill;

        public bool IsOpen => _overlay != null && _overlay.style.display != DisplayStyle.None;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) { enabled = false; return; }

            _overlay = root.Q<VisualElement>(_overlayName);
            _master = root.Q<Slider>(_masterName);
            _effects = root.Q<Slider>(_effectsName);
            _masterLabel = root.Q<Label>(_masterLabelName);
            _effectsLabel = root.Q<Label>(_effectsLabelName);

            var close = root.Q<Button>(_closeName);
            if (close != null) close.clicked += Close;

            var test = root.Q<Button>(_testName);
            if (test != null) test.clicked += () => Sfx.Play(b => b.Merge);

            if (!string.IsNullOrEmpty(_openName))
            {
                var open = root.Q<Button>(_openName);
                if (open != null) open.clicked += Open;
            }

            if (_overlay != null) _overlay.RegisterCallback<ClickEvent>(OnOverlayClick);

            _masterFill = AttachFill(_master);
            _effectsFill = AttachFill(_effects);

            if (_master != null) _master.RegisterValueChangedCallback(OnMasterChanged);
            if (_effects != null) _effects.RegisterValueChangedCallback(OnEffectsChanged);

            Pull();
            Close();
        }

        private void OnDisable()
        {
            if (_master != null) _master.UnregisterValueChangedCallback(OnMasterChanged);
            if (_effects != null) _effects.UnregisterValueChangedCallback(OnEffectsChanged);
        }

        public void Open()
        {
            Pull();
            if (_overlay != null) _overlay.style.display = DisplayStyle.Flex;
            Sfx.Play(b => b.UiOpen);
        }

        public void Close()
        {
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            Sfx.Instance?.SaveSettings();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        private void OnOverlayClick(ClickEvent evt)
        {
            if (evt.target == _overlay) Close();
        }

        /// <summary>Считать текущие значения в ползунки — окно может открыться сильно позже старта.</summary>
        private void Pull()
        {
            var sfx = Sfx.Instance;
            if (sfx == null) return;

            if (_master != null) _master.SetValueWithoutNotify(sfx.MasterVolume);
            if (_effects != null) _effects.SetValueWithoutNotify(sfx.EffectsVolume);

            UpdateLabels(sfx.MasterVolume, sfx.EffectsVolume);
        }

        private void OnMasterChanged(ChangeEvent<float> evt)
        {
            var sfx = Sfx.Instance;
            if (sfx == null) return;

            sfx.MasterVolume = evt.newValue;
            UpdateLabels(sfx.MasterVolume, sfx.EffectsVolume);

            // Слышимая обратная связь: без неё ползунок громкости — это ползунок вслепую.
            Sfx.Play(b => b.UiClick);
        }

        private void OnEffectsChanged(ChangeEvent<float> evt)
        {
            var sfx = Sfx.Instance;
            if (sfx == null) return;

            sfx.EffectsVolume = evt.newValue;
            UpdateLabels(sfx.MasterVolume, sfx.EffectsVolume);
            Sfx.Play(b => b.UiClick);
        }

        private void UpdateLabels(float master, float effects)
        {
            if (_masterLabel != null) _masterLabel.text = "Общая громкость — " + Mathf.RoundToInt(master * 100f) + "%";
            if (_effectsLabel != null) _effectsLabel.text = "Эффекты — " + Mathf.RoundToInt(effects * 100f) + "%";

            SetFill(_masterFill, _master, master);
            SetFill(_effectsFill, _effects, effects);
        }

        /// <summary>
        /// Подложить в жёлоб дорожки полоску набранного. У стокового <c>Slider</c> заливки нет
        /// вовсе: есть жёлоб и ручка, и громкость читается ТОЛЬКО местом ручки — «80 %» и
        /// «100 %» отличались тем, где стоит зелёный квадратик. Полоски нужд рядом устроены
        /// правильно (дорожка + заливка тем же <c>wood_bar_fill</c>), и звук выпадал из общего
        /// языка окна.
        /// <para>
        /// Кладём внутрь дорожки, а не рядом: жёлоб обрезает заливку своими краями сам, и её
        /// не приходится совмещать с ним вручную при каждом изменении размера окна.
        /// </para>
        /// </summary>
        private static VisualElement AttachFill(Slider slider)
        {
            var tracker = slider?.Q<VisualElement>(className: "unity-base-slider__tracker");
            if (tracker == null) return null;

            var fill = new VisualElement();
            fill.AddToClassList("slider__fill");
            // Дорожка ловит клик и переносит ручку; заливка лежит поверх неё и перехватила бы
            // это ровно на набранной части — то есть слева тянулось бы, а справа нет.
            fill.pickingMode = PickingMode.Ignore;
            tracker.Add(fill);
            return fill;
        }

        private static void SetFill(VisualElement fill, Slider slider, float value)
        {
            if (fill == null || slider == null) return;

            float span = slider.highValue - slider.lowValue;
            float part = span > 0.0001f ? (value - slider.lowValue) / span : 0f;
            fill.style.width = Length.Percent(Mathf.Clamp01(part) * 100f);
        }
    }
}

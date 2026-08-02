using UnityEngine;
using UnityEngine.UIElements;
using Farm.Juice;

namespace Farm.UI
{
    /// <summary>
    /// Audio settings. Values are applied as the slider moves — a settings panel that only takes
    /// effect on "apply" makes the player guess what they are choosing.
    /// <para>
    /// Persistence lives in <see cref="Sfx"/> rather than here: the setting must survive the window
    /// being closed, the scene reloading, and the UI being replaced entirely.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Settings Window")]
    public sealed class SettingsWindow : MonoBehaviour
    {
        private VisualElement _overlay;
        private Slider _master;
        private Slider _effects;
        private Label _masterLabel;
        private Label _effectsLabel;

        public bool IsOpen => _overlay != null && _overlay.style.display != DisplayStyle.None;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) { enabled = false; return; }

            _overlay = root.Q<VisualElement>("settings-overlay");
            _master = root.Q<Slider>("volume-master");
            _effects = root.Q<Slider>("volume-effects");
            _masterLabel = root.Q<Label>("volume-master-label");
            _effectsLabel = root.Q<Label>("volume-effects-label");

            var close = root.Q<Button>("settings-close");
            if (close != null) close.clicked += Close;

            var test = root.Q<Button>("volume-test");
            if (test != null) test.clicked += () => Sfx.Play(b => b.Merge);

            if (_overlay != null) _overlay.RegisterCallback<ClickEvent>(OnOverlayClick);

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

        /// <summary>Read current values into the sliders — the window may open long after startup.</summary>
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
        }
    }
}

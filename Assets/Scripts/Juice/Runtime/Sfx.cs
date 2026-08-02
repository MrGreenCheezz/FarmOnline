using UnityEngine;

namespace Farm.Juice
{
    /// <summary>
    /// Проигрывает банк звуков. Один пул AudioSource вместо источника на каждый объект:
    /// кью короткие и постоянно накладываются, а порождать источник на каждый выстрел —
    /// значит молотить объекты ровно в самые нагруженные моменты, когда игре нельзя дёргаться.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    [AddComponentMenu("Farm/Juice/Sfx")]
    public sealed class Sfx : MonoBehaviour
    {
        [SerializeField] private SoundBank _bank;

        [Tooltip("Сколько звуков может звучать одновременно.")]
        [SerializeField, Min(1)] private int _voices = 12;

        [SerializeField, Range(0f, 1f)] private float _masterVolume = 0.8f;

        [Tooltip("Не проигрывать один и тот же кью чаще, чем раз в столько секунд.")]
        [SerializeField, Min(0f)] private float _minRepeatInterval = 0.04f;

        private const string PrefMaster = "audio.master";
        private const string PrefEffects = "audio.effects";

        private AudioSource[] _sources;
        private int _next;
        private SoundBank.Cue _lastCue;
        private float _lastTime = -1f;
        private float _effectsVolume = 1f;

        public static Sfx Instance { get; private set; }
        public SoundBank Bank => _bank;

        /// <summary>Общий уровень. Сохраняется — громкость, слетающая при перезапуске, не настройка.</summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(PrefMaster, _masterVolume);
            }
        }

        /// <summary>Уровень эффектов поверх общего. Отдельно — чтобы позже сюда же встала музыка.</summary>
        public float EffectsVolume
        {
            get => _effectsVolume;
            set
            {
                _effectsVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(PrefEffects, _effectsVolume);
            }
        }

        /// <summary>Записать настройки на диск. Вызывается при закрытии окна настроек.</summary>
        public void SaveSettings() => PlayerPrefs.Save();

        private void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;

            _masterVolume = PlayerPrefs.GetFloat(PrefMaster, _masterVolume);
            _effectsVolume = PlayerPrefs.GetFloat(PrefEffects, 1f);

            _sources = new AudioSource[_voices];
            for (int i = 0; i < _voices; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;   // 2D: камера далеко, позиционный звук тут только мешал бы
                _sources[i] = source;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Play(SoundBank.Cue cue) => Play(cue, 0f);

        /// <summary>
        /// Проиграть кью со сдвигом высоты тона поверх его обычного случайного разброса.
        /// Сдвиг нужен звукам, которые обязаны «расти» вместе с событием — например,
        /// слияние звучит тем выше, чем выше уровень.
        /// </summary>
        public void Play(SoundBank.Cue cue, float pitchOffset)
        {
            if (cue == null || _sources == null) return;

            var clip = cue.Pick();
            if (clip == null) return;

            // Один и тот же кью, выпущенный дважды в одном кадре, звучит как щелчок помехи.
            if (cue == _lastCue && Time.unscaledTime - _lastTime < _minRepeatInterval) return;
            _lastCue = cue;
            _lastTime = Time.unscaledTime;

            var source = _sources[_next];
            _next = (_next + 1) % _sources.Length;

            source.pitch = 1f + pitchOffset + Random.Range(-cue.PitchJitter, cue.PitchJitter);
            source.PlayOneShot(clip, cue.Volume * _effectsVolume * _masterVolume);
        }

        /// <summary>Удобство, чтобы вызывающим не нужна была своя проверка синглтона на null.</summary>
        public static void Play(System.Func<SoundBank, SoundBank.Cue> select)
        {
            var sfx = Instance;
            if (sfx == null || sfx._bank == null || select == null) return;
            sfx.Play(select(sfx._bank));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}

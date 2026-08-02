using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Farm.Farming
{
    /// <summary>
    /// The farm's clock: turns the sun, dims the sky, counts days.
    /// <para>
    /// It lives in the farming assembly rather than with the rest of the juice because the farmer
    /// plans his day around it, and the character assembly cannot reference juice without a cycle.
    /// The visual side is a handful of lines; splitting the clock from the light it drives would
    /// cost more than it saves.
    /// </para>
    /// <para>
    /// It exists so the farmer has something to organise his life around. A character who works
    /// until a meter runs out and then naps is a state machine; a character who works while it is
    /// light and goes to bed at dusk is a routine, and a routine is what makes checking back in
    /// after ten minutes feel like something happened.
    /// </para>
    /// <para>
    /// Ambient light is driven through <see cref="RenderSettings.ambientIntensity"/> rather than by
    /// switching the ambient mode: the scene is lit from the skybox, and swapping that at runtime
    /// would change how everything looks the moment this component is added.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("Farm/Day Night Cycle")]
    public sealed class DayNightCycle : MonoBehaviour
    {
        [Header("Время")]
        [Tooltip("Сколько реальных секунд длятся полные сутки.")]
        [SerializeField, Min(10f)] private float _dayLength = 240f;

        [Tooltip("С какого времени суток начинается партия. 0.25 — раннее утро.")]
        [SerializeField, Range(0f, 1f)] private float _startTime = 0.28f;

        [Tooltip("Идёт ли время. Выключи, чтобы замереть на текущем часе.")]
        [SerializeField] private bool _running = true;

        [Header("Границы суток")]
        // Держатся вплотную к геометрии солнца (восход 0.25, закат 0.75). Разъехавшись,
        // они дают худшее: на экране глухая ночь, а игра всё ещё считает, что рабочий день.
        [Tooltip("Доля суток, когда встаёт солнце.")]
        [SerializeField, Range(0f, 0.5f)] private float _dawn = 0.26f;

        [Tooltip("Доля суток, когда садится.")]
        [SerializeField, Range(0.5f, 1f)] private float _dusk = 0.74f;

        [Header("Свет")]
        [Tooltip("Солнце. Пусто — найдётся первый направленный свет в сцене.")]
        [SerializeField] private Light _sun;

        [SerializeField, Min(0f)] private float _dayIntensity = 1.15f;

        [Tooltip("Ночь должна читаться. Игра про то, чтобы смотреть на ферму — а на чёрный экран " +
                 "смотреть нечего; правдоподобная темнота тут работает против замысла.")]
        [SerializeField, Min(0f)] private float _nightIntensity = 0.34f;

        [SerializeField] private Color _noonColor = new Color(1f, 0.96f, 0.88f);
        [SerializeField] private Color _duskColor = new Color(1f, 0.72f, 0.45f);
        [SerializeField] private Color _nightColor = new Color(0.52f, 0.62f, 0.9f);

        [Header("Окружающий свет")]
        // Цветом, а не множителем к скайбоксу. Множитель бесполезен ровно там, где он нужен:
        // на закате скайбокс сам почти чёрный, и любая доля от него остаётся чёрной — грани,
        // отвёрнутые от солнца, проваливаются в силуэт. Плоский ambient даёт нижнюю границу,
        // ниже которой сцена не уходит никогда.
        [Tooltip("Заполняющий свет в полдень.")]
        [SerializeField] private Color _ambientDayColor = new Color(0.55f, 0.58f, 0.62f);

        [Tooltip("Заполняющий свет ночью. Не чёрный намеренно — иначе половина фермы " +
                 "превращается в силуэт, и смотреть на неё нечем.")]
        [SerializeField] private Color _ambientNightColor = new Color(0.20f, 0.24f, 0.34f);

        [Header("Тени")]
        [Tooltip("Плотность тени в полдень.")]
        [SerializeField, Range(0f, 1f)] private float _shadowStrengthDay = 0.62f;

        [Tooltip("Плотность тени на низком солнце.\n" +
                 "Держится заметно ниже дневной: чем ниже солнце, тем длиннее тени и тем большая " +
                 "часть кадра оказывается в них — при полной плотности половина фермы уходит " +
                 "в непроглядно чёрное, потому что подсветить её нечем, кроме тусклого ambient.")]
        [SerializeField, Range(0f, 1f)] private float _shadowStrengthLow = 0.28f;

        [Tooltip("На сколько градусов повернуть путь солнца вокруг вертикали.")]
        [SerializeField, Range(0f, 360f)] private float _sunHeading = 35f;

        [Tooltip("Под каким углом стоит «луна». Ночью светильник не уходит под горизонт, а " +
                 "переезжает сюда: источник из-под земли не освещает ничего, и ночь чернеет в ноль.")]
        [SerializeField, Range(10f, 80f)] private float _moonElevation = 42f;

        private float _time01;
        private int _day = 1;
        private bool _wasNight;
        private float _originalAmbient = 1f;
        private AmbientMode _originalMode = AmbientMode.Skybox;
        private Color _originalAmbientColor = Color.grey;
        private bool _captured;

        public static DayNightCycle Instance { get; private set; }

        /// <summary>Time of day, 0..1. 0 is midnight, 0.5 is noon.</summary>
        public float Time01 => _time01;

        /// <summary>Which day it is, starting at 1.</summary>
        public int Day => _day;

        public bool IsNight => _time01 < _dawn || _time01 >= _dusk;
        public bool IsDay => !IsNight;

        /// <summary>Fraction of the working day already gone, 0..1. Clamped outside daylight.</summary>
        public float DayProgress01 =>
            Mathf.Clamp01(Mathf.InverseLerp(_dawn, _dusk, _time01));

        /// <summary>Dawn broke. Argument is the day that just started.</summary>
        public event Action<DayNightCycle, int> DayStarted;

        /// <summary>Dusk fell.</summary>
        public event Action<DayNightCycle, int> NightStarted;

        /// <summary>Clock reading like "07:30", for the HUD.</summary>
        public string ClockText
        {
            get
            {
                float hours = _time01 * 24f;
                int h = Mathf.FloorToInt(hours);
                int m = Mathf.FloorToInt((hours - h) * 60f);
                return h.ToString("00") + ":" + m.ToString("00");
            }
        }

        private void OnEnable()
        {
            Instance = this;

            if (_sun == null) _sun = FindSun();

            if (!_captured)
            {
                _originalAmbient = RenderSettings.ambientIntensity;
                _originalMode = RenderSettings.ambientMode;
                _originalAmbientColor = RenderSettings.ambientLight;
                _captured = true;
            }

            _time01 = Mathf.Repeat(_startTime, 1f);
            _wasNight = IsNight;
            Apply();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;

            // Возвращаем сцене её собственное освещение, иначе выключенный компонент
            // оставит редактор в вечных сумерках.
            if (!_captured) return;

            RenderSettings.ambientIntensity = _originalAmbient;
            RenderSettings.ambientMode = _originalMode;
            RenderSettings.ambientLight = _originalAmbientColor;
        }

        private void Update()
        {
            if (_running && _dayLength > 0f && Application.isPlaying)
            {
                float previous = _time01;
                _time01 = Mathf.Repeat(_time01 + Time.deltaTime / _dayLength, 1f);

                // Через полночь — новый день.
                if (_time01 < previous) _day++;

                bool night = IsNight;
                if (night != _wasNight)
                {
                    _wasNight = night;
                    Raise(night ? NightStarted : DayStarted, _day);
                }
            }

            Apply();
        }

        /// <summary>
        /// Jump to a time of day. Handy for testing the night without waiting for it.
        /// <para>
        /// Raises the dawn/dusk event if the jump crossed one. Skipping it would leave everything
        /// that lives by those events — the night harvest, the farmer's bedtime — stuck in the old
        /// half of the day while the sky says otherwise.
        /// </para>
        /// </summary>
        public void SetTime(float time01)
        {
            _time01 = Mathf.Repeat(time01, 1f);

            bool night = IsNight;
            if (night != _wasNight)
            {
                _wasNight = night;
                Raise(night ? NightStarted : DayStarted, _day);
            }

            Apply();
        }

        private void Apply()
        {
            if (_sun == null) return;

            // Высота солнца над горизонтом как множитель. Наклон пологий намеренно: при крутом
            // сумерки схлопываются в один кадр, и закат — самое красивое время суток — проходит
            // мимо игрока.
            float height = Mathf.Sin((_time01 - 0.25f) * Mathf.PI * 2f);
            float daylight = Mathf.Clamp01(height * 1.15f + 0.42f);

            if (height > -0.02f)
            {
                // Полдень — солнце в зените. Смещение на четверть суток переводит долю в угол так,
                // что 0.5 даёт 90 градусов над горизонтом.
                _sun.transform.rotation = Quaternion.Euler((_time01 - 0.25f) * 360f, _sunHeading, 0f);
            }
            else
            {
                // Ночь: тот же светильник работает луной с другой стороны неба.
                _sun.transform.rotation = Quaternion.Euler(_moonElevation, _sunHeading + 180f, 0f);
            }

            _sun.intensity = Mathf.Lerp(_nightIntensity, _dayIntensity, daylight);

            // Тёплый на рассвете и закате, нейтральный в полдень, холодный ночью.
            Color warm = Color.Lerp(_nightColor, _duskColor, Mathf.Clamp01(daylight * 3f));
            _sun.color = Color.Lerp(warm, _noonColor, Mathf.Clamp01((daylight - 0.35f) / 0.65f));

            // Тень слабеет вместе с солнцем. Считаем по высоте, а не по общей освещённости:
            // важен именно угол — он определяет, какая доля кадра лежит в тени.
            float high = Mathf.Clamp01(height);
            _sun.shadowStrength = Mathf.Lerp(_shadowStrengthLow, _shadowStrengthDay, high);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.ambientLight = Color.Lerp(_ambientNightColor, _ambientDayColor, daylight);
        }

        private static Light FindSun()
        {
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (light.type == LightType.Directional) return light;
            return null;
        }

        private void Raise(Action<DayNightCycle, int> handler, int day)
        {
            if (handler == null) return;
            try { handler(this, day); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}

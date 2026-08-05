using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Farm.Farming
{
    /// <summary>
    /// Часы фермы: крутят солнце, гасят небо, считают дни.
    /// <para>
    /// Живут в сборке фермы, а не рядом с остальным «соком», потому что фермер строит по ним
    /// свой день, а сборка персонажей не может ссылаться на Juice без цикла. Визуальная
    /// сторона — горстка строк; отделять часы от света, которым они управляют, стоило бы
    /// дороже, чем экономило.
    /// </para>
    /// <para>
    /// Существуют, чтобы фермеру было вокруг чего строить жизнь. Персонаж, который работает,
    /// пока не сядет шкала, и дремлет — это конечный автомат; персонаж, который работает,
    /// пока светло, и ложится в сумерках — это распорядок, а распорядок и делает возвращение
    /// в игру через десять минут ощущением, что что-то произошло.
    /// </para>
    /// <para>
    /// Окружающий свет ведётся через плоский ambient, а не множителем скайбокса: множитель
    /// бесполезен ровно там, где нужен, — на закате скайбокс сам почти чёрный.
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

        [Tooltip("Под каким углом стоит «луна» в полночь. Ночью светильник не уходит под " +
                 "горизонт, а поднимается сюда: источник из-под земли не освещает ничего, " +
                 "и ночь чернеет в ноль.")]
        [SerializeField, Range(10f, 80f)] private float _moonElevation = 42f;

        [Tooltip("Самое низкое положение светила — рассвет и закат.\n" +
                 "Ноль ставить нельзя: свет, лёгший вдоль земли, не освещает ничего, и кадр " +
                 "проваливается в черноту ровно в самое красивое время суток.")]
        [SerializeField, Range(3f, 25f)] private float _minElevation = 9f;

        private float _time01;
        private int _day = 1;
        private bool _wasNight;
        private float _originalAmbient = 1f;
        private AmbientMode _originalMode = AmbientMode.Skybox;
        private Color _originalAmbientColor = Color.grey;
        private bool _captured;

        public static DayNightCycle Instance { get; private set; }

        /// <summary>Время суток, 0..1. 0 — полночь, 0.5 — полдень.</summary>
        public float Time01 => _time01;

        /// <summary>Номер дня, с единицы.</summary>
        public int Day => _day;

        public bool IsNight => _time01 < _dawn || _time01 >= _dusk;
        public bool IsDay => !IsNight;

        /// <summary>Какая доля рабочего дня прошла, 0..1. Вне светлого времени зажата в края.</summary>
        public float DayProgress01 =>
            Mathf.Clamp01(Mathf.InverseLerp(_dawn, _dusk, _time01));

        /// <summary>Рассвело. Аргумент — только что начавшийся день.</summary>
        public event Action<DayNightCycle, int> DayStarted;

        /// <summary>Стемнело.</summary>
        public event Action<DayNightCycle, int> NightStarted;

        /// <summary>Показание часов вида «07:30» — для HUD.</summary>
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
        /// Перепрыгнуть на время суток. Удобно тестировать ночь, не дожидаясь её.
        /// <para>
        /// Если прыжок пересёк рассвет или закат — поднимает событие. Пропустить его значило бы
        /// оставить всё, что живёт по этим событиям — ночной сбор, отбой фермера, — в старой
        /// половине суток, когда небо уже говорит другое.
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

        /// <summary>Вернуть часы из сохранения: и время суток, и номер дня.</summary>
        public void RestoreState(float time01, int day)
        {
            _day = Mathf.Max(1, day);
            SetTime(time01);
        }

        private void Apply()
        {
            if (_sun == null) return;

            // Высота солнца над горизонтом как множитель. Наклон пологий намеренно: при крутом
            // сумерки схлопываются в один кадр, и закат — самое красивое время суток — проходит
            // мимо игрока.
            float height = Mathf.Sin((_time01 - 0.25f) * Mathf.PI * 2f);
            float daylight = Mathf.Clamp01(height * 1.15f + 0.42f);

            _sun.transform.rotation = SkyRotation(height);
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

        /// <summary>
        /// Куда повёрнут светильник. Одна непрерывная дуга на всю ночь и день, без разрыва.
        /// <para>
        /// Раньше здесь стоял жёсткий выбор «солнце или луна», и он давал ровно ту ступеньку,
        /// которую видно глазом: яркость менялась плавно, а поворот прыгал мгновенно. Солнце
        /// ложилось к горизонту, светило вдоль земли и не освещало почти ничего — кадр чернел;
        /// в точке переключения светильник телепортировался на другую сторону неба и разом
        /// заливал ферму сверху. Поэтому «сумерки» приходили <i>после</i> черноты, хотя должны
        /// быть до неё, а на рассвете то же самое происходило зеркально.
        /// </para>
        /// <para>
        /// Теперь высота никогда не опускается ниже <see cref="_minElevation"/> — свет не ложится
        /// вдоль земли и кадру неоткуда провалиться, — а «переезд на другую сторону неба» делает
        /// азимут, проходя полный круг за сутки. Обе величины непрерывны, поэтому взяться
        /// ступеньке больше неоткуда, и восход с закатом сами оказываются на разных сторонах.
        /// </para>
        /// </summary>
        private Quaternion SkyRotation(float height)
        {
            // Днём поднимаемся к зениту, ночью — к высоте луны. На рассвете и закате обе ветви
            // сходятся ровно в _minElevation, поэтому стыка между ними не видно.
            float elevation = height >= 0f
                ? Mathf.Lerp(_minElevation, 90f, height)
                : Mathf.Lerp(_minElevation, _moonElevation, -height);

            float azimuth = _sunHeading + _time01 * 360f;
            return Quaternion.Euler(elevation, azimuth, 0f);
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

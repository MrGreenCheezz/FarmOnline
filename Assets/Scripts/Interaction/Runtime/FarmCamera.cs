using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.Interaction
{
    /// <summary>
    /// Ноги камеры: панорама, зум и рамка, за которую не уехать.
    /// <para>
    /// Висит на <b>риге</b> — пустом объекте в той точке земли, куда камера смотрит; сама камера
    /// его ребёнок. Так сделано не для красоты: локальную позицию камеры каждый кадр переписывает
    /// <c>CameraBreath</c> из своего якоря, снятого один раз. Любой, кто тоже возьмётся за
    /// <c>localPosition</c> камеры, подерётся с дыханием и проиграет. Поэтому <b>двигается только
    /// риг</b>, а зум уводит его назад по лучу взгляда — камера при этом остаётся на своём
    /// локальном месте, точка взгляда не сдвигается ни на сантиметр, а меняется одна дистанция.
    /// </para>
    /// <para>
    /// Точка взгляда нарочно не совпадает с центром фермы: она на несколько метров ближе к камере,
    /// отчего ферма поднята в верх кадра, а низ оставлен под HUD. Смещение живёт в сцене — там,
    /// где риг поставлен, — и все расчёты кадра его учитывают.
    /// </para>
    /// <para>
    /// Левая кнопка мыши камере не принадлежит: ею игрок таскает и сливает грядки
    /// (<see cref="PlotDragger"/>, правило 1 проекта). Панорама — правая или средняя, WASD и стрелки.
    /// </para>
    /// <para>
    /// Тач: камере отданы <b>только два пальца</b> — щипок и снос, — потому что один палец
    /// предназначен грядкам. Оговорка: <see cref="PlotDragger"/> сегодня читает лишь мышь,
    /// так что на телефоне один палец пока не делает ничего. Это его хвост, не камеры;
    /// когда он появится, здесь менять будет нечего.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Farm Camera")]
    public sealed class FarmCamera : MonoBehaviour
    {
        [Tooltip("Игровая камера. Пусто — возьмём первую в детях, иначе Camera.main.")]
        [SerializeField] private Camera _camera;

        [Tooltip("UIDocument, поверх которого колесо и панорама не работают. Пусто — найдётся первый в сцене.")]
        [SerializeField] private UIDocument _ui;

        [Header("Панорама")]
        [Tooltip("Скорость панорамы клавишами на исходном отдалении, метров в секунду.\n" +
                 "На отъезде растёт вместе с дистанцией: иначе через выросшую вдвое ферму " +
                 "пришлось бы ехать вдвое дольше, хотя на экране она та же.")]
        [SerializeField, Min(1f)] private float _keyPanSpeed = 12f;

        [Header("Зум")]
        [Tooltip("Ближний предел — дистанция от камеры до точки взгляда, метров.\n" +
                 "9 м — примерно вдвое ближе исходных 19: грядка вдвое крупнее, и этого хватает, " +
                 "чтобы разглядеть ступень. Ближе камера начинает цеплять кроны деревьев.")]
        [SerializeField, Min(3f)] private float _minDistance = 9f;

        [Tooltip("Во сколько раз меняется дистанция за один щелчок колеса.")]
        [SerializeField, Range(1.02f, 1.5f)] private float _zoomStep = 1.15f;

        [Tooltip("Насколько быстро зум догоняет колесо. Больше — резче; 0 остановит зум совсем.")]
        [SerializeField, Range(1f, 30f)] private float _zoomSmoothing = 12f;

        [Header("Дальний предел")]
        [Tooltip("Какую долю половины кадра отдаём ферме при полном отъезде.\n" +
                 "0.85 значит: ближний край фермы садится на 85% вниз от центра кадра, а последние " +
                 "15% остаются под HUD и под воздух — упереть забор в самый край кадра значит " +
                 "спрятать его под плашками.")]
        [SerializeField, Range(0.5f, 1f)] private float _fitFill = 0.85f;

        [Tooltip("Сколько метров показать за забором при полном отъезде.")]
        [SerializeField, Min(0f)] private float _fitMargin = 0.8f;

        [Tooltip("Меньше этого дальний предел не опускается: на маленькой ферме кадр и так " +
                 "показывает её целиком, но отъехать хоть немного игрок вправе.")]
        [SerializeField, Min(1f)] private float _minZoomOut = 1.25f;

        [Tooltip("Потолок дистанции, метров. Нужен для узких окон (портрет на телефоне), где " +
                 "«вместить всё» означает поднять камеру на полсотни метров и превратить ферму " +
                 "в булавочную головку. Дальше — пусть игрок панорамирует.")]
        [SerializeField, Min(10f)] private float _distanceCeiling = 60f;

        [Header("Туман")]
        [Tooltip("Отодвигать ближнюю границу тумана вместе с отъездом камеры.\n" +
                 "На отъезде дальняя половина фермы уходит за 70 м и линейный туман, настроенный " +
                 "под ближний вид, красит её в серое. Дальняя граница при этом не трогается: " +
                 "она держит край земли (мир 120×120 м), и отодвинуть её значит показать обрыв.")]
        [SerializeField] private bool _fogFollowsZoom = true;

        [Tooltip("Докуда, в долях дальней границы, разрешено отодвигать ближнюю. Ближе к 1 — " +
                 "туман становится узкой стеной на горизонте вместо дымки.")]
        [SerializeField, Range(0.3f, 0.95f)] private float _fogNearShare = 0.75f;

        // ---- геометрия, снятая со сцены ----

        /// <summary>Направление взгляда. Камера не вращается, поэтому оно постоянное.</summary>
        private Vector3 _forward = Vector3.forward;

        /// <summary>Дистанция от камеры до точки взгляда, как её поставил дизайнер. Отсюда пляшет зум.</summary>
        private float _defaultDistance = 19f;

        /// <summary>Точка взгляда, поставленная в сцене. От неё считается дальний предел.</summary>
        private Vector3 _home;

        // ---- состояние ----

        /// <summary>Куда камера смотрит сейчас. Всегда на земле и всегда внутри фермы.</summary>
        private Vector3 _aim;

        /// <summary>Текущая (сглаженная) дистанция и та, которую просит игрок.</summary>
        private float _distance;
        private float _distanceGoal;

        private bool _dragging;
        private Vector3 _dragAnchor;

        private bool _pinching;
        private Vector2 _pinchMid;
        private float _pinchSpread;

        private float _farmRadius = 13f;

        // Сохранённый вид применяем не один раз, а до первого касания камеры игроком: уровень
        // фермы приезжает из сохранения позже нашего старта, и до него круг ещё прежний —
        // сохранённая точка за его краем срезалась бы навсегда, а зум упёрся бы в старый предел.
        private bool _restoring;
        private Vector3 _savedAim;
        private float _savedDistance;

        private bool _dirty;
        private float _dirtyAt;

        private float _fogNear;
        private float _fogFar;
        private bool _fogCaptured;

        private const string PrefAimX = "camera.aim.x";
        private const string PrefAimZ = "camera.aim.z";
        private const string PrefDistance = "camera.distance";

        /// <summary>Через столько секунд после последнего движения вид уезжает в PlayerPrefs.</summary>
        private const float SaveDelay = 1.5f;

        private void Awake()
        {
            if (_camera == null) _camera = GetComponentInChildren<Camera>();
            if (_camera == null) _camera = Camera.main;
            if (_ui == null) _ui = FindFirstObjectByType<UIDocument>();

            if (_camera == null)
            {
                // Молчаливая камера без камеры — самый дорогой вид отказа: игра идёт, мышь
                // ничего не делает, и причину ищут в мыши.
                Debug.LogError("FarmCamera: не нашёл камеру ни в детях, ни в Camera.main — управление камерой выключено.", this);
                enabled = false;
                return;
            }

            _forward = _camera.transform.forward;
            _home = transform.position;
            _aim = _home;

            // Дистанция — вдоль луча взгляда, а не по прямой: так она останется верной, даже если
            // камера сдвинута вбок от рига.
            _defaultDistance = Mathf.Max(1f, -Vector3.Dot(_camera.transform.position - _home, _forward));

            Vector3 lookAt = _camera.transform.position + _forward * _defaultDistance;
            if ((lookAt - _home).sqrMagnitude > 0.25f)
            {
                // Риг обязан стоять там, куда камера смотрит: на этом держится и панорама,
                // и расчёт дальнего предела. Разъехались — скажем вслух, молча кадр не спасти.
                Debug.LogWarning($"FarmCamera: риг стоит в {_home}, а камера смотрит в {lookAt}. " +
                                 "Поставь риг в точку взгляда, иначе кадр на отъезде уедет.", this);
            }

            _distance = _defaultDistance;
            _distanceGoal = _defaultDistance;

            var bounds = FarmBounds.Instance;
            if (bounds != null) _farmRadius = bounds.Radius;

            if (RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear)
            {
                // Числа берём из сцены, а не из кода: сцена сильнее кода, и настроенная дымка —
                // её дело. Мы её только растягиваем.
                _fogNear = RenderSettings.fogStartDistance;
                _fogFar = RenderSettings.fogEndDistance;
                _fogCaptured = _fogFar > _fogNear;
            }

            Restore();
        }

        /// <summary>Куда камера смотрит сейчас, на земле.</summary>
        public Vector3 Aim => _aim;

        /// <summary>Дистанция от камеры до точки взгляда, метров.</summary>
        public float Distance => _distance;

        /// <summary>
        /// Показать игроку место: навести камеру на точку и отъехать на заданную дистанцию.
        /// Пределы те же, что у мыши, — за забор и дальше предела не пустит.
        /// <para>
        /// Единственный способ подвинуть камеру из кода: положение рига пересчитывается каждый
        /// кадр, и прямая запись в <c>transform.position</c> проживёт ровно до конца этого кадра.
        /// </para>
        /// </summary>
        public void Frame(Vector3 worldPoint, float distance)
        {
            _aim = FarmBounds.ClampToFarm(new Vector3(worldPoint.x, _home.y, worldPoint.z));
            _distanceGoal = Mathf.Clamp(distance, _minDistance, MaxDistance());
            Touched();
        }

        private void OnEnable() => FarmBounds.RadiusChanged += OnRadiusChanged;

        private void OnDisable()
        {
            FarmBounds.RadiusChanged -= OnRadiusChanged;
            _dragging = false;
            _pinching = false;

            if (_dirty) Save();
            RestoreFog();
        }

        /// <summary>Ферма выросла — вместе с ней растут и площадка для панорамы, и право отъехать.</summary>
        private void OnRadiusChanged(float radius) => _farmRadius = radius;

        /// <summary>
        /// Радиус фермы. Спрашиваем границы каждый кадр, а не полагаемся на одно событие:
        /// радиус переживает и пересборку сцены при загрузке партии, и правку поля в инспекторе —
        /// оба пути события не поднимают, а камера с устаревшим радиусом молча не даёт отъехать.
        /// Подписка при этом остаётся: если границ в сцене не стало, последнее известное число
        /// лучше, чем ноль.
        /// </summary>
        private float FarmRadius
        {
            get
            {
                var bounds = FarmBounds.Instance;
                if (bounds != null) _farmRadius = bounds.Radius;
                return _farmRadius;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            ReadMouse();
            ReadKeys(dt);
            ReadTouch();

            if (_restoring)
            {
                _aim = _savedAim;
                _distanceGoal = _savedDistance;
            }

            // Пределы считаем каждый кадр: они зависят и от радиуса фермы, и от пропорций окна,
            // а окно в браузере меняют когда вздумается.
            float goal = Mathf.Clamp(_distanceGoal, _minDistance, MaxDistance());
            _distance = _zoomSmoothing > 0f
                ? Mathf.Lerp(_distance, goal, 1f - Mathf.Exp(-_zoomSmoothing * dt))
                : goal;

            Apply();
            ApplyFog();
            TickSave();
        }

        // ---- ввод ----

        private void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 screen = mouse.position.ReadValue();
            bool held = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            bool pressed = mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;

            if (pressed && !IsPointerOverUI(screen) && TryGroundPoint(screen, out Vector3 anchor))
            {
                _dragging = true;
                _dragAnchor = anchor;
                Touched();
            }

            if (!held) _dragging = false;
            else if (_dragging && TryGroundPoint(screen, out Vector3 under)) PanBy(_dragAnchor - under);

            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f && !IsPointerOverUI(screen)) Zoom(Notches(wheel));
        }

        /// <summary>
        /// Щелчки колеса из сырого значения. Windows шлёт 120 за щелчок, тачпад и браузер —
        /// единицы и дроби; без приведения зум на одном устройстве стоит, на другом швыряет.
        /// </summary>
        private static float Notches(float raw)
        {
            float notches = Mathf.Abs(raw) >= 10f ? raw / 120f : raw;
            return Mathf.Clamp(notches, -3f, 3f);
        }

        private void ReadKeys(float dt)
        {
            var keys = Keyboard.current;
            if (keys == null) return;

            float x = 0f;
            float z = 0f;
            if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) x -= 1f;
            if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) x += 1f;
            if (keys.sKey.isPressed || keys.downArrowKey.isPressed) z -= 1f;
            if (keys.wKey.isPressed || keys.upArrowKey.isPressed) z += 1f;
            if (x == 0f && z == 0f) return;

            var move = new Vector2(x, z);
            if (move.sqrMagnitude > 1f) move.Normalize();   // по диагонали не быстрее

            // Скорость от дистанции: на экране путь через ферму занимает одно и то же время,
            // на каком отдалении ни смотри.
            float speed = _keyPanSpeed * (_distance / _defaultDistance);
            Vector3 right = Vector3.ProjectOnPlane(_camera.transform.right, Vector3.up).normalized;
            Vector3 ahead = Vector3.ProjectOnPlane(_forward, Vector3.up).normalized;

            PanBy((right * move.x + ahead * move.y) * (speed * dt));
        }

        /// <summary>
        /// Два пальца — камере, один — грядкам. Щипок меняет дистанцию, снос середины двигает
        /// ферму под пальцами; и то и другое считается от одного и того же кадра, поэтому жесты
        /// не мешают друг другу.
        /// </summary>
        private void ReadTouch()
        {
            var screen = Touchscreen.current;
            if (screen == null) { _pinching = false; return; }

            Vector2 a = default;
            Vector2 b = default;
            int found = 0;

            var touches = screen.touches;
            for (int i = 0; i < touches.Count && found < 3; i++)
            {
                var t = touches[i];
                if (!t.press.isPressed) continue;

                if (found == 0) a = t.position.ReadValue();
                else if (found == 1) b = t.position.ReadValue();
                found++;
            }

            if (found != 2) { _pinching = false; return; }

            Vector2 mid = (a + b) * 0.5f;
            float spread = Vector2.Distance(a, b);

            if (!_pinching)
            {
                _pinching = true;
                _pinchMid = mid;
                _pinchSpread = spread;
                Touched();
                return;
            }

            if (spread > 1f && _pinchSpread > 1f)
            {
                // Пальцы разошлись — приблизить: дистанция меняется обратно разлёту.
                float goal = Mathf.Clamp(_distanceGoal * (_pinchSpread / spread), _minDistance, MaxDistance());
                if (!Mathf.Approximately(goal, _distanceGoal)) { _distanceGoal = goal; Touched(); }
            }

            if (TryGroundPoint(_pinchMid, out Vector3 was) && TryGroundPoint(mid, out Vector3 now)) PanBy(was - now);

            _pinchMid = mid;
            _pinchSpread = spread;
        }

        private void Zoom(float notches)
        {
            float goal = Mathf.Clamp(_distanceGoal * Mathf.Pow(_zoomStep, -notches), _minDistance, MaxDistance());
            if (Mathf.Approximately(goal, _distanceGoal)) return;

            _distanceGoal = goal;
            Touched();
        }

        private void PanBy(Vector3 delta)
        {
            delta.y = 0f;
            if (delta.sqrMagnitude < 1e-8f) return;

            // Верх кадра смотрит почти вдоль земли: там точка под курсором улетает за сотню
            // метров, и рывок мыши превратился бы в прыжок через всю ферму. Ограничение задевает
            // только такие вырожденные случаи — обычный жест в него не упирается.
            float limit = _distance * 0.5f;
            if (delta.sqrMagnitude > limit * limit) delta = delta.normalized * limit;

            _aim += delta;
            Touched();
        }

        /// <summary>Игрок сам взялся за камеру: сохранённый вид больше не навязываем, а новый — запомним.</summary>
        private void Touched()
        {
            _restoring = false;
            _dirty = true;
            _dirtyAt = Time.unscaledTime;
        }

        // ---- применение ----

        private void Apply()
        {
            _aim = FarmBounds.ClampToFarm(_aim);
            _aim.y = _home.y;

            // Зум — это шаг рига назад по лучу взгляда. Камера сидит на своём локальном месте
            // (там хозяйничает дыхание), а точка взгляда не двигается: угол наклона сохраняется
            // сам собой, потому что никто ничего не вращает.
            transform.position = _aim + _forward * (_defaultDistance - _distance);
        }

        /// <summary>
        /// Дальний предел: дистанция, с которой ферма целиком помещается в кадр из <see cref="_home"/>.
        /// <para>
        /// Считается, а не подбирается руками, потому что зависит сразу от четырёх вещей: радиуса
        /// фермы (он растёт по ступеням), угла наклона, поля зрения и пропорций окна. Любое
        /// записанное число разошлось бы с игрой на первой же ступени.
        /// </para>
        /// </summary>
        private float MaxDistance()
        {
            float need = FarmRadius + _fitMargin;
            float pitch = Mathf.Asin(Mathf.Clamp(-_forward.y, -1f, 1f));
            float half = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;   // поле зрения задано по вертикали

            // Насколько центр фермы дальше точки взгляда. Ради этого смещения ферма и стоит
            // в верхней части кадра, и ближний край от него выигрывает: чем ближе смотрим,
            // тем дальше вниз уходит нижняя кромка кадра.
            Vector3 ahead = Vector3.ProjectOnPlane(_forward, Vector3.up).normalized;
            Vector3 center = FarmBounds.Instance != null ? FarmBounds.Instance.Center : Vector3.zero;
            float behind = Vector3.Dot(center - _home, ahead);

            float best = _defaultDistance * _minZoomOut;
            float sin = Mathf.Sin(pitch);
            float cos = Mathf.Cos(pitch);

            // По вертикали держит ближняя точка круга: она ниже всех в кадре, и чем ближе камера,
            // тем быстрее она уезжает вниз. Условие «её экранная высота не ниже −_fitFill»
            // сворачивается до одной строки.
            float down = _fitFill * Mathf.Tan(half);
            if (down > 0.01f) best = Mathf.Max(best, (need - behind) * (cos + sin / down));

            // По горизонтали держит не край круга, а точка его касания взглядом: она чуть ближе
            // к камере, и на экране уходит дальше вбок, чем «бок» фермы. Отсюда квадраты:
            // это тангенс к окружности, а не ширина кадра на её глубине.
            float side = _fitFill * _camera.aspect * Mathf.Tan(half);
            if (side > 0.01f)
            {
                float across = need / side;
                best = Mathf.Max(best, Mathf.Sqrt(need * need * cos * cos + across * across) - behind * cos);
            }

            return Mathf.Clamp(best, _minDistance, _distanceCeiling);
        }

        private void ApplyFog()
        {
            if (!_fogCaptured || !_fogFollowsZoom) return;

            float ratio = Mathf.Max(1f, _distance / _defaultDistance);
            RenderSettings.fogStartDistance = Mathf.Min(_fogNear * ratio, _fogFar * _fogNearShare);
        }

        private void RestoreFog()
        {
            if (_fogCaptured) RenderSettings.fogStartDistance = _fogNear;
        }

        // ---- экран и земля ----

        /// <summary>Точка на плоскости земли под курсором. False — луч ушёл в небо.</summary>
        private bool TryGroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = _camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, _home.y, 0f));

            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = default;
            return false;
        }

        /// <summary>
        /// Истина, когда курсор над элементом UI Toolkit. Иначе колесо над списком магазина
        /// одновременно листало бы список и отъезжало от фермы.
        /// </summary>
        private bool IsPointerOverUI(Vector2 screen)
        {
            var panel = _ui != null && _ui.rootVisualElement != null ? _ui.rootVisualElement.panel : null;
            if (panel == null) return false;

            // Панель считает Y сверху вниз, экран — снизу вверх.
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(panelPoint) != null;
        }

        // ---- память между запусками ----

        private void Restore()
        {
            // В гостях вид не восстанавливаем: это чужая ферма другого размера, и своя
            // точка обзора там бессмысленна. Заодно она не будет перезаписана чужой.
            if (GuestMode.IsGuest) return;

            if (!PlayerPrefs.HasKey(PrefDistance)) return;

            _savedAim = new Vector3(
                PlayerPrefs.GetFloat(PrefAimX, _home.x),
                _home.y,
                PlayerPrefs.GetFloat(PrefAimZ, _home.z));
            _savedDistance = PlayerPrefs.GetFloat(PrefDistance, _defaultDistance);
            _restoring = true;
        }

        private void TickSave()
        {
            if (!_dirty || Time.unscaledTime - _dirtyAt < SaveDelay) return;
            Save();
        }

        private void Save()
        {
            _dirty = false;

            // Вид с чужой фермы своим не становится: гость её только смотрит, а масштабы
            // там другие — вернувшись домой, игрок оказался бы в чужом кадре.
            if (GuestMode.IsGuest) return;

            PlayerPrefs.SetFloat(PrefAimX, _aim.x);
            PlayerPrefs.SetFloat(PrefAimZ, _aim.z);
            PlayerPrefs.SetFloat(PrefDistance, _distanceGoal);
            PlayerPrefs.Save();
        }

        private void OnApplicationPause(bool paused)
        {
            // В браузере вкладку закрывают без предупреждения: пауза — последний надёжный
            // момент, когда вид ещё можно записать.
            if (paused && _dirty) Save();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.9f);
            Gizmos.DrawWireSphere(Application.isPlaying ? _aim : transform.position, 0.4f);
        }
    }
}

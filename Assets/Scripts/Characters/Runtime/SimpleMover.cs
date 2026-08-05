using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    /// <summary>
    /// Ходок по прямой: разворачивается к цели и идёт.
    /// Без поиска пути — спокойно пройдёт сквозь препятствие, что для открытого поля нормально.
    /// <para>
    /// «По прямой» — про намерение, не про траекторию. Идеальная прямая с постоянной
    /// скоростью и мгновенным стартом — самый громкий признак автомата: живой шаг
    /// разгоняется, тормозит у цели, чуть гуляет вбок и не умеет идти боком во время
    /// разворота. Всё это здесь, а не в агенте: агент решает «куда», походка — «как».
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Simple Mover")]
    public sealed class SimpleMover : AgentMover
    {
        [SerializeField, Min(0.01f)] private float _speed = 1.5f;

        [Tooltip("Градусов в секунду при развороте.")]
        [SerializeField, Min(1f)] private float _turnSpeed = 540f;

        [Tooltip("На каком расстоянии считать, что дошёл.")]
        [SerializeField, Min(0.01f)] private float _arriveDistance = 0.2f;

        [Tooltip("Держать исходную высоту — цели задаются в плоскости XZ.")]
        [SerializeField] private bool _lockToStartHeight = true;

        [Tooltip("Идти по рельефу земли. Выключи, если персонаж должен держаться одной высоты.")]
        [SerializeField] private bool _followGround = true;

        [Header("Живость шага")]
        [Tooltip("Разгон и торможение, ед/с². Старт с места на полной скорости читается как телепорт.")]
        [SerializeField, Min(0.5f)] private float _acceleration = 6f;

        [Tooltip("Насколько курс гуляет вбок от прямой. 0 — лазерная линия.")]
        [SerializeField, Range(0f, 1f)] private float _meander = 0.3f;

        [Tooltip("Как быстро курс гуляет, колебаний в секунду.")]
        [SerializeField, Min(0.05f)] private float _meanderFrequency = 0.45f;

        [Tooltip("Разброс темпа от ходки к ходке, доля. Один и тот же темп каждый рейс — походка робота.")]
        [SerializeField, Range(0f, 0.3f)] private float _paceVariance = 0.07f;

        private Vector3 _destination;
        private bool _hasDestination;
        private float _groundY;
        private float _currentSpeed;

        // Живость: свой сдвиг шума у каждого ходока и свой темп у каждой ходки.
        private float _noiseSeed;
        private float _legPace = 1f;
        private Vector3 _legDestination;

        public override float Speed
        {
            get => _speed;
            set => _speed = Mathf.Max(0.01f, value);
        }

        public override float CurrentSpeed => _currentSpeed;

        public override bool HasArrived => !_hasDestination;

        private void Awake()
        {
            _groundY = transform.position.y;
            _noiseSeed = UnityEngine.Random.value * 97f;
        }

        public override void SetDestination(Vector3 worldPosition)
        {
            _destination = worldPosition;
            if (_lockToStartHeight) _destination.y = _groundY;
            _hasDestination = true;

            // Новый темп только на настоящую новую ходку: агент переутверждает ту же цель
            // каждый кадр, и перекатывать темп на каждый вызов значило бы дрожать скоростью.
            if ((worldPosition - _legDestination).sqrMagnitude > 2.25f)
            {
                _legDestination = worldPosition;
                _legPace = 1f + UnityEngine.Random.Range(-_paceVariance, _paceVariance);
            }
        }

        public override void Stop()
        {
            _hasDestination = false;
            _currentSpeed = 0f;
        }

        private void Update()
        {
            if (!_hasDestination)
            {
                _currentSpeed = 0f;
                return;
            }

            Vector3 position = transform.position;
            Vector3 offset = _destination - position;
            offset.y = 0f;

            float distance = offset.magnitude;
            if (distance <= _arriveDistance)
            {
                Stop();
                return;
            }

            Vector3 direction = offset / distance;

            // Курс чуть гуляет вбок, но у цели сходится к прямой — иначе он кружил бы
            // вокруг точки прибытия, не в силах попасть в радиус «дошёл».
            if (_meander > 0f)
            {
                float wave = Mathf.PerlinNoise(_noiseSeed, Time.time * _meanderFrequency) * 2f - 1f;
                float fade = Mathf.InverseLerp(0.7f, 2.4f, distance);
                Vector3 side = new Vector3(-direction.z, 0f, direction.x);
                direction = (direction + side * (wave * _meander * fade)).normalized;
            }

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                _turnSpeed * Time.deltaTime);

            // Сначала развернуться, потом разгоняться: ходьба боком со скоростью бега —
            // то, чего живое тело просто не делает.
            float facing = Mathf.InverseLerp(0.1f, 0.85f, Vector3.Dot(transform.forward, direction));
            float targetSpeed = _speed * _legPace * Mathf.Lerp(0.35f, 1f, facing);

            // Торможение к цели: он приходит и останавливается, а не выключается в точке.
            const float BrakeDistance = 1f;
            if (distance < BrakeDistance)
                targetSpeed *= Mathf.Lerp(0.45f, 1f, distance / BrakeDistance);

            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, _acceleration * Time.deltaTime);

            float step = Mathf.Min(_currentSpeed * Time.deltaTime, distance);
            Vector3 next = position + direction * step;

            // По рельефу: без этого персонаж идёт по своей исходной высоте и на пологой
            // волне то уходит в землю по колено, то шагает над ней.
            if (_followGround) next.y = _groundY + FarmingRuntime.Ground.SampleHeight(next);

            transform.position = next;

            // Земля запоминает шаги — где ходят, там протаптывается тропинка.
            Footpaths.Report(position, next);
        }
    }
}

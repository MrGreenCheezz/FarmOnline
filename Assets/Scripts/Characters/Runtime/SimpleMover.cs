using UnityEngine;

namespace Farm.Characters
{
    /// <summary>
    /// Straight-line walker: turns toward the destination and walks at a constant speed.
    /// No pathfinding — it will happily walk through obstacles, which is fine for an open field.
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

        private Vector3 _destination;
        private bool _hasDestination;
        private float _groundY;
        private float _currentSpeed;

        public override float Speed
        {
            get => _speed;
            set => _speed = Mathf.Max(0.01f, value);
        }

        public override float CurrentSpeed => _currentSpeed;

        public override bool HasArrived => !_hasDestination;

        private void Awake() => _groundY = transform.position.y;

        public override void SetDestination(Vector3 worldPosition)
        {
            _destination = worldPosition;
            if (_lockToStartHeight) _destination.y = _groundY;
            _hasDestination = true;
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

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                _turnSpeed * Time.deltaTime);

            float step = Mathf.Min(_speed * Time.deltaTime, distance);
            transform.position = position + direction * step;

            _currentSpeed = Time.deltaTime > 0f ? step / Time.deltaTime : 0f;
        }
    }
}

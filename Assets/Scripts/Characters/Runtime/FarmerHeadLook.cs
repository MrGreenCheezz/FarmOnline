using UnityEngine;

namespace Farm.Characters
{
    /// <summary>
    /// Голова как канал внимания: доворачивает кость головы к тому, что заметил фермер
    /// (<see cref="FarmerAgent.GlanceTarget"/>), не трогая ни корпус, ни маршрут.
    /// <para>
    /// Без этого «взгляд на ходу» невозможен в принципе: корпусом каждый кадр рулит ходок,
    /// и любой поворот тела к событию либо перезаписывается, либо дерётся с ним дрожью.
    /// Голова — единственное, что можно повернуть, продолжая идти куда шёл.
    /// </para>
    /// <para>
    /// Чистая презентация по образцу <see cref="FarmerAnimator"/>: читает состояние, никогда
    /// его не задаёт, удаление стоит только картинки. Кость ищется по имени; не нашлась —
    /// компонент тихо бездействует.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Farmer Head Look")]
    public sealed class FarmerHeadLook : MonoBehaviour
    {
        [Tooltip("Кость головы. Пусто — найдётся по имени, содержащему 'head'.")]
        [SerializeField] private Transform _head;

        [Tooltip("Дальше этого угла от курса шея не выкручивается — фермер просто не смотрит.")]
        [SerializeField, Range(10f, 90f)] private float _maxYaw = 65f;

        [SerializeField, Range(5f, 45f)] private float _maxPitch = 20f;

        [Tooltip("Скорость поворота головы, градусов в секунду.")]
        [SerializeField, Min(30f)] private float _turnSpeed = 240f;

        [Tooltip("За сколько секунд взгляд включается и гаснет.")]
        [SerializeField, Min(0.05f)] private float _blendTime = 0.25f;

        private FarmerAgent _agent;
        private Quaternion _applied;
        private float _weight;

        private void Awake()
        {
            _agent = GetComponent<FarmerAgent>();

            if (_head == null)
            {
                foreach (var child in GetComponentsInChildren<Transform>())
                {
                    if (child == transform) continue;
                    if (!child.name.ToLowerInvariant().Contains("head")) continue;
                    _head = child;
                    break;
                }
            }
        }

        // LateUpdate: после Animator, когда поза кадра уже записана, — иначе анимация
        // затирала бы наш поворот тем же кадром.
        private void LateUpdate()
        {
            if (_head == null || _agent == null) return;

            Quaternion animated = _head.rotation;
            bool wants = TryGetDesired(out Quaternion desired);

            _weight = Mathf.MoveTowards(_weight, wants ? 1f : 0f, Time.deltaTime / _blendTime);
            if (_weight <= 0f)
            {
                // Взгляд погашен: анимация владеет головой безраздельно, а память поворота
                // держится свежей, чтобы следующий взгляд начинался от текущей позы.
                _applied = animated;
                return;
            }

            _applied = Quaternion.RotateTowards(_applied, wants ? desired : animated,
                _turnSpeed * Time.deltaTime);

            _head.rotation = Quaternion.Slerp(animated, _applied, _weight);
        }

        /// <summary>Куда голове хочется смотреть — уже с ограничением шеи. False — некуда или не дотянуться.</summary>
        private bool TryGetDesired(out Quaternion desired)
        {
            desired = default;

            Vector3? target = _agent.GlanceTarget;
            if (!target.HasValue) return false;

            Vector3 to = target.Value - _head.position;
            if (to.sqrMagnitude < 0.04f) return false;

            // Углы меряются от корпуса: голова поворачивается ОТНОСИТЕЛЬНО курса, и предел
            // шеи должен ехать вместе с идущим телом, а не с миром.
            Quaternion relative = Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(to.normalized);
            Vector3 euler = relative.eulerAngles;

            float yaw = Mathf.DeltaAngle(0f, euler.y);

            // За пределом шеи не выкручиваемся и не «прилипаем» к краю: невозможный взгляд
            // честно не случается — точно так же занятый не оборачивается всем телом.
            if (Mathf.Abs(yaw) > _maxYaw) return false;

            float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), -_maxPitch, _maxPitch);

            desired = transform.rotation * Quaternion.Euler(pitch, yaw, 0f);
            return true;
        }
    }
}

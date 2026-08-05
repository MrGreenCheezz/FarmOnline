using UnityEngine;

namespace Farm.Game
{
    /// <summary>
    /// Медленный облёт фермы за спиной меню. Без единого ключевого кадра: круг, дыхание высоты
    /// и лёгкое покачивание — камера, стоящая намертво, превращает живую сцену в скриншот.
    /// <para>
    /// Скорости нарочно на грани заметности. Меню — это пауза, а не аттракцион: движение должно
    /// читаться, только если на него смотреть, и никогда не отвлекать от кнопок.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Farm/Menu Camera")]
    public sealed class MenuCamera : MonoBehaviour
    {
        [Tooltip("Вокруг чего кружить. Пусто — вокруг начала координат.")]
        [SerializeField] private Transform _focus;

        [SerializeField] private Vector3 _focusOffset = new Vector3(0f, 1.2f, 0f);

        [Header("Облёт")]
        [SerializeField, Min(1f)] private float _radius = 17f;
        [SerializeField, Min(0f)] private float _height = 7.5f;

        [Tooltip("Градусов в секунду. Полный круг за пару минут — это ощущается как дыхание, а не как карусель.")]
        [SerializeField] private float _degreesPerSecond = 2.2f;

        [SerializeField] private float _startAngle = 35f;

        [Header("Дыхание")]
        [Tooltip("На сколько метров камера всплывает и опускается.")]
        [SerializeField, Min(0f)] private float _bobHeight = 0.7f;

        [Tooltip("Насколько медленно. Период = 1 / это.")]
        [SerializeField, Min(0.001f)] private float _bobSpeed = 0.07f;

        private float _angle;

        private void Awake() => _angle = _startAngle;

        private void LateUpdate()
        {
            // unscaled: меню живёт вне игрового времени, и пауза его не замораживает.
            _angle += _degreesPerSecond * Time.unscaledDeltaTime;

            Vector3 centre = (_focus != null ? _focus.position : Vector3.zero) + _focusOffset;

            float radians = _angle * Mathf.Deg2Rad;
            float bob = Mathf.Sin(Time.unscaledTime * _bobSpeed * Mathf.PI * 2f) * _bobHeight;

            transform.position = centre + new Vector3(
                Mathf.Cos(radians) * _radius,
                _height + bob,
                Mathf.Sin(radians) * _radius);

            transform.rotation = Quaternion.LookRotation(centre - transform.position, Vector3.up);
        }
    }
}

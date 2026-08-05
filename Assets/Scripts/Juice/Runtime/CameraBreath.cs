using UnityEngine;

namespace Farm.Juice
{
    /// <summary>
    /// Камера чуть дышит: очень медленный снос и почти незаметный крен.
    /// <para>
    /// Неподвижная камера превращает кадр в скриншот — всё живое внутри него читается как
    /// анимация на картинке, а не как мир, в котором игрок находится. Достаточно движения на
    /// грани восприятия, чтобы кадр стал «снятым», а не «нарисованным»: замечать его не нужно,
    /// нужно чтобы без него становилось хуже.
    /// </para>
    /// <para>
    /// Смещается <b>позиция</b>, а не поворот. Крен на тех же амплитудах читается как качка и
    /// укачивает; снос на пару сантиметров — нет. По той же причине здесь перлин, а не синус:
    /// у синуса слышен период, и через минуту наблюдения он выдаёт себя как метроном.
    /// </para>
    /// <para>
    /// Ставится кодом из <see cref="FarmJuice"/> — сцену править не надо, как и в случае с
    /// <c>FarmerHeadLook</c>. Удали компонент, и камера просто замрёт.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Camera Breath")]
    public sealed class CameraBreath : MonoBehaviour
    {
        [Tooltip("Размах сноса вбок и вверх, в метрах. Держи сантиметровым: это дыхание, а не качка.")]
        [SerializeField, Range(0f, 0.5f)] private float _sway = 0.06f;

        [Tooltip("Размах подачи вперёд-назад. Меньше бокового: приближение заметнее сноса.")]
        [SerializeField, Range(0f, 0.3f)] private float _push = 0.03f;

        [Tooltip("Скорость. 0.05 — примерно полный цикл за полминуты, то есть медленнее, " +
                 "чем взгляд успевает за ним следить.")]
        [SerializeField, Range(0.01f, 0.5f)] private float _speed = 0.05f;

        private Vector3 _anchor;
        private Vector3 _applied;
        private float _seed;

        private void OnEnable()
        {
            // Запоминаем то положение, которое поставил дизайнер, а не то, куда мы сами сдвинули.
            _anchor = transform.localPosition - _applied;
            _seed = Random.value * 100f;
        }

        private void OnDisable()
        {
            // Возвращаем камеру ровно туда, откуда взяли: иначе каждый выход из игры оставлял бы
            // её на пару сантиметров в стороне, и за сессию она уползла бы заметно.
            transform.localPosition = _anchor;
            _applied = Vector3.zero;
        }

        private void LateUpdate()
        {
            // Немасштабируемое время: на паузе и в ускоренном прогоне дыхание должно остаться
            // тем же самым, иначе оно превращается в индикатор скорости игры.
            float t = Time.unscaledTime * _speed;

            var offset = new Vector3(
                Signed(Mathf.PerlinNoise(t, _seed)) * _sway,
                Signed(Mathf.PerlinNoise(t + 13.7f, _seed)) * _sway * 0.6f,
                Signed(Mathf.PerlinNoise(t + 41.3f, _seed)) * _push);

            _applied = offset;
            transform.localPosition = _anchor + offset;
        }

        /// <summary>Шум Unity даёт 0..1, а дыханию нужно уходить в обе стороны.</summary>
        private static float Signed(float noise01) => noise01 * 2f - 1f;
    }
}

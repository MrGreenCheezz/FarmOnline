using UnityEngine;
using Farm.Farming;

namespace Farm.Juice
{
    /// <summary>
    /// Светильник, живущий по своим часам: тёмный в полдень, зажжённый ночью, с неровностью огня.
    /// <para>
    /// Читает <see cref="DayNightCycle"/> сам, а не включается кем-то извне, — лампа работает в
    /// момент, когда её бросили на ферму: магазин ставит покупку и уходит, и лампа, которую надо
    /// было бы потом подключать, приехала бы тёмной.
    /// </para>
    /// <para>
    /// Мерцание нарочно медленное и неглубокое. Быстрое читается как сломанная лампа, а не как
    /// огонь, и на ферме, уставленной кострами, превращает всё поле в стробоскоп.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Farm Lamp")]
    public sealed class FarmLamp : MonoBehaviour
    {
        [Tooltip("Чем светит. Пусто — возьмётся Light на этом объекте или ниже.")]
        [SerializeField] private Light _light;

        [Tooltip("Яркость при полностью зажжённом состоянии.")]
        [SerializeField, Min(0f)] private float _intensity = 3.2f;

        [Tooltip("Насколько дрожит пламя, в долях яркости.")]
        [SerializeField, Range(0f, 0.6f)] private float _flicker = 0.18f;

        [Tooltip("За сколько секунд разгорается и гаснет.")]
        [SerializeField, Min(0.05f)] private float _fade = 2.5f;

        [Tooltip("Что подсвечивать вместе со светом — материал самого костра.")]
        [SerializeField] private Renderer _emberRenderer;

        [SerializeField] private Color _emberColor = new Color(1f, 0.55f, 0.2f);

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _block;
        private float _lit;
        private float _phase;

        /// <summary>Насколько лампа зажжена, 0..1. Ведёт и свет, и свечение углей.</summary>
        public float Lit => _lit;

        private void Awake()
        {
            if (_light == null) _light = GetComponentInChildren<Light>();
            if (_emberRenderer == null) _emberRenderer = GetComponentInChildren<Renderer>();

            // Фаза от положения в мире: соседние костры не должны мигать в такт.
            _phase = Mathf.Abs(transform.position.x * 3.7f + transform.position.z * 11.3f) % 10f;

            _lit = ShouldBeLit ? 1f : 0f;
            Apply();
        }

        private bool ShouldBeLit
        {
            get
            {
                var cycle = DayNightCycle.Instance;
                return cycle == null || cycle.IsNight;   // без часов светим всегда, а не никогда
            }
        }

        private void Update()
        {
            float target = ShouldBeLit ? 1f : 0f;
            _lit = Mathf.MoveTowards(_lit, target, Time.deltaTime / _fade);
            Apply();
        }

        private void Apply()
        {
            if (_light != null)
            {
                float wobble = 1f + Mathf.Sin(Time.time * 2.3f + _phase) * 0.6f * _flicker
                                  + Mathf.Sin(Time.time * 5.1f + _phase * 2f) * 0.4f * _flicker;

                _light.intensity = _intensity * _lit * wobble;
                _light.enabled = _lit > 0.01f;
            }

            if (_emberRenderer == null) return;

            _block ??= new MaterialPropertyBlock();
            _emberRenderer.GetPropertyBlock(_block);
            _block.SetColor(EmissionColor, _emberColor * _lit * 2f);
            _emberRenderer.SetPropertyBlock(_block);
        }
    }
}

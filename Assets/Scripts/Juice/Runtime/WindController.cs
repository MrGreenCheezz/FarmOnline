using UnityEngine;

namespace Farm.Juice
{
    /// <summary>
    /// Погода фермы с точки зрения шейдера растительности. Публикует направление ветра,
    /// силу и порывы как глобальные шейдер-переменные.
    /// <para>
    /// Порывы живут здесь, а не в шейдере, потому что порыв обязан быть общим: когда каждое
    /// растение крутит свой, поле ровно мерцает и читается как шум. Одна кривая порыва на всю
    /// ферму — то, что делает его похожим на ветер: поле накатывает и стихает вместе, а у
    /// каждого растения при этом своя фаза от его позиции.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("Farm/Wind Controller")]
    public sealed class WindController : MonoBehaviour
    {
        private static readonly int WindId = Shader.PropertyToID("_FarmWind");
        private static readonly int GustId = Shader.PropertyToID("_FarmWindGust");

        [Header("Направление")]
        [Tooltip("Куда дует, в градусах вокруг вертикали.")]
        [SerializeField, Range(0f, 360f)] private float _direction = 35f;

        [Tooltip("На сколько градусов направление гуляет туда-сюда.")]
        [SerializeField, Range(0f, 90f)] private float _directionSway = 18f;

        [Tooltip("Как быстро гуляет направление.")]
        [SerializeField, Min(0f)] private float _directionSwaySpeed = 0.08f;

        [Header("Сила")]
        [Tooltip("Базовая сила. Множитель к тому, что задано в материале растения.")]
        [SerializeField, Min(0f)] private float _strength = 1f;

        [Tooltip("Насколько сильно порывы отклоняются от базы. 0 — ровный ветер без порывов.")]
        [SerializeField, Range(0f, 1f)] private float _gustDepth = 0.55f;

        [Tooltip("Как часто накатывают порывы.")]
        [SerializeField, Min(0.01f)] private float _gustSpeed = 0.35f;

        [Header("Скорость качания")]
        [SerializeField, Min(0f)] private float _speed = 1f;

        [Tooltip("Множитель мелкой дрожи листвы. Растёт вместе с порывом.")]
        [SerializeField, Range(0f, 2f)] private float _turbulence = 1f;

        /// <summary>Текущий множитель порыва. Открыт, чтобы эффекты и звук могли дышать вместе с ветром.</summary>
        public float Gust { get; private set; } = 1f;

        private void OnEnable() => Apply();
        private void OnDisable() => Publish(Vector2.right, 0f, 0f, 1f, 0f);   // штиль, чтобы сцена не осталась с чужим ветром
        private void Update() => Apply();

        private void OnValidate()
        {
            if (isActiveAndEnabled) Apply();
        }

        private void Apply()
        {
            // Время редактора, а не игровое: ветер должен дуть и на паузе, иначе
            // подбирать материалы приходится вслепую.
            float time = Application.isPlaying ? Time.time : (float)UnityEditor_TimeSinceStartup();

            float angle = _direction + Mathf.Sin(time * _directionSwaySpeed * Mathf.PI * 2f) * _directionSway;
            float radians = angle * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));

            // Две несоизмеримые волны вместо шума: тот же эффект «неровного» порыва,
            // но результат воспроизводим и настраивается двумя ползунками.
            float wave = Mathf.Sin(time * _gustSpeed * Mathf.PI * 2f) * 0.6f
                       + Mathf.Sin(time * _gustSpeed * Mathf.PI * 2f * 1.73f + 2.1f) * 0.4f;

            Gust = Mathf.Max(0f, 1f + wave * _gustDepth);

            Publish(direction, _strength, _speed, Gust, _turbulence * Mathf.Lerp(0.6f, 1.4f, Mathf.InverseLerp(0f, 2f, Gust)));
        }

        private static void Publish(Vector2 direction, float strength, float speed, float gust, float turbulence)
        {
            Shader.SetGlobalVector(WindId, new Vector4(direction.x, direction.y, strength, speed));
            Shader.SetGlobalVector(GustId, new Vector4(gust, turbulence, 0f, 0f));
        }

        private static double UnityEditor_TimeSinceStartup()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.timeAsDouble;
#endif
        }

        /// <summary>
        /// Ветер существует и без этого компонента в сцене — иначе сцена, забывшая его, рисует
        /// каждое растение замершим на полусгибе с тем, что прошлая сцена оставила в глобалях.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SetDefaults() => Publish(new Vector2(0.82f, 0.57f), 1f, 1f, 1f, 1f);
    }
}

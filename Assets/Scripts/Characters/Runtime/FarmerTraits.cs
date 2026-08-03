using UnityEngine;

namespace Farm.Characters
{
    /// <summary>
    /// Стойкие предпочтения фермера — то, чем один работник отличается от другого.
    /// <para>
    /// Черты не решают за него, они лишь подкручивают веса в <see cref="FarmerBrain"/>. Это
    /// важно: если черта запрещает или предписывает действие, поведение становится жёстким
    /// и предсказуемым; если она смещает оценку, игрок видит склонность — «этот вечно лезет
    /// в шахту, а тот жмётся к дому», — но фермер всё равно делает разумное, когда припрёт.
    /// </para>
    /// <para>
    /// Компонент необязательный: без него все черты читаются как нейтральные 0.5, и фермер
    /// работает ровно так же, просто без характера.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Farmer Traits")]
    public sealed class FarmerTraits : MonoBehaviour
    {
        [Tooltip("Раскидать черты случайно при старте. Выключи, чтобы задать характер руками.")]
        [SerializeField] private bool _randomize = true;

        [Tooltip("Зерно для случайных черт. 0 — вывести из имени объекта, чтобы у разных " +
                 "работников были разные характеры, но у каждого — свой и неизменный.")]
        [SerializeField] private int _seed;

        [Header("Черты")]
        [Tooltip("Тяга к руде и дереву. Высокая — потащится через всю ферму за железом, " +
                 "низкая — предпочтёт грядки под боком.")]
        [SerializeField, Range(0f, 1f)] private float _oreLover = 0.5f;

        [Tooltip("Домосед. Высокий — сильнее штрафует дорогу и вертится у дома.")]
        [SerializeField, Range(0f, 1f)] private float _homebody = 0.5f;

        [Tooltip("Жаворонок. Высокий — берётся за работу сразу с рассветом; низкий " +
                 "раскачивается полутра, зато дольше возится вечером.")]
        [SerializeField, Range(0f, 1f)] private float _earlyRiser = 0.5f;

        [Tooltip("Аккуратность. Высокая — чаще бросает всё, чтобы навести порядок на ферме.")]
        [SerializeField, Range(0f, 1f)] private float _tidiness = 0.5f;

        [Tooltip("Смелость в темноте. Низкая — ночью держится ближе к огню и дому.")]
        [SerializeField, Range(0f, 1f)] private float _nightCourage = 0.5f;

        private bool _ready;

        public float OreLover { get { EnsureReady(); return _oreLover; } }
        public float Homebody { get { EnsureReady(); return _homebody; } }
        public float EarlyRiser { get { EnsureReady(); return _earlyRiser; } }
        public float Tidiness { get { EnsureReady(); return _tidiness; } }
        public float NightCourage { get { EnsureReady(); return _nightCourage; } }

        /// <summary>Нейтральное значение черты, когда компонента на персонаже нет.</summary>
        public const float Neutral = 0.5f;

        private void Awake() => EnsureReady();

        private void EnsureReady()
        {
            if (_ready) return;
            _ready = true;
            if (!_randomize) return;

            // Своё зерно на персонажа: характер должен быть случайным между работниками,
            // но одним и тем же у одного работника от запуска к запуску.
            var random = new System.Random(_seed != 0 ? _seed : name.GetHashCode());

            _oreLover = Roll(random);
            _homebody = Roll(random);
            _earlyRiser = Roll(random);
            _tidiness = Roll(random);
            _nightCourage = Roll(random);
        }

        /// <summary>
        /// Черта тяготеет к середине: сумма двух бросков вместо одного. Ровное распределение
        /// делает каждого второго работника карикатурой, а характер должен быть оттенком.
        /// </summary>
        private static float Roll(System.Random random) =>
            Mathf.Clamp01((float)(random.NextDouble() + random.NextDouble()) * 0.5f);

        /// <summary>Короткое описание характера для подсказок и логов.</summary>
        public string Describe()
        {
            EnsureReady();

            var parts = new System.Collections.Generic.List<string>(3);
            if (_oreLover > 0.68f) parts.Add("любит руду");
            else if (_oreLover < 0.32f) parts.Add("не любит шахты");

            if (_homebody > 0.68f) parts.Add("домосед");
            else if (_homebody < 0.32f) parts.Add("лёгок на подъём");

            if (_earlyRiser > 0.68f) parts.Add("жаворонок");
            else if (_earlyRiser < 0.32f) parts.Add("тяжело встаёт");

            if (_tidiness > 0.7f) parts.Add("аккуратист");
            if (_nightCourage < 0.3f) parts.Add("робеет в темноте");

            return parts.Count > 0 ? string.Join(", ", parts) : "ровный характер";
        }
    }
}

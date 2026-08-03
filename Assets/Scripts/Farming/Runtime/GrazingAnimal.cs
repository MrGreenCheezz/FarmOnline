using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Пасущееся животное: постоит, пожуёт, выберет новое место, не спеша дойдёт, иногда ляжет.
    /// <para>
    /// Живёт в сборке фермы, а не рядом с фермером, по одной практической причине: животных
    /// создаёт <see cref="Shop"/>, и он должен уметь навесить этот компонент на покупку сам.
    /// Из-за этого здесь своя маленькая ходьба вместо <c>AgentMover</c> — десяток строк дублируется,
    /// зато магазину не нужно знать про сборку персонажей.
    /// </para>
    /// <para>
    /// Загон никуда не привязан: животное пасётся вокруг той точки, где его поставили. Перетащил
    /// игрок — оно продолжит пастись на новом месте, а не потянется обратно.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Grazing Animal")]
    public sealed class GrazingAnimal : MonoBehaviour
    {
        private enum Phase
        {
            /// <summary>Стоит и жуёт.</summary>
            Chewing = 0,
            /// <summary>Не спеша идёт к выбранной точке.</summary>
            Walking = 1,
            /// <summary>Лежит.</summary>
            Resting = 2
        }

        [Tooltip("Что анимировать. Пусто — возьмётся Animator на этом объекте или ниже.")]
        [SerializeField] private Animator _animator;

        [Header("Ходьба")]
        [Tooltip("Скорость шага. Заметно медленнее фермера: животное пасётся, а не спешит.")]
        [SerializeField, Min(0.05f)] private float _speed = 0.55f;

        [SerializeField, Min(1f)] private float _turnSpeed = 220f;

        [Tooltip("Как далеко оно отходит от места, где его поставили.")]
        [SerializeField, Min(0.5f)] private float _wanderRadius = 3.5f;

        [Tooltip("На каком расстоянии считать, что дошло.")]
        [SerializeField, Min(0.05f)] private float _arriveDistance = 0.25f;

        [Header("Распорядок")]
        [Tooltip("Сколько секунд жуёт на месте, от и до.")]
        [SerializeField] private Vector2 _chewSeconds = new Vector2(4f, 11f);

        [Tooltip("Вероятность прилечь вместо очередного перехода.")]
        [SerializeField, Range(0f, 1f)] private float _restChance = 0.22f;

        [Tooltip("Сколько секунд лежит, от и до.")]
        [SerializeField] private Vector2 _restSeconds = new Vector2(8f, 20f);

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int RestingId = Animator.StringToHash("Resting");

        private Phase _phase = Phase.Chewing;
        private float _groundY;
        private Vector3 _anchor;
        private Vector3 _destination;
        private float _timer;
        private bool _hasSpeed;
        private bool _hasResting;

        /// <summary>Чем животное занято прямо сейчас — для отладки и подсказок.</summary>
        public bool IsResting => _phase == Phase.Resting;

        private void OnEnable()
        {
            _anchor = transform.position;

            // Смещение относительно рельефа: животное могло стоять не на нулевой отметке.
            _groundY = transform.position.y - FarmingRuntime.Ground.SampleHeight(transform.position);

            EnterChewing();
        }

        // Не в Awake: модель животного создаёт GrowableVisuals, и на момент Awake её ещё нет.
        private void Start() => RefreshAnimator();

        /// <summary>
        /// Найти Animator заново. Вызывать, если модель пересоздали на ходу.
        /// <para>
        /// Параметры ищутся один раз и пропускаются, если контроллер их не объявляет:
        /// недособранный контроллер ничего не логирует и ничего не ломает. Так свинья,
        /// у которой анимаций нет вовсе, спокойно ходит и жуёт без единой ошибки.
        /// </para>
        /// </summary>
        public void RefreshAnimator()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>();

            _hasSpeed = false;
            _hasResting = false;

            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == SpeedId) _hasSpeed = true;
                else if (p.nameHash == RestingId) _hasResting = true;
            }
        }

        /// <summary>
        /// Живой Animator текущей стадии.
        /// <para>
        /// Искать заново приходится потому, что <see cref="GrowableVisuals"/> держит модели всех
        /// стадий разом и переключает активную. После смены стадии прежняя ссылка указывает на
        /// выключенный объект, и животное молча замирает — анимация есть, но играть её некому.
        /// </para>
        /// </summary>
        private Animator ActiveAnimator()
        {
            if (_animator != null && _animator.isActiveAndEnabled) return _animator;

            _animator = null;
            RefreshAnimator();
            return _animator;
        }

        private void Update()
        {
            // Пока животное несут, оно не идёт никуда само: иначе вырывалось бы из-под курсора.
            if (DragFocus.IsDragged(transform))
            {
                _anchor = transform.position;   // отпустят — пастись будет уже здесь
                _groundY = 0f;                  // высоту задаёт тот, кто несёт
                Animate(0f);
                return;
            }

            switch (_phase)
            {
                case Phase.Chewing: TickChewing(); break;
                case Phase.Walking: TickWalking(); break;
                case Phase.Resting: TickResting(); break;
            }
        }

        // ---- распорядок ----

        private void TickChewing()
        {
            Animate(0f);

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            // Иногда вместо перехода — прилечь. Животное, которое только ходит и жуёт,
            // читается как заводная игрушка.
            if (Random.value < _restChance) EnterResting();
            else EnterWalking();
        }

        private void TickWalking()
        {
            Vector3 offset = _destination - transform.position;
            offset.y = 0f;

            float distance = offset.magnitude;
            if (distance <= _arriveDistance)
            {
                EnterChewing();
                return;
            }

            Vector3 direction = offset / distance;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                _turnSpeed * Time.deltaTime);

            Vector3 next = transform.position + direction * Mathf.Min(_speed * Time.deltaTime, distance);
            next.y = _groundY + FarmingRuntime.Ground.SampleHeight(next);
            transform.position = next;

            float step = Mathf.Min(_speed * Time.deltaTime, distance);

            Animate(Time.deltaTime > 0f ? step / Time.deltaTime / Mathf.Max(0.01f, _speed) : 0f);
        }

        private void TickResting()
        {
            Animate(0f);

            _timer -= Time.deltaTime;
            if (_timer <= 0f) EnterChewing();
        }

        // ---- переходы ----

        private void EnterChewing()
        {
            _phase = Phase.Chewing;
            _timer = Random.Range(_chewSeconds.x, Mathf.Max(_chewSeconds.x, _chewSeconds.y));
            SetResting(false);
        }

        private void EnterWalking()
        {
            _phase = Phase.Walking;
            SetResting(false);

            Vector2 offset = Random.insideUnitCircle * _wanderRadius;
            var target = _anchor + new Vector3(offset.x, 0f, offset.y);

            // За забор не уходим: животное, забредшее к горизонту, фермеру уже не собрать.
            _destination = FarmBounds.ClampToFarm(target);
            _destination.y = _groundY + FarmingRuntime.Ground.SampleHeight(_destination);
        }

        private void EnterResting()
        {
            _phase = Phase.Resting;
            _timer = Random.Range(_restSeconds.x, Mathf.Max(_restSeconds.x, _restSeconds.y));
            SetResting(true);
        }

        // ---- анимация ----

        private void Animate(float speed01)
        {
            var animator = ActiveAnimator();
            if (animator == null || !_hasSpeed) return;

            animator.SetFloat(SpeedId, Mathf.Clamp01(speed01), 0.12f, Time.deltaTime);

            // Отдых — состояние, а не мгновение: после смены стадии его надо утвердить заново.
            if (_hasResting) animator.SetBool(RestingId, _phase == Phase.Resting);
        }

        private void SetResting(bool resting)
        {
            var animator = ActiveAnimator();
            if (animator == null || !_hasResting) return;
            animator.SetBool(RestingId, resting);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 center = Application.isPlaying ? _anchor : transform.position;

            Gizmos.color = new Color(0.6f, 0.85f, 0.5f, 0.7f);
            const int segments = 32;
            Vector3 prev = center + new Vector3(_wanderRadius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(
                    Mathf.Cos(a) * _wanderRadius, 0f, Mathf.Sin(a) * _wanderRadius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}

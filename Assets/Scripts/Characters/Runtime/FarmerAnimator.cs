using UnityEngine;

namespace Farm.Characters
{
    /// <summary>
    /// Feeds the Animator from what the farmer is actually doing. Purely a translator — it reads
    /// state, never sets it, so removing this component costs the visuals and nothing else.
    /// <para>
    /// Parameters are looked up once and skipped if the controller does not declare them, so a
    /// half-finished controller logs nothing and breaks nothing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Farmer Animator")]
    public sealed class FarmerAnimator : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private FarmerAgent _agent;
        [SerializeField] private AgentMover _mover;

        [Tooltip("Скорость, при которой ходьба показывается на полную. Обычно = скорости ходьбы.")]
        [SerializeField, Min(0.01f)] private float _referenceSpeed = 1.5f;

        [Tooltip("Сглаживание параметра Speed, секунд.")]
        [SerializeField, Min(0f)] private float _damping = 0.1f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int CarryingId = Animator.StringToHash("Carrying");
        private static readonly int HarvestId = Animator.StringToHash("Harvest");

        private bool _hasSpeed;
        private bool _hasCarrying;
        private bool _hasHarvest;

        private void Awake()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_agent == null) _agent = GetComponent<FarmerAgent>();
            if (_mover == null) _mover = GetComponent<AgentMover>();

            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == SpeedId) _hasSpeed = true;
                else if (p.nameHash == CarryingId) _hasCarrying = true;
                else if (p.nameHash == HarvestId) _hasHarvest = true;
            }
        }

        private void OnEnable()
        {
            if (_agent != null) _agent.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            if (_agent != null) _agent.StateChanged -= OnStateChanged;
        }

        private void Update()
        {
            if (_animator == null || _mover == null) return;

            if (_hasSpeed)
                _animator.SetFloat(SpeedId, Mathf.Clamp01(_mover.CurrentSpeed / _referenceSpeed),
                                   _damping, Time.deltaTime);

            if (_hasCarrying && _agent != null)
                _animator.SetBool(CarryingId, _agent.IsCarrying);
        }

        private void OnStateChanged(FarmerAgent agent, FarmerState state)
        {
            if (_animator == null || !_hasHarvest) return;
            if (state == FarmerState.Harvesting) _animator.SetTrigger(HarvestId);
        }
    }
}

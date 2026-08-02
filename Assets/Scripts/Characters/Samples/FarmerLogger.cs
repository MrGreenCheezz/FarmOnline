using UnityEngine;
using Farm.Farming;

namespace Farm.Characters.Samples
{
    /// <summary>Рассказывает в консоль, чем занят фермер. Отладочный помощник, не геймплей.</summary>
    [AddComponentMenu("Farm/Samples/Farmer Logger")]
    public sealed class FarmerLogger : MonoBehaviour
    {
        [SerializeField] private FarmerAgent _agent;
        [SerializeField] private CharacterNeeds _needs;
        [SerializeField] private bool _logStates = true;

        [Tooltip("Как часто писать голод/жажду, секунд. 0 — не писать.")]
        [SerializeField, Min(0f)] private float _needsInterval = 5f;

        private float _timer;

        private void Awake()
        {
            if (_agent == null) _agent = GetComponent<FarmerAgent>();
            if (_needs == null) _needs = GetComponent<CharacterNeeds>();
        }

        private void OnEnable()
        {
            if (_agent != null)
            {
                _agent.StateChanged += OnStateChanged;
                _agent.Collected += OnCollected;
                _agent.Delivered += OnDelivered;
            }

            if (_needs != null)
            {
                _needs.BecameHungry += OnHungry;
                _needs.BecameThirsty += OnThirsty;
            }
        }

        private void OnDisable()
        {
            if (_agent != null)
            {
                _agent.StateChanged -= OnStateChanged;
                _agent.Collected -= OnCollected;
                _agent.Delivered -= OnDelivered;
            }

            if (_needs != null)
            {
                _needs.BecameHungry -= OnHungry;
                _needs.BecameThirsty -= OnThirsty;
            }
        }

        private void Update()
        {
            if (_needs == null || _needsInterval <= 0f) return;

            _timer += Time.deltaTime;
            if (_timer < _needsInterval) return;
            _timer = 0f;

            Debug.Log("[Фермер] сытость " + _needs.Satiety01.ToString("P0") +
                      ", вода " + _needs.Hydration01.ToString("P0"), this);
        }

        private void OnStateChanged(FarmerAgent a, FarmerState s)
        {
            if (!_logStates) return;
            string extra = a.Target != null ? " -> " + a.Target.name : "";
            Debug.Log("[Фермер] " + s + extra + "  (рюкзак: " + a.Inventory + ")", this);
        }

        private void OnCollected(FarmerAgent a, HarvestResult r) =>
            Debug.Log("[Фермер] взял " + r + "  рюкзак: " + a.Inventory, this);

        private void OnDelivered(FarmerAgent a, int amount) =>
            Debug.Log("[Фермер] донёс домой " + amount + " ед.", this);

        private void OnHungry(CharacterNeeds n) => Debug.Log("[Фермер] проголодался", this);
        private void OnThirsty(CharacterNeeds n) => Debug.Log("[Фермер] хочет пить", this);
    }
}

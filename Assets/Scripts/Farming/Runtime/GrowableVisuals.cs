using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Shows the mesh for the current stage. Entirely optional — <see cref="Growable"/> never
    /// references it, it just listens to the same events anything else would. Swap it for a
    /// shader-driven or animated version without touching the growth logic.
    /// </summary>
    [RequireComponent(typeof(Growable))]
    [AddComponentMenu("Farm/Growable Visuals")]
    public sealed class GrowableVisuals : MonoBehaviour
    {
        [Tooltip("Parent for spawned stage meshes. Defaults to this transform.")]
        [SerializeField] private Transform _anchor;

        [Tooltip("On: every stage mesh is spawned once and toggled with SetActive — no allocation " +
                 "on stage changes, at the cost of holding all stages in memory.\n" +
                 "Off: the current stage is spawned on demand and destroyed when it changes.")]
        [SerializeField] private bool _cacheStages = true;

        private Growable _growable;
        private GameObject[] _instances;
        private int _visibleStage = -1;

        private void Awake()
        {
            _growable = GetComponent<Growable>();
            if (_anchor == null) _anchor = transform;
        }

        private void OnEnable()
        {
            _growable.Planted += OnPlanted;
            _growable.StageAdvanced += OnStageAdvanced;
            _growable.Cleared += OnCleared;
            _growable.Withered += OnCleared;

            Show(_growable.Phase == GrowthPhase.Empty ? -1 : _growable.StageIndex);
        }

        private void OnDisable()
        {
            _growable.Planted -= OnPlanted;
            _growable.StageAdvanced -= OnStageAdvanced;
            _growable.Cleared -= OnCleared;
            _growable.Withered -= OnCleared;
        }

        private void OnDestroy() => DestroyInstances();

        private void OnPlanted(Growable g) => Show(g.StageIndex);
        private void OnStageAdvanced(Growable g, int stage) => Show(stage);
        private void OnCleared(Growable g) => Show(-1);

        /// <summary>Show stage <paramref name="stage"/>, or nothing when it is negative.</summary>
        private void Show(int stage)
        {
            if (_visibleStage == stage) return;

            var definition = _growable.Definition;
            if (definition == null) return;

            if (_cacheStages)
            {
                EnsureInstances(definition);

                if (_visibleStage >= 0 && _visibleStage < _instances.Length && _instances[_visibleStage] != null)
                    _instances[_visibleStage].SetActive(false);

                if (stage >= 0 && stage < _instances.Length && _instances[stage] != null)
                    _instances[stage].SetActive(true);
            }
            else
            {
                DestroyInstances();

                var prefab = definition.GetStage(stage)?.Visual;
                if (prefab != null)
                {
                    _instances = new GameObject[1];
                    _instances[0] = Instantiate(prefab, _anchor.position, _anchor.rotation, _anchor);
                }
            }

            _visibleStage = stage;
        }

        private void EnsureInstances(GrowableDefinition definition)
        {
            if (_instances != null && _instances.Length == definition.StageCount) return;

            DestroyInstances();
            _instances = new GameObject[definition.StageCount];

            for (int i = 0; i < _instances.Length; i++)
            {
                var prefab = definition.GetStage(i)?.Visual;
                if (prefab == null) continue;

                _instances[i] = Instantiate(prefab, _anchor.position, _anchor.rotation, _anchor);
                _instances[i].SetActive(false);
            }
        }

        private void DestroyInstances()
        {
            if (_instances == null) return;

            for (int i = 0; i < _instances.Length; i++)
                if (_instances[i] != null) Destroy(_instances[i]);

            _instances = null;
            _visibleStage = -1;
        }
    }
}

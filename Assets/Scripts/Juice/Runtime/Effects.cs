using System.Collections.Generic;
using UnityEngine;

namespace Farm.Juice
{
    /// <summary>
    /// Spawns one-shot particle effects, pooled per prefab.
    /// <para>
    /// Pooled rather than Instantiate/Destroy because effects fire hardest exactly when the game is
    /// busiest — a chain of merges — and that is the worst possible moment to be allocating and
    /// collecting GameObjects.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    [AddComponentMenu("Farm/Juice/Effects")]
    public sealed class Effects : MonoBehaviour
    {
        [SerializeField] private EffectLibrary _library;

        [Tooltip("Общий размер всех эффектов. Камера смотрит на ферму издалека, и эффект, " +
                 "выверенный вблизи, там превращается в несколько пикселей — этот множитель " +
                 "поднимает все разом, не трогая настройки каждого префаба.")]
        [SerializeField, Min(0.1f)] private float _globalScale = 1f;

        [Tooltip("Сколько копий одного эффекта держать наготове.")]
        [SerializeField, Min(1)] private int _poolPerEffect = 6;

        private readonly Dictionary<GameObject, List<ParticleSystem>> _pools =
            new Dictionary<GameObject, List<ParticleSystem>>();

        public static Effects Instance { get; private set; }
        public EffectLibrary Library => _library;

        /// <summary>Size multiplier applied to every effect. Tune it once for the camera distance.</summary>
        public float GlobalScale
        {
            get => _globalScale;
            set => _globalScale = Mathf.Max(0.1f, value);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Fire <paramref name="prefab"/> at a world position. Null prefab is a no-op.</summary>
        public void Spawn(GameObject prefab, Vector3 position, float scale = 1f)
        {
            if (prefab == null) return;

            var system = Rent(prefab);
            if (system == null) return;

            system.transform.position = position;

            // Масштаб трансформа тянет за собой и скорости частиц, а не только их размер —
            // именно это и нужно: крупный всплеск должен ещё и разлетаться шире.
            system.transform.localScale = Vector3.one * (scale * _globalScale);
            system.Clear(true);
            system.Play(true);
        }

        public static void Play(System.Func<EffectLibrary, GameObject> select, Vector3 position, float scale = 1f)
        {
            var effects = Instance;
            if (effects == null || effects._library == null || select == null) return;
            effects.Spawn(select(effects._library), position, scale);
        }

        private ParticleSystem Rent(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab, out var pool))
            {
                pool = new List<ParticleSystem>(_poolPerEffect);
                _pools[prefab] = pool;
            }

            // Свободной считаем ту, что уже отыграла.
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && !pool[i].isPlaying) return pool[i];

            if (pool.Count >= _poolPerEffect)
            {
                // Пул забит — переиспользуем самую старую, это лучше, чем не показать эффект вовсе.
                var oldest = pool[0];
                pool.RemoveAt(0);
                pool.Add(oldest);
                return oldest;
            }

            var instance = Instantiate(prefab, transform).GetComponent<ParticleSystem>();
            if (instance == null) return null;

            var main = instance.main;
            main.playOnAwake = false;
            main.stopAction = ParticleSystemStopAction.None;

            pool.Add(instance);
            return instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}

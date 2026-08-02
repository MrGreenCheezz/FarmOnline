using UnityEngine;

namespace Farm.Juice
{
    /// <summary>Скрытый хост корутин твинов — статический класс сам их запускать не умеет.</summary>
    [AddComponentMenu("")]
    public sealed class TweenRunner : MonoBehaviour
    {
        private static TweenRunner _instance;
        private static bool _quitting;

        public static TweenRunner Instance
        {
            get
            {
                if (_instance != null || _quitting || !Application.isPlaying) return _instance;

                var go = new GameObject("[TweenRunner]") { hideFlags = HideFlags.HideInHierarchy };
                _instance = go.AddComponent<TweenRunner>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }

        private void OnApplicationQuit() => _quitting = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _quitting = false;
        }
    }
}

using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Makes a small object drift and bob in place.
    /// <para>
    /// Exists for one reason: a firefly that holds perfectly still reads as a glowing pebble, and
    /// the player never thinks to click it. Movement is what says "this is alive, take it".
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Idle Bob")]
    public sealed class IdleBob : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _height = 0.22f;
        [SerializeField, Min(0f)] private float _drift = 0.35f;
        [SerializeField, Min(0.01f)] private float _speed = 0.8f;

        private Vector3 _origin;
        private float _phase;

        private void OnEnable()
        {
            _origin = transform.position;

            // Фаза из положения: рой не должен качаться одним телом.
            _phase = Mathf.Abs(_origin.x * 7.3f + _origin.z * 3.1f) % (Mathf.PI * 2f);
        }

        private void Update()
        {
            float t = Time.time * _speed + _phase;

            transform.position = _origin + new Vector3(
                Mathf.Sin(t * 0.7f) * _drift,
                Mathf.Sin(t) * _height + _height,
                Mathf.Cos(t * 0.53f) * _drift);
        }
    }
}

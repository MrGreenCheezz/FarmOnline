using UnityEngine;

namespace Farm.Farming.Samples
{
    /// <summary>
    /// Stand-in for the character that will eventually do this itself: every
    /// <see cref="_interval"/> seconds it grabs the nearest ripe growable and harvests it.
    /// Shows that the AI only needs <see cref="GrowableRegistry"/> — it never scans the scene
    /// and never touches a plot that is still growing.
    /// </summary>
    [AddComponentMenu("Farm/Samples/Auto Harvester")]
    public sealed class AutoHarvester : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _interval = 1f;

        [Tooltip("Reach in world units. 0 or less means unlimited.")]
        [SerializeField] private float _range;

        [SerializeField] private bool _filterByCategory;
        [SerializeField] private ResourceCategory _category = ResourceCategory.Crop;

        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < _interval) return;
            _timer = 0f;

            if (GrowableRegistry.ReadyCount == 0) return;

            ResourceCategory? filter = _filterByCategory ? _category : (ResourceCategory?)null;
            float range = _range > 0f ? _range : float.PositiveInfinity;

            var target = GrowableRegistry.FindNearestReady(transform.position, filter, range);
            target?.TryHarvest();
        }
    }
}

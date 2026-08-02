using UnityEngine;

namespace Farm.Farming.Samples
{
    /// <summary>
    /// Заглушка вместо персонажа, который однажды займётся этим сам: каждые
    /// <see cref="_interval"/> секунд берёт ближайшую спелую грядку и собирает её.
    /// Показывает, что ИИ достаточно одного <see cref="GrowableRegistry"/> — он никогда
    /// не сканирует сцену и не трогает ещё растущие грядки.
    /// </summary>
    [AddComponentMenu("Farm/Samples/Auto Harvester")]
    public sealed class AutoHarvester : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _interval = 1f;

        [Tooltip("Дальность в мировых единицах. 0 и меньше — без ограничений.")]
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

using UnityEngine;

namespace Farm.Farming.Samples
{
    /// <summary>
    /// Бросается в сцену и рассказывает каждое событие фермы в консоль.
    /// Заодно служит образцом подключения к системе: одна подписка на
    /// <see cref="FarmingEvents"/> — и видна вся ферма, без проводки к каждой грядке.
    /// </summary>
    [AddComponentMenu("Farm/Samples/Farming Event Logger")]
    public sealed class FarmingEventLogger : MonoBehaviour
    {
        [SerializeField] private bool _logStageTicks = true;

        private void OnEnable()
        {
            FarmingEvents.Planted += OnPlanted;
            FarmingEvents.StageAdvanced += OnStageAdvanced;
            FarmingEvents.Ready += OnReady;
            FarmingEvents.Harvested += OnHarvested;
            FarmingEvents.Withered += OnWithered;
            FarmingEvents.Cleared += OnCleared;
        }

        private void OnDisable()
        {
            FarmingEvents.Planted -= OnPlanted;
            FarmingEvents.StageAdvanced -= OnStageAdvanced;
            FarmingEvents.Ready -= OnReady;
            FarmingEvents.Harvested -= OnHarvested;
            FarmingEvents.Withered -= OnWithered;
            FarmingEvents.Cleared -= OnCleared;
        }

        private static string Name(Growable g)
        {
            var def = g.Definition;
            return g.name + " [" + (def != null ? def.Id : "?") + "]";
        }

        private void OnPlanted(Growable g) =>
            Debug.Log("[Farm] ПОСАЖЕНО   " + Name(g) + "  стадий: " + g.StageCount +
                      ", созреет через " + g.TimeUntilReady.ToString("F1") + " c", g);

        private void OnStageAdvanced(Growable g, int stage)
        {
            if (!_logStageTicks) return;
            var def = g.Definition;
            string label = def != null && def.GetStage(stage) != null ? def.GetStage(stage).Name : stage.ToString();
            Debug.Log("[Farm] СТАДИЯ " + stage + "/" + (g.StageCount - 1) + "  " + Name(g) + "  (" + label + ")", g);
        }

        private void OnReady(Growable g) =>
            Debug.Log("[Farm] СОЗРЕЛО    " + Name(g) + "  готовых на ферме: " + GrowableRegistry.ReadyCount, g);

        private void OnHarvested(Growable g, HarvestResult r) =>
            Debug.Log("[Farm] СОБРАНО    " + Name(g) + "  -> " + r, g);

        private void OnWithered(Growable g) =>
            Debug.Log("[Farm] ИСПОРТИЛОСЬ " + Name(g), g);

        private void OnCleared(Growable g) =>
            Debug.Log("[Farm] ОПУСТЕЛО   " + Name(g), g);
    }
}

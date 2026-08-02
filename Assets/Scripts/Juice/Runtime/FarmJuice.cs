using UnityEngine;
using Farm.Farming;

namespace Farm.Juice
{
    /// <summary>
    /// Turns farm events into things you can see and hear. The conductor: it owns no state and
    /// changes no gameplay, it only listens and reacts — delete it and the game still runs, just
    /// silently and flatly.
    /// <para>
    /// Effect strength is deliberately uneven. Merging is the decision the whole game is built on,
    /// so it gets the biggest punch, the brightest burst and a pitch that rises with level; routine
    /// harvesting gets a small squash. Giving every action the same weight is the same as giving
    /// none of them any.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Juice/Farm Juice")]
    public sealed class FarmJuice : MonoBehaviour
    {
        [Header("Сила отклика")]
        [SerializeField, Min(0f)] private float _mergePunch = 0.55f;
        [SerializeField, Min(0f)] private float _harvestSquash = 0.28f;
        [SerializeField, Min(0f)] private float _readyPunch = 0.18f;

        [Tooltip("Насколько выше звучит слияние с каждым уровнем — рост уровня должен слышаться.")]
        [SerializeField, Range(0f, 0.2f)] private float _mergePitchPerLevel = 0.06f;

        private Shop _shop;
        private Farm.Characters.FarmerAgent _farmer;

        private void OnEnable()
        {
            _farmer = FindFirstObjectByType<Farm.Characters.FarmerAgent>();
            if (_farmer != null) _farmer.Delivered += OnDelivered;

            FarmingEvents.Planted += OnPlanted;
            FarmingEvents.Ready += OnReady;
            FarmingEvents.Harvested += OnHarvested;
            FarmingEvents.Merged += OnMerged;
            FarmingEvents.Withered += OnWithered;
            BuildingRegistry.Upgraded += OnBuildingUpgraded;

            _shop = Shop.Instance != null ? Shop.Instance : FindFirstObjectByType<Shop>();
            if (_shop != null)
            {
                _shop.Bought += OnBought;
                _shop.Sold += OnSold;
                _shop.Refused += OnRefused;
            }
        }

        private void OnDisable()
        {
            FarmingEvents.Planted -= OnPlanted;
            FarmingEvents.Ready -= OnReady;
            FarmingEvents.Harvested -= OnHarvested;
            FarmingEvents.Merged -= OnMerged;
            FarmingEvents.Withered -= OnWithered;
            BuildingRegistry.Upgraded -= OnBuildingUpgraded;

            if (_shop != null)
            {
                _shop.Bought -= OnBought;
                _shop.Sold -= OnSold;
                _shop.Refused -= OnRefused;
            }
            _shop = null;

            if (_farmer != null) _farmer.Delivered -= OnDelivered;
            _farmer = null;
        }

        /// <summary>Coins landing at home — the payoff for the whole walk.</summary>
        private void OnDelivered(Farm.Characters.FarmerAgent agent, int amount)
        {
            Sfx.Play(b => b.Deliver);
            Effects.Play(l => l.Harvest, agent.HomePosition + Vector3.up * 0.5f, 1.3f);
        }

        // ---- ферма ----

        private void OnPlanted(Growable g)
        {
            if (g == null) return;

            Tween.PopIn(g.transform, 0.38f);
            Effects.Play(l => l.Plant, g.transform.position);
            Sfx.Play(b => b.Plant);
        }

        private void OnReady(Growable g)
        {
            if (g == null) return;

            Tween.Punch(g.transform, _readyPunch, 0.26f);
            Effects.Play(l => l.Ready, g.transform.position + Vector3.up * 0.5f);
            Sfx.Play(b => b.Ready);
        }

        private void OnHarvested(Growable g, HarvestResult result)
        {
            if (g == null) return;

            Tween.Squash(g.transform, _harvestSquash, 0.26f);
            Effects.Play(l => l.Harvest, g.transform.position + Vector3.up * 0.35f);
            Sfx.Play(b => b.Harvest);
        }

        private void OnMerged(Growable survivor, Growable absorbed)
        {
            if (survivor == null) return;

            // Заметно сильнее остальных: это то действие, ради которого игра существует.
            Tween.Punch(survivor.transform, _mergePunch, 0.42f);
            Effects.Play(l => l.MergeBurst, survivor.transform.position + Vector3.up * 0.4f,
                         1f + survivor.Level * 0.12f);

            PlayMergeSound(survivor.Level);
        }

        /// <summary>Higher level, higher pitch — progression you can hear without reading the badge.</summary>
        private void PlayMergeSound(int level)
        {
            var sfx = Sfx.Instance;
            if (sfx == null || sfx.Bank == null || sfx.Bank.Merge == null) return;

            var cue = sfx.Bank.Merge;
            float savedJitter = cue.PitchJitter;
            cue.PitchJitter = Mathf.Min(0.5f, savedJitter + Mathf.Clamp(level - 1, 0, 6) * _mergePitchPerLevel);
            sfx.Play(cue);
            cue.PitchJitter = savedJitter;
        }

        private void OnWithered(Growable g)
        {
            if (g == null) return;

            Tween.Squash(g.transform, 0.4f, 0.35f);
            Effects.Play(l => l.Wither, g.transform.position + Vector3.up * 0.3f);
            Sfx.Play(b => b.Wither);
        }

        // ---- экономика ----

        private void OnBought(Shop shop, ShopItemDefinition item) => Sfx.Play(b => b.Buy);
        private void OnSold(Shop shop, ResourceDefinition resource, int amount, int gold) => Sfx.Play(b => b.Sell);
        private void OnRefused(Shop shop, string reason) => Sfx.Play(b => b.Refused);

        /// <summary>
        /// Levelling a building costs as much as a merge and deserves to land as hard — the model
        /// never changes, so without the punch the only feedback is a number in a panel.
        /// </summary>
        private void OnBuildingUpgraded(Building building, int level)
        {
            if (building == null) return;

            Tween.Punch(building.transform, _mergePunch * 0.6f, 0.36f);
            Effects.Play(l => l.MergeBurst, building.transform.position + Vector3.up * 0.6f);
            Sfx.Play(b => b.Buy);
        }
    }
}

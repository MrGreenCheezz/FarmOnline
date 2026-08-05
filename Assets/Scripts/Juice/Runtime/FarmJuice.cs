using System.Collections.Generic;
using UnityEngine;
using Farm.Farming;

namespace Farm.Juice
{
    /// <summary>
    /// Превращает события фермы в то, что видно и слышно. Дирижёр: не владеет состоянием
    /// и не меняет геймплей, только слушает и реагирует — удали его, и игра продолжит
    /// работать, просто беззвучно и плоско.
    /// <para>
    /// Сила отклика нарочно неравномерна. Слияние — решение, на котором построена вся игра,
    /// поэтому ему достаётся самый сильный панч, самая яркая вспышка и тон, растущий с
    /// уровнем; рутинный сбор получает лёгкое сплющивание. Дать каждому действию одинаковый
    /// вес — то же самое, что не дать веса никакому.
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

        [Tooltip("Отклик на каждую пройденную ступень роста. Заметно слабее спелости и без звука: " +
                 "это происходит у десятка грядок сразу, и громкое оформление превратилось бы в " +
                 "трескотню. Смысл — чтобы поле дышало и рост было видно, а не слышно.")]
        [SerializeField, Min(0f)] private float _growPunch = 0.1f;

        [Tooltip("Насколько выше звучит слияние с каждым уровнем — рост уровня должен слышаться.")]
        [SerializeField, Range(0f, 0.2f)] private float _mergePitchPerLevel = 0.06f;

        [Tooltip("Как редко напоминать звуком о переполненном складе, секунд.")]
        [SerializeField, Min(0.5f)] private float _overflowInterval = 3f;

        private Shop _shop;
        private WorldStorage _storage;
        private float _lastOverflow;

        // Все жители, на которых мы сейчас подписаны, и чьё состояние помним поимённо:
        // дирижёру важен переход конкретного человека, а не «кого-то из деревни».
        private readonly List<Farm.Characters.FarmerAgent> _wiredFarmers =
            new List<Farm.Characters.FarmerAgent>();
        private readonly Dictionary<Farm.Characters.FarmerAgent, Farm.Characters.FarmerState> _farmerStates =
            new Dictionary<Farm.Characters.FarmerAgent, Farm.Characters.FarmerState>();

        private void OnEnable()
        {
            Farm.Characters.FarmerRegistry.Changed += RewireFarmers;
            RewireFarmers();

            _storage = WorldStorage.Instance != null ? WorldStorage.Instance : FindFirstObjectByType<WorldStorage>();
            if (_storage != null) _storage.Overflowed += OnStorageOverflowed;

            FarmingEvents.Planted += OnPlanted;
            FarmingEvents.StageAdvanced += OnStageAdvanced;
            FarmingEvents.Ready += OnReady;
            FarmingEvents.Harvested += OnHarvested;
            FarmingEvents.Merged += OnMerged;
            FarmingEvents.Withered += OnWithered;
            BuildingRegistry.Upgraded += OnBuildingUpgraded;

            EnsureCameraBreath();
            EnsureCritters();

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
            FarmingEvents.StageAdvanced -= OnStageAdvanced;
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

            if (_storage != null) _storage.Overflowed -= OnStorageOverflowed;
            _storage = null;

            Farm.Characters.FarmerRegistry.Changed -= RewireFarmers;
            UnwireFarmers();
        }

        /// <summary>
        /// Перевесить подписки на текущий состав жителей. Дирижёр слушает каждого:
        /// придёт второй житель — его доставки и пробуждения зазвучат сами, без правок здесь.
        /// </summary>
        private void RewireFarmers()
        {
            UnwireFarmers();

            var all = Farm.Characters.FarmerRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var farmer = all[i];
                if (farmer == null) continue;

                farmer.Delivered += OnDelivered;
                farmer.StateChanged += OnFarmerState;
                farmer.Improved += OnImproved;
                _wiredFarmers.Add(farmer);
                _farmerStates[farmer] = farmer.State;
            }
        }

        private void UnwireFarmers()
        {
            for (int i = 0; i < _wiredFarmers.Count; i++)
            {
                var farmer = _wiredFarmers[i];
                if (farmer == null) continue;

                farmer.Delivered -= OnDelivered;
                farmer.StateChanged -= OnFarmerState;
                farmer.Improved -= OnImproved;
            }

            _wiredFarmers.Clear();
            _farmerStates.Clear();
        }

        /// <summary>
        /// Потягивание после сна: проснуться — это движение тела, а не смена бита.
        /// Живёт здесь, а не в агенте: Characters не ссылается на Juice, и телесный
        /// отклик — забота дирижёра, как и весь остальной панч.
        /// </summary>
        private void OnFarmerState(Farm.Characters.FarmerAgent agent, Farm.Characters.FarmerState state)
        {
            _farmerStates.TryGetValue(agent, out var previous);

            if (previous == Farm.Characters.FarmerState.Sleeping &&
                state != Farm.Characters.FarmerState.Sleeping)
                Tween.Punch(agent.transform, 0.14f, 0.5f);

            _farmerStates[agent] = state;
        }

        /// <summary>
        /// Житель что-то построил. Появление из ничего + та же посадочная пыль, что у покупок:
        /// для игрока это одно событие «на ферме прибыло», кто бы его ни оплатил.
        /// </summary>
        private void OnImproved(Farm.Characters.FarmerAgent agent, ImprovementDefinition project, GameObject built)
        {
            if (built == null) return;

            Tween.PopIn(built.transform, 0.45f);
            Effects.Play(l => l.Place, built.transform.position);
            Sfx.Play(b => b.Plant);
        }

        /// <summary>
        /// Склад не принял груз. Звук тот же, что у отказа в магазине, — для игрока это одно
        /// и то же событие «не влезло», и второй звук для него был бы лишним словарём.
        /// Редко: полный склад отказывает каждой доставке, а звук в упор перестаёт значить.
        /// </summary>
        private void OnStorageOverflowed(ResourceDefinition resource, int amount)
        {
            if (Time.unscaledTime - _lastOverflow < _overflowInterval) return;
            _lastOverflow = Time.unscaledTime;
            Sfx.Play(b => b.Refused);
        }

        /// <summary>Груз доехал до дома — награда за весь путь.</summary>
        private void OnDelivered(Farm.Characters.FarmerAgent agent, int amount)
        {
            Sfx.Play(b => b.Deliver);
            Effects.Play(l => l.Harvest, agent.HomePosition + Vector3.up * 0.5f, 1.3f);
        }

        /// <summary>
        /// Повесить дыхание на игровую камеру, если его там ещё нет. Кодом, а не сценой:
        /// это оформление, и сцена не должна о нём знать — ровно по тем же основаниям,
        /// по которым голова фермера добавляется из его же Awake.
        /// </summary>
        private void EnsureCameraBreath()
        {
            var camera = Camera.main;
            if (camera == null) return;

            if (camera.GetComponent<CameraBreath>() == null)
                camera.gameObject.AddComponent<CameraBreath>();
        }

        /// <summary>
        /// Дневная живность — на себя же, если её не поставили в сцену руками. Хочешь крутить
        /// числа насовсем — добавь <see cref="AmbientCritters"/> в сцену, и эта проверка отступит.
        /// </summary>
        private void EnsureCritters()
        {
            if (FindFirstObjectByType<AmbientCritters>() == null)
                gameObject.AddComponent<AmbientCritters>();
        }

        // ---- ферма ----

        private void OnPlanted(Growable g)
        {
            if (g == null) return;

            Tween.PopIn(g.transform, 0.38f);
            Effects.Play(l => l.Plant, g.transform.position);
            Sfx.Play(b => b.Plant);
        }

        /// <summary>
        /// Каждая пройденная ступень роста — маленький вдох грядки.
        /// <para>
        /// Ради этого события всё и подключалось: рост занимает минуты, и без отклика поле
        /// выглядит застывшим — игрок узнаёт о прогрессе только по итогу. Тихо и слабо
        /// намеренно: ступени проходят у десятка грядок вперемешку.
        /// </para>
        /// </summary>
        private void OnStageAdvanced(Growable g, int stage)
        {
            // Последнюю ступень оформляет спелость, и вдвое дёргать одну грядку незачем.
            if (g == null || g.IsReady) return;

            Tween.Punch(g.transform, _growPunch, 0.3f);
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

        /// <summary>Выше уровень — выше тон: прогресс слышен и без чтения плашки.</summary>
        private void PlayMergeSound(int level)
        {
            var sfx = Sfx.Instance;
            if (sfx == null || sfx.Bank == null || sfx.Bank.Merge == null) return;

            float pitchOffset = Mathf.Clamp(level - 1, 0, 6) * _mergePitchPerLevel;
            sfx.Play(sfx.Bank.Merge, pitchOffset);
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
        /// Уровень постройки стоит как слияние и обязан приземляться так же весомо — модель
        /// не меняется, и без панча единственной обратной связью была бы цифра в панели.
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

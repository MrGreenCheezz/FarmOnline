using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>What a building does. The first two serve the character; the rest work on their own.</summary>
    public enum BuildingService
    {
        /// <summary>Feeds. Consumes food from world storage.</summary>
        Kitchen = 0,
        /// <summary>Waters. Costs nothing — water is not a resource, so thirst never deadlocks.</summary>
        Well = 1,
        /// <summary>Refines raw resources into better ones. Runs itself — see <see cref="Workshop"/>.</summary>
        Workshop = 2,
        /// <summary>Pays a bonus on every sale. Passive, nothing to visit.</summary>
        Market = 3
    }

    /// <summary>One rung of a building's upgrade ladder.</summary>
    [Serializable]
    public sealed class BuildingLevel
    {
        [Tooltip("Что стоит подняться на этот уровень. Для первого — цена постройки.")]
        public Price Cost;

        [Tooltip("Какую долю потребности закрывает один визит. 1 — полностью.\n" +
                 "Только для кухни и колодца — мастерская и рынок читают Output.")]
        [Range(0.05f, 1f)] public float Efficiency = 0.35f;

        [Tooltip("Выработка уровня для служб, которым доли мало: множитель скорости мастерской, " +
                 "надбавка рынка. Доля закрытой потребности ограничена единицей, а эти — нет.")]
        [Min(0f)] public float Output = 1f;

        [Tooltip("Подпись для игрока: что даёт этот уровень.")]
        public string Note;
    }

    /// <summary>
    /// One conversion a workshop can run: so much of one resource becomes so much of another.
    /// <para>
    /// Recipes live on the building rather than in their own asset because a recipe is meaningless
    /// without the workshop that runs it — splitting them would only add a reference to keep in sync.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class WorkshopRecipe
    {
        [Tooltip("Что забирается со склада.")]
        public ResourceDefinition Input;
        [Min(1)] public int InputAmount = 2;

        [Tooltip("Что кладётся обратно.")]
        public ResourceDefinition Output;
        [Min(1)] public int OutputAmount = 1;

        [Tooltip("Секунд на партию на первом уровне. Дальше делится на скорость уровня.")]
        [Min(0.1f)] public float Seconds = 6f;

        [Tooltip("С какого уровня постройки рецепт доступен.")]
        [Min(1)] public int UnlockLevel = 1;

        public bool IsValid => Input != null && Output != null && InputAmount > 0 && OutputAmount > 0;

        /// <summary>Gold the batch is worth. Picks which recipe runs when several are affordable.</summary>
        public int OutputValue => Output != null ? Output.SellPrice * OutputAmount : 0;

        public override string ToString()
        {
            if (!IsValid) return "<пустой рецепт>";
            return InputAmount + " " + Input.DisplayName + " -> " + OutputAmount + " " + Output.DisplayName;
        }
    }

    /// <summary>
    /// A building the player can place and upgrade. Data only — <see cref="Building"/> runs it.
    /// <para>
    /// Costs are stored per level rather than computed, so any rung can be retuned by hand; the
    /// generator fills them from <see cref="TierEconomy"/> so the default ladder stays consistent.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Farm/Building", fileName = "Building_")]
    public sealed class BuildingDefinition : ScriptableObject
    {
        [Header("Что это")]
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField, TextArea(2, 4)] private string _description;
        [SerializeField] private Sprite _icon;
        [SerializeField] private BuildingService _service = BuildingService.Kitchen;

        [Header("Модель")]
        [Tooltip("Что ставится на ферму. Один префаб на все уровни — уровень показывается плашкой.")]
        [SerializeField] private GameObject _prefab;

        [Header("Уровни")]
        [SerializeField] private BuildingLevel[] _levels = Array.Empty<BuildingLevel>();

        [Header("Работа")]
        [Tooltip("Сколько секунд занимает визит.")]
        [SerializeField, Min(0.1f)] private float _serviceDuration = 1.2f;

        [Tooltip("Базовая порция для колодца, в долях потребности. У кухни считается по еде.")]
        [SerializeField, Range(0.1f, 1f)] private float _baseRestore = 1f;

        [Header("Мастерская")]
        [Tooltip("Что эта постройка умеет перерабатывать. Только для службы Workshop.")]
        [SerializeField] private WorkshopRecipe[] _recipes = Array.Empty<WorkshopRecipe>();

        public string Id => string.IsNullOrEmpty(_id) ? name : _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? Id : _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public BuildingService Service => _service;
        public GameObject Prefab => _prefab;
        public float ServiceDuration => _serviceDuration;
        public float BaseRestore => _baseRestore;

        public IReadOnlyList<WorkshopRecipe> Recipes => _recipes;

        /// <summary>True when the character has to walk over for this to do anything.</summary>
        public bool IsVisited => _service == BuildingService.Kitchen || _service == BuildingService.Well;

        public int MaxLevel => _levels != null ? _levels.Length : 0;

        public BuildingLevel GetLevel(int level)
        {
            if (_levels == null || level < 1 || level > _levels.Length) return null;
            return _levels[level - 1];
        }

        public float EfficiencyAt(int level)
        {
            var data = GetLevel(level);
            return data != null ? data.Efficiency : 0f;
        }

        /// <summary>Workshop speed or market bonus at this level. 0 when the level does not exist.</summary>
        public float OutputAt(int level)
        {
            var data = GetLevel(level);
            return data != null ? data.Output : 0f;
        }

        /// <summary>Cost to reach <paramref name="level"/>. Null when that level does not exist.</summary>
        public Price? CostOf(int level)
        {
            var data = GetLevel(level);
            return data != null ? data.Cost : (Price?)null;
        }
    }
}

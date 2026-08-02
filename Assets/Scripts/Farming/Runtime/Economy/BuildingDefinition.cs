using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Что постройка делает. Первые две обслуживают персонажа; остальные работают сами.</summary>
    public enum BuildingService
    {
        /// <summary>Кормит. Расходует еду со склада мира.</summary>
        Kitchen = 0,
        /// <summary>Поит. Бесплатно — вода не ресурс, поэтому жажда никогда не заходит в тупик.</summary>
        Well = 1,
        /// <summary>Перерабатывает сырьё в лучшее. Работает сама — см. <see cref="Workshop"/>.</summary>
        Workshop = 2,
        /// <summary>Платит надбавку с каждой продажи. Пассивен, ходить не к чему.</summary>
        Market = 3
    }

    /// <summary>Одна ступень лестницы улучшений постройки.</summary>
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
    /// Одно превращение, доступное мастерской: столько-то одного ресурса становится столько-то другого.
    /// <para>
    /// Рецепты живут на постройке, а не в собственном ассете, потому что рецепт бессмыслен без
    /// мастерской, которая его крутит, — разделение лишь добавило бы ссылку, которую надо держать в согласии.
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

        /// <summary>Ценность партии в золоте. По ней выбирается рецепт, когда доступно несколько.</summary>
        public int OutputValue => Output != null ? Output.SellPrice * OutputAmount : 0;

        public override string ToString()
        {
            if (!IsValid) return "<пустой рецепт>";
            return InputAmount + " " + Input.DisplayName + " -> " + OutputAmount + " " + Output.DisplayName;
        }
    }

    /// <summary>
    /// Постройка, которую игрок ставит и улучшает. Только данные — работает ею <see cref="Building"/>.
    /// <para>
    /// Цены хранятся по уровням, а не считаются: любую ступень можно перекрутить руками;
    /// генератор заполняет их из <see cref="TierEconomy"/>, чтобы лестница по умолчанию
    /// оставалась согласованной.
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

        /// <summary>Истина, когда персонажу нужно подойти, чтобы постройка что-то сделала.</summary>
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

        /// <summary>Скорость мастерской или надбавка рынка этого уровня. 0, если уровня нет.</summary>
        public float OutputAt(int level)
        {
            var data = GetLevel(level);
            return data != null ? data.Output : 0f;
        }

        /// <summary>Цена достижения уровня <paramref name="level"/>. Null, если такого уровня нет.</summary>
        public Price? CostOf(int level)
        {
            var data = GetLevel(level);
            return data != null ? data.Cost : (Price?)null;
        }
    }
}

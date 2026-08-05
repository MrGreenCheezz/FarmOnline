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
        Market = 3,
        /// <summary>
        /// Постоянно усиливает всю ферму — что именно, говорит <see cref="BuildingDefinition.Boost"/>.
        /// Пассивна: работает фактом своего существования, ходить не к чему.
        /// </summary>
        Boost = 4
    }

    /// <summary>
    /// Что усиливает постройка-усилитель. Значения сериализуются — добавляй в конец.
    /// <para>
    /// Это нарочно список <i>узких мест</i>, а не список ресурсов: ускорять «пшеницу» —
    /// это контент, который придётся дописывать под каждый новый ассет, а ускорять «рост»
    /// работает для всего, что вырастет в игре потом. Выбор игрока при этом остаётся
    /// стратегическим: усилители дороги, и вкладываться сразу во все не выйдет.
    /// </para>
    /// </summary>
    public enum FarmBoost
    {
        None = 0,
        /// <summary>Множитель скорости роста всех грядок, загонов и жил.</summary>
        GrowthSpeed = 1,
        /// <summary>Добавка к урожаю каждого сбора, долей.</summary>
        HarvestYield = 2,
        /// <summary>Множитель скорости ходьбы фермера.</summary>
        MoveSpeed = 3,
        /// <summary>Дополнительные ячейки склада, штук. По природе глобален — радиус его не касается.</summary>
        StorageSlots = 4,
        /// <summary>Насколько медленнее садятся нужды фермера, долей.</summary>
        Vigor = 5,
        /// <summary>Защита от увядания: спелое в ауре не портится, сколько бы ни стояло.</summary>
        WitherGuard = 6
    }

    /// <summary>
    /// Кого касается аура усилителя. Значения сериализуются — добавляй в конец.
    /// <para>
    /// Фильтр — это личность постройки: пасека, которая усиливает и руду, — не пасека,
    /// а безликий «усилитель №3». Узкая аура и читается лучше, и дешевле стоит.
    /// </para>
    /// </summary>
    public enum AuraFilter
    {
        /// <summary>Всё, что растёт (или сам фермер — для его аур).</summary>
        Everything = 0,
        /// <summary>Только посевы и деревья.</summary>
        Crops = 1,
        /// <summary>Только живность.</summary>
        Livestock = 2,
        /// <summary>Только жилы.</summary>
        Ore = 3
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

        [Header("Усилитель")]
        [Tooltip("Что усиливает постройка. Только для службы Boost; величина берётся из Output уровня.")]
        [SerializeField] private FarmBoost _boost = FarmBoost.None;

        [Tooltip("Радиус действия на первом уровне, метров. 0 — вся ферма (для того, что глобально " +
                 "по природе, вроде ячеек склада). Радиус и делает постройку решением о размещении, " +
                 "а не строчкой в списке покупок.")]
        [SerializeField, Min(0f)] private float _auraRadius;

        [Tooltip("На сколько метров аура растёт с каждым уровнем после первого.")]
        [SerializeField, Min(0f)] private float _auraRadiusPerLevel = 1f;

        [Tooltip("Кого касается аура. Узкая — дешевле и читается как личность постройки.")]
        [SerializeField] private AuraFilter _auraFilter = AuraFilter.Everything;

        public string Id => string.IsNullOrEmpty(_id) ? name : _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? Id : _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public BuildingService Service => _service;

        /// <summary>Что усиливает. <see cref="FarmBoost.None"/> у всего, что не усилитель.</summary>
        public FarmBoost Boost => _service == BuildingService.Boost ? _boost : FarmBoost.None;

        /// <summary>Кого касается аура усилителя.</summary>
        public AuraFilter AuraFilter => _auraFilter;

        /// <summary>Радиус ауры на уровне. 0 — действует на всю ферму.</summary>
        public float AuraRadiusAt(int level) =>
            _auraRadius <= 0f ? 0f : _auraRadius + _auraRadiusPerLevel * Mathf.Max(0, level - 1);

        /// <summary>Подходит ли категория растимого под фильтр ауры.</summary>
        public bool AuraCovers(ResourceCategory category)
        {
            switch (_auraFilter)
            {
                case AuraFilter.Crops: return category == ResourceCategory.Crop;
                case AuraFilter.Livestock: return category == ResourceCategory.Livestock;
                case AuraFilter.Ore: return category == ResourceCategory.Ore;
                default: return true;
            }
        }

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

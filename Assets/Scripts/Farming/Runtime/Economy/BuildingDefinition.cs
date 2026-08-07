using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        /// <summary>
        /// Историческое: порча исключена решением владельца 06.08.2026, аура пуста.
        /// Значение держит совместимость сериализованных ассетов (пугало).
        /// </summary>
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

    /// <summary>Одна строка сырья в рецепте: столько-то такого-то ресурса.</summary>
    [Serializable]
    public sealed class RecipeInput
    {
        public ResourceDefinition Resource;
        [Min(1)] public int Amount = 1;

        public bool IsValid => Resource != null && Amount > 0;
    }

    /// <summary>
    /// Одно превращение, доступное мастерской: перечисленное сырьё становится столько-то другого.
    /// <para>
    /// Рецепты живут на постройке, а не в собственном ассете, потому что рецепт бессмыслен без
    /// мастерской, которая его крутит, — разделение лишь добавило бы ссылку, которую надо держать в согласии.
    /// </para>
    /// <para>
    /// Строк сырья может быть несколько (хлеб = мука + вода): пока вход был один, все 48 ресурсов
    /// жили независимыми вертикалями «сырьё → слиток», и держать выгодно было ту линию, что даёт
    /// больше золота в час. Составной рецепт связывает линии между собой — это и есть глубина
    /// ресурсов, ради которой он заведён (07.08.2026, docs/PLAN-RESOURCES.md).
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class WorkshopRecipe
    {
        [Tooltip("Что забирается со склада. Несколько строк — составной рецепт: партия начнётся, " +
                 "только когда на складе есть ВСЁ перечисленное.")]
        public RecipeInput[] Inputs = Array.Empty<RecipeInput>();

        [Tooltip("Что кладётся обратно.")]
        public ResourceDefinition Output;
        [Min(1)] public int OutputAmount = 1;

        [Tooltip("Секунд на партию на первом уровне. Дальше делится на скорость уровня.")]
        [Min(0.1f)] public float Seconds = 6f;

        [Tooltip("С какого уровня постройки рецепт доступен.")]
        [Min(1)] public int UnlockLevel = 1;

        // Наследие одиночного входа: ассеты, написанные до составных рецептов, держат сырьё
        // в этих двух полях. Читаются они через FormerlySerializedAs и раздаются наружу как
        // одна строка — благодаря этому переход не потребовал ни одной правки ассетов, и
        // партия в чужом сейве не потерялась. Инструмент «Farm → Онлайн → Перевести рецепты
        // на составные» переносит их в Inputs; после переноса поля стоят пустыми.
        [SerializeField, HideInInspector, FormerlySerializedAs("Input")]
        private ResourceDefinition _legacyInput;

        [SerializeField, HideInInspector, FormerlySerializedAs("InputAmount")]
        private int _legacyInputAmount = 2;

        /// <summary>Сколько строк сырья у рецепта. Ноль — рецепт пуст.</summary>
        public int InputCount
        {
            get
            {
                if (Inputs != null && Inputs.Length > 0) return Inputs.Length;
                return _legacyInput != null ? 1 : 0;
            }
        }

        public ResourceDefinition InputResourceAt(int index)
        {
            if (Inputs != null && Inputs.Length > 0)
                return index >= 0 && index < Inputs.Length && Inputs[index] != null
                    ? Inputs[index].Resource : null;

            return index == 0 ? _legacyInput : null;
        }

        public int InputAmountAt(int index)
        {
            if (Inputs != null && Inputs.Length > 0)
                return index >= 0 && index < Inputs.Length && Inputs[index] != null
                    ? Inputs[index].Amount : 0;

            return index == 0 ? _legacyInputAmount : 0;
        }

        /// <summary>Наследие для инструмента миграции: что лежит в старых полях.</summary>
        public bool CaptureLegacy(out ResourceDefinition resource, out int amount)
        {
            resource = _legacyInput;
            amount = _legacyInputAmount;
            return _legacyInput != null;
        }

        /// <summary>Перенести наследие в <see cref="Inputs"/>. Зовёт только редакторский инструмент.</summary>
        public void AdoptLegacy()
        {
            if (_legacyInput == null) return;

            if (Inputs == null || Inputs.Length == 0)
                Inputs = new[] { new RecipeInput { Resource = _legacyInput, Amount = _legacyInputAmount } };

            _legacyInput = null;
        }

        public bool IsValid
        {
            get
            {
                if (Output == null || OutputAmount <= 0) return false;

                int lines = InputCount;
                if (lines == 0) return false;

                // Каждая строка обязана быть целой: полустрока молча превратила бы составной
                // рецепт в более дешёвый, и игрок получил бы хлеб без муки.
                for (int i = 0; i < lines; i++)
                    if (InputResourceAt(i) == null || InputAmountAt(i) <= 0) return false;

                return true;
            }
        }

        /// <summary>Ценность партии в золоте. По ней выбирается рецепт, когда доступно несколько.</summary>
        public int OutputValue => Output != null ? Output.SellPrice * OutputAmount : 0;

        /// <summary>Во что обходится партия по продажным ценам сырья — для маржи и подсказок.</summary>
        public int InputValue
        {
            get
            {
                int total = 0;
                int lines = InputCount;
                for (int i = 0; i < lines; i++)
                {
                    var resource = InputResourceAt(i);
                    if (resource != null) total += resource.SellPrice * InputAmountAt(i);
                }
                return total;
            }
        }

        public override string ToString()
        {
            if (!IsValid) return "<пустой рецепт>";

            var sb = new System.Text.StringBuilder();
            int lines = InputCount;
            for (int i = 0; i < lines; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(InputAmountAt(i)).Append(' ').Append(InputResourceAt(i).DisplayName);
            }

            return sb.Append(" -> ").Append(OutputAmount).Append(' ').Append(Output.DisplayName).ToString();
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
        [Tooltip("Что эта постройка умеет перерабатывать. Рецепты делают станком любую постройку, " +
                 "а не только службу Workshop: кухня печёт и продолжает кормить, ветряк мелет и " +
                 "продолжает подгонять рост.")]
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

        [Tooltip("Вкл: в постройке копится корм для скотины (FarmFeed), потолок растёт с её уровнем.\n" +
                 "Отдельный признак, а не служба: хлев служит аурой роста живности, и менять\n" +
                 "одну механику на другую вместо того, чтобы добавить, — потеря, а не размен.")]
        [SerializeField] private bool _givesFeed;

        public string Id => string.IsNullOrEmpty(_id) ? name : _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? Id : _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public BuildingService Service => _service;

        /// <summary>Что усиливает. <see cref="FarmBoost.None"/> у всего, что не усилитель.</summary>
        public FarmBoost Boost => _service == BuildingService.Boost ? _boost : FarmBoost.None;

        /// <summary>Кого касается аура усилителя.</summary>
        public AuraFilter AuraFilter => _auraFilter;

        /// <summary>Копится ли в постройке корм для скотины. См. <see cref="FarmFeed"/>.</summary>
        public bool GivesFeed => _givesFeed;

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

        /// <summary>
        /// Есть ли у постройки хоть одно живое превращение. По этому, а не по службе, на неё
        /// вешается <c>Workshop</c>: служба у постройки одна, а кухне нужно и кормить, и печь.
        /// Привязка станка к службе означала бы, что вторая роль стоит первой.
        /// </summary>
        public bool HasRecipes
        {
            get
            {
                if (_recipes == null) return false;
                for (int i = 0; i < _recipes.Length; i++)
                    if (_recipes[i] != null && _recipes[i].IsValid) return true;
                return false;
            }
        }

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

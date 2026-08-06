using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;
using Farm.Characters;
using Farm.Net;

namespace Farm.UI
{
    /// <summary>
    /// Постоянный HUD: золото, состояние фермера, статус фермы и кнопки, открывающие склад и
    /// магазин. Ему же принадлежит общее меню продажи, которое переиспользует <see cref="InventoryWindow"/>.
    /// <para>
    /// Раскладка и внешний вид живут в <c>GameHud.uxml</c> / <c>GameHud.uss</c>; этот класс только
    /// заталкивает значения в именованные элементы, поэтому смена стиля не трогает C#.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Game HUD")]
    public sealed class GameHud : MonoBehaviour
    {
        [Tooltip("Пусто — найдётся первый в сцене.")]
        [SerializeField] private FarmerAgent _farmer;

        [Tooltip("Как часто обновлять полоски и таймеры, секунд.")]
        [SerializeField, Min(0.02f)] private float _refreshInterval = 0.1f;

        private const string PrefCollapsed = "hud.collapsed";
        private const string PrefBadges = "hud.badges";

        /// <summary>Колонка статуса отступила: игрок ведёт ношу и должен видеть поле под ней.</summary>
        private const string BusyClass = "hud-panels--busy";

        private UIDocument _document;
        private ShopWindow _shop;
        private InventoryWindow _inventory;
        private SettingsWindow _settings;
        private VisualElement _hud;
        private VisualElement _panels;
        private Button _collapseButton;
        private bool _collapsed;

        private PlotLevelBadges _badges;
        private Button _badgesButton;

        /// <summary>Вкладки жителей над панелью фермера. Null, пока житель один.</summary>
        private VisualElement _residentTabs;

        /// <summary>Ведомость жалования: сколько нанято и почём сутки. Живёт под вкладками.</summary>
        private Label _wageSummary;

        /// <summary>Игрок выбрал вкладку сам — авто-переключение на жителя №1 больше не трогает выбор.</summary>
        private bool _followChosen;

        private VisualElement _root;
        private VisualElement _screen;
        private Label _goldValue;
        private Label _clockValue;
        private Label _waterValue;
        private Label _levelValue;
        private DayNightCycle _clock;
        private Label _storageSummary;
        private Label _farmerState;
        private Label _farmerThought;
        private Label _satietyLabel;
        private Label _hydrationLabel;
        private Label _energyLabel;
        private Label _carryLabel;
        private Label _plotsReady;
        private Label _plotsNext;
        private Label _netStatus;
        private Label _farmerHire;
        private Button _hireButton;
        private Label _skillNext;

        /// <summary>Пока идёт — в строке найма висит отказ, и Refresh её не затирает.</summary>
        private float _hireMessageTimer;
        private string _hireMessage;

        private Label _farmLevel;
        private Label _farmLevelNote;
        private Button _farmExpand;

        /// <summary>То же самое для строки уровня фермы: один механизм на оба сообщения.</summary>
        private float _farmMessageTimer;
        private string _farmMessage;

        private VisualElement _satietyFill;
        private VisualElement _hydrationFill;
        private VisualElement _energyFill;
        private VisualElement _carryFill;
        private VisualElement _skillList;
        private VisualElement _contextMenu;
        private VisualElement _contextItems;
        private Label _contextTitle;

        private CharacterNeeds _needs;
        private IInventory _storage;
        private Wallet _wallet;
        private float _timer;

        /// <summary>Пара ссылок на живые элементы одной строки навыка.</summary>
        private struct SkillRow
        {
            public Label Level;
            public VisualElement Fill;
        }

        // Одна строка на навык, собирается один раз — перестраивать её каждый кадр значит
        // выбрасывать десяток объектов в секунду ради четырёх меняющихся чисел.
        private readonly Dictionary<FarmerSkill, SkillRow> _skillRows = new Dictionary<FarmerSkill, SkillRow>();

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            _shop = GetComponent<ShopWindow>();
            _inventory = GetComponent<InventoryWindow>();
            _settings = GetComponent<SettingsWindow>();
            _collapsed = PlayerPrefs.GetInt(PrefCollapsed, 0) == 1;

            var root = _document != null ? _document.rootVisualElement : null;
            if (root == null)
            {
                Debug.LogError("[HUD] У UIDocument нет корня — не назначен Source Asset?", this);
                enabled = false;
                return;
            }

            _root = root;
            _goldValue = root.Q<Label>("gold-value");
            _clockValue = root.Q<Label>("clock-value");
            _waterValue = root.Q<Label>("water-value");
            _levelValue = root.Q<Label>("level-value");
            _storageSummary = root.Q<Label>("storage-summary");
            _farmerState = root.Q<Label>("farmer-state");
            _farmerThought = root.Q<Label>("farmer-thought");
            _satietyLabel = root.Q<Label>("satiety-label");
            _hydrationLabel = root.Q<Label>("hydration-label");
            _energyLabel = root.Q<Label>("energy-label");
            _carryLabel = root.Q<Label>("carry-label");
            _plotsReady = root.Q<Label>("plots-ready");
            _plotsNext = root.Q<Label>("plots-next");
            _netStatus = root.Q<Label>("net-status");
            _farmerHire = root.Q<Label>("farmer-hire");
            _hireButton = root.Q<Button>("hire-button");
            if (_hireButton != null) _hireButton.clicked += OnHireClicked;
            _farmLevel = root.Q<Label>("farm-level");
            _farmLevelNote = root.Q<Label>("farm-level-note");
            _farmExpand = root.Q<Button>("farm-expand");
            if (_farmExpand != null) _farmExpand.clicked += OnExpandClicked;
            _skillNext = root.Q<Label>("skill-next");
            _satietyFill = root.Q<VisualElement>("satiety-fill");
            _hydrationFill = root.Q<VisualElement>("hydration-fill");
            _energyFill = root.Q<VisualElement>("energy-fill");
            _carryFill = root.Q<VisualElement>("carry-fill");
            _skillList = root.Q<VisualElement>("skill-list");
            _contextMenu = root.Q<VisualElement>("context-menu");
            _contextItems = root.Q<VisualElement>("ctx-items");
            _contextTitle = root.Q<Label>("ctx-title");

            root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);

            _screen = root.Q<VisualElement>("screen") ?? root;
            _screen.RegisterCallback<GeometryChangedEvent>(OnScreenResized);
            ApplyViewport();

            var shopButton = root.Q<Button>("shop-button");
            if (shopButton != null) shopButton.clicked += () => { CloseSellMenu(); _shop?.Toggle(); };

            var inventoryButton = root.Q<Button>("inventory-button");
            if (inventoryButton != null) inventoryButton.clicked += () => { CloseSellMenu(); _inventory?.Toggle(); };

            var settingsButton = root.Q<Button>("settings-button");
            if (settingsButton != null) settingsButton.clicked += () => { CloseSellMenu(); _settings?.Toggle(); };

            _hud = root.Q<VisualElement>("hud");
            _panels = root.Q<VisualElement>("hud-panels");
            _collapseButton = root.Q<Button>("collapse-button");
            if (_collapseButton != null) _collapseButton.clicked += TogglePanels;
            ApplyCollapsed();

            _badges = GetComponent<PlotLevelBadges>();
            _badgesButton = root.Q<Button>("badges-button");
            if (_badgesButton != null) _badgesButton.clicked += ToggleBadges;

            // Выбор игрока должен пережить перезапуск: настройка, слетающая при рестарте,
            // не настройка.
            if (_badges != null) _badges.Visible = PlayerPrefs.GetInt(PrefBadges, 1) == 1;
            ApplyBadges();

            // Панель следит за первым жителем; появятся другие — HUD не сломается, а выбор
            // «за кем следить» станет отдельной задачей интерфейса.
            if (_farmer == null) _farmer = FarmerRegistry.Primary;
            if (_farmer == null) _farmer = FindFirstObjectByType<FarmerAgent>();
            if (_farmer != null) _needs = _farmer.GetComponent<CharacterNeeds>();

            BuildResidentTabs();
            BuildSkillRows();

            // После сборки строк навыков: они создаются кодом и приходят с обычным picking.
            MakeReadout(_hud);

            // Жители регистрируются в OnEnable, а порядок OnEnable между объектами Unity
            // не обещает: на этом кадре реестр может быть ещё пуст. Пересобираем вкладки
            // по событию — когда состав действительно известен.
            FarmerRegistry.Changed += OnResidentsChanged;

            DragFocus.Changed += OnCarryChanged;
            GatherFocus.Changed += OnGatherChanged;
            ApplyBusy();

            NetStatus.Changed += OnNetStatusChanged;
            RefreshNetStatus(NetStatus.Line);

            // Уровень меняется и без нажатия этой кнопки — например, когда партия только
            // загрузилась. Подписка, а не одна отрисовка в OnEnable.
            FarmLevels.Changed += OnFarmLevelChanged;

            FarmWater.Changed += OnCareChanged;
            FarmFertilizer.Changed += OnCareChanged;
            FarmExperience.Changed += OnXpChanged;
            FarmExperience.LevelUp += OnLevelUp;

            Bind();
            Refresh();
        }

        private void OnDisable()
        {
            if (_storage != null) _storage.Changed -= OnStorageChanged;
            if (_wallet != null) _wallet.Changed -= OnWalletChanged;
            if (_clock != null) _clock.DayStarted -= OnDayStarted;
            if (_root != null) _root.UnregisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            if (_screen != null) _screen.UnregisterCallback<GeometryChangedEvent>(OnScreenResized);

            FarmerRegistry.Changed -= OnResidentsChanged;
            DragFocus.Changed -= OnCarryChanged;
            GatherFocus.Changed -= OnGatherChanged;
            NetStatus.Changed -= OnNetStatusChanged;
            FarmLevels.Changed -= OnFarmLevelChanged;
            FarmWater.Changed -= OnCareChanged;
            FarmFertilizer.Changed -= OnCareChanged;
            FarmExperience.Changed -= OnXpChanged;
            FarmExperience.LevelUp -= OnLevelUp;

            _storage = null;
            _wallet = null;
            _clock = null;
            _root = null;
            _screen = null;
        }

        /// <summary>
        /// Три класса на корне — единственный способ дать вёрстке узнать размер экрана:
        /// медиазапросов в USS нет, а угадать телефон по одному числу нельзя.
        /// <para>
        /// Пороги в логических пикселях, а не в физических: PanelSettings масштабирует всё под
        /// эталон 1920×1080 при <c>m_Match: 0.5</c>, и телефон 1080×2340 приезжает сюда как
        /// 978×2119. Считать по <see cref="Screen"/> значило бы мерить не ту линейку.
        /// </para>
        /// <para>
        /// 1100 — не круглое число, а щель: любое 16:9 даёт ровно 1920×1080 логических,
        /// поэтому порог по высоте обязан быть выше 1080 (иначе эталонный экран остаётся
        /// «высоким» и колонка HUD не влезает), а порог по ширине — ниже 1920 и выше
        /// портретных 978.
        /// </para>
        /// </summary>
        private void ApplyViewport()
        {
            if (_screen == null) return;

            float w = _screen.resolvedStyle.width;
            float h = _screen.resolvedStyle.height;
            if (w <= 0f || h <= 0f) return;

            _screen.EnableInClassList("vp--narrow", w < 1100f);
            _screen.EnableInClassList("vp--short", h < 1100f);
            _screen.EnableInClassList("vp--wide", w >= 1400f);
        }

        private void OnScreenResized(GeometryChangedEvent evt) => ApplyViewport();

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < _refreshInterval) return;
            _timer = 0f;
            Refresh();
        }

        private void Bind()
        {
            if (_storage == null)
            {
                _storage = FarmingRuntime.Sink as IInventory;
                if (_storage != null) _storage.Changed += OnStorageChanged;
            }

            if (_wallet == null)
            {
                _wallet = Wallet.Instance != null ? Wallet.Instance : FindFirstObjectByType<Wallet>();
                if (_wallet != null) _wallet.Changed += OnWalletChanged;
            }

            if (_clock == null)
            {
                _clock = DayNightCycle.Instance;
                if (_clock != null) _clock.DayStarted += OnDayStarted;
            }
        }

        private void OnStorageChanged(IInventory inventory) => RefreshStorageSummary();

        private void OnWalletChanged(Wallet wallet, int delta)
        {
            RefreshGold();

            // Счётчик обязан отреагировать физически: цифра, которая просто меняется,
            // не читается как награда.
            Punch(_goldValue, delta > 0 ? 0.28f : 0.14f);
        }

        /// <summary>
        /// Заря — единственная веха, которая приходит сама, без действий игрока. Раньше
        /// её не отмечало вообще ничто: `DayStarted` поднимался в пустоту. Отмечаем скромно,
        /// самой плашкой дня, — это раз в десять минут, и фанфары тут были бы навязчивы.
        /// </summary>
        private void OnDayStarted(DayNightCycle clock, int day)
        {
            RefreshClock();
            Punch(_clockValue, 0.3f);
        }

        private void Punch(Label label, float strength)
        {
            if (label != null && isActiveAndEnabled) StartCoroutine(PunchRoutine(label, strength));
        }

        private System.Collections.IEnumerator PunchRoutine(Label label, float strength)
        {
            const float duration = 0.26f;
            float t = 0f;

            // Немасштабируемое время: отклик интерфейса не должен зависеть от скорости игры.
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / duration;
                float s = 1f + Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * strength;
                label.style.scale = new StyleScale(new Scale(new Vector2(s, s)));
                yield return null;
            }

            label.style.scale = new StyleScale(new Scale(Vector2.one));
        }

        private void Refresh()
        {
            Bind();
            RefreshGold();
            RefreshClock();
            RefreshWater();
            RefreshLevel();
            RefreshStorageSummary();
            RefreshFarmer();
            RefreshFarm();
            RefreshSkills();
        }

        private void RefreshGold()
        {
            // Подпись словом, а не значком монеты: в шрифте темы таких глифов нет, рисуется квадрат.
            if (_goldValue != null) _goldValue.text = (_wallet != null ? _wallet.Gold : 0) + " зол.";
        }

        private void RefreshClock()
        {
            if (_clockValue == null) return;

            var clock = DayNightCycle.Instance;
            _clockValue.text = clock != null ? "День " + clock.Day + "   " + clock.ClockText : "—";
        }

        /// <summary>
        /// Сколько вёдер в колодце. Показание, а не кнопка: полив делается жестом по грядке,
        /// а здесь игрок видит, на сколько грядок его хватит.
        /// <para>
        /// Без колодца счётчик уходит совсем — пустая «0/0» читалась бы как поломка, тогда как
        /// на деле игроку просто нечего было построить.
        /// </para>
        /// </summary>
        private void RefreshWater()
        {
            if (_waterValue == null) return;

            int capacity = FarmWater.Capacity;
            bool show = capacity > 0;

            _waterValue.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            string text = "Вода " + FarmWater.Charges + "/" + capacity;

            // Неполный колодец говорит, когда набежит ведро: накопление, которого не видно,
            // читается как «перезарядка-стена», хотя вода капает всё время (живой отзыв).
            double next = FarmWater.SecondsToNext;
            if (next > 0.0)
                text += next < 3600.0
                    ? " · ведро через " + Mathf.CeilToInt((float)(next / 60.0)) + " м"
                    : " · ведро через " + Mathf.CeilToInt((float)(next / 3600.0)) + " ч";

            // Подкормка приписывается к той же строке, а не заводит свою: она бывает не всегда,
            // и пустая ячейка «Подкормка 0» в топбаре читалась бы как недоделка. Топбар и без
            // того несёт восемь кнопок.
            int fertilizer = FarmFertilizer.Charges;
            if (fertilizer > 0) text += "   Подкормка " + fertilizer;

            _waterValue.text = text;
        }

        /// <summary>
        /// Уровень игрока с остатком до следующего. Дробь, а не полоса: в топбаре полосе
        /// не хватит места быть читаемой, а «120/250» отвечает на тот же вопрос точнее.
        /// </summary>
        private void RefreshLevel()
        {
            if (_levelValue == null) return;

            _levelValue.text = FarmExperience.Level >= FarmExperience.MaxLevel
                ? "Ур. " + FarmExperience.Level
                : "Ур. " + FarmExperience.Level + " · " + FarmExperience.IntoLevel + "/" + FarmExperience.LevelCost;
        }

        /// <summary>Уровень взят — топбар вздрагивает, чтобы момент не прошёл незамеченным.</summary>
        private void OnLevelUp(int level)
        {
            RefreshLevel();
            Punch(_levelValue, 0.5f);
        }

        private void OnCareChanged() => RefreshWater();

        private void OnXpChanged() => RefreshLevel();

        // ---- навыки ----

        /// <summary>
        /// Вкладки жителей под заголовком «ФЕРМЕР». При единственном жителе не строятся
        /// вовсе: панель выглядит как в эпоху одного фермера, лишней хромоты в HUD нет.
        /// Состав жителей за партию не меняется, поэтому строится один раз в OnEnable.
        /// </summary>
        /// <summary>Состав жителей стал известен или поменялся — вкладки и слежка заново.</summary>
        private void OnResidentsChanged()
        {
            // Пока игрок не выбрал сам, панель следит за жителем №1 — им же, каким бы
            // ни был порядок регистрации: назначение ростера приходит этим же событием.
            if (!_followChosen && FarmerRegistry.Primary != null && _farmer != FarmerRegistry.Primary)
            {
                _farmer = FarmerRegistry.Primary;
                _needs = _farmer.GetComponent<CharacterNeeds>();
            }

            BuildResidentTabs();
        }

        private void BuildResidentTabs()
        {
            var panel = _root != null ? _root.Q<VisualElement>("panel-farmer") : null;
            if (panel == null) return;

            if (_residentTabs != null) { _residentTabs.RemoveFromHierarchy(); _residentTabs = null; }
            if (_wageSummary != null) { _wageSummary.RemoveFromHierarchy(); _wageSummary = null; }

            // Житель №1 — первым: порядок вкладок это порядок состава, а не гонка
            // регистраций (порядок OnEnable Unity не обещает).
            var residents = new List<FarmerAgent>();
            var primary = FarmerRegistry.Primary;
            if (primary != null) residents.Add(primary);
            foreach (var agent in FarmerRegistry.All)
                if (agent != null && agent != primary) residents.Add(agent);

            if (residents.Count < 2)
            {
                // Вкладок нет — и заголовок обязан вернуться к правде одного фермера.
                var soloTitle = panel.Q<Label>(className: "panel__title");
                if (soloTitle != null) soloTitle.text = "ФЕРМЕР";
                return;
            }

            _residentTabs = new VisualElement();
            _residentTabs.AddToClassList("resident-tabs");

            // Контейнер строится после общего прохода MakeReadout, поэтому прозрачность
            // для кликов ему выставляется здесь: ловят только сами кнопки-вкладки.
            _residentTabs.pickingMode = PickingMode.Ignore;

            foreach (var resident in residents)
            {
                var self = resident;
                var tab = new Button(() => FollowResident(self)) { text = self.name, userData = self };
                tab.AddToClassList("resident-tab");
                _residentTabs.Add(tab);
            }

            panel.Insert(1, _residentTabs);   // сразу под заголовком, выше состояния

            // Ведомость: одна строка вместо окна — при трёх жителях игроку нужна сумма,
            // а не бухгалтерия. Пер-жителя наём остаётся строкой выбранной вкладки.
            _wageSummary = new Label();
            _wageSummary.AddToClassList("row");
            _wageSummary.AddToClassList("row--muted");
            _wageSummary.pickingMode = PickingMode.Ignore;   // строится после прохода MakeReadout
            panel.Insert(2, _wageSummary);
            RefreshWageSummary();

            // Заголовок панели говорит правду: над вкладками двоих «ФЕРМЕР» — враньё.
            var title = panel.Q<Label>(className: "panel__title");
            if (title != null) title.text = "ЖИТЕЛИ";

            RefreshResidentTabs();
        }

        /// <summary>Панель следит дальше за этим жителем.</summary>
        private void FollowResident(FarmerAgent resident)
        {
            if (resident == null || resident == _farmer) return;

            _followChosen = true;
            _farmer = resident;
            _needs = resident.GetComponent<CharacterNeeds>();
            RefreshResidentTabs();
            Refresh();
        }

        private void RefreshResidentTabs()
        {
            if (_residentTabs == null) return;

            foreach (var child in _residentTabs.Children())
                child.EnableInClassList("resident-tab--on", ReferenceEquals(child.userData, _farmer));
        }

        /// <summary>
        /// Ведомость одной строкой: сколько работников на жаловании и почём сутки.
        /// Сумма — по нанятым, а не по всем: ненанятый живёт бытом и не стоит ничего.
        /// </summary>
        private void RefreshWageSummary()
        {
            if (_wageSummary == null) return;

            int total = 0, hired = 0, wage = 0;
            foreach (var resident in FarmerRegistry.All)
            {
                if (resident == null) continue;

                // Сторож вне ведомости: он не нанимается, и «нанято 2 из 4» с вечной
                // недостачей читалось бы как невыполнимая задача.
                if (resident.Role == ResidentRole.Watchman) continue;

                total++;
                if (!resident.IsHired) continue;
                hired++;
                wage += resident.RoleWagePerDay;
            }

            _wageSummary.text = hired > 0
                ? "на жаловании " + hired + " из " + total + " · " + wage + " зол./сутки"
                : "никто не нанят — все живут бытом";
        }

        private void BuildSkillRows()
        {
            if (_skillList == null) return;

            _skillList.Clear();
            _skillRows.Clear();

            foreach (FarmerSkill skill in System.Enum.GetValues(typeof(FarmerSkill)))
            {
                var row = new VisualElement();
                row.AddToClassList("skill");

                var head = new VisualElement();
                head.AddToClassList("skill__head");

                var name = new Label(FarmerSkills.NameOf(skill));
                name.AddToClassList("skill__name");
                head.Add(name);

                var level = new Label("1");
                level.AddToClassList("skill__level");
                head.Add(level);

                row.Add(head);

                var track = new VisualElement();
                track.AddToClassList("skill__track");

                var fill = new VisualElement();
                fill.AddToClassList("skill__fill");
                track.Add(fill);
                row.Add(track);

                _skillList.Add(row);
                _skillRows[skill] = new SkillRow { Level = level, Fill = fill };
            }
        }

        private void RefreshSkills()
        {
            var skills = _farmer != null ? _farmer.Skills : null;

            if (skills == null)
            {
                if (_skillNext != null) _skillNext.text = "навыки не подключены";
                return;
            }

            // Заодно ищем, какой навык ближе всего к следующему уровню — о нём и подсказка.
            FarmerSkill closest = FarmerSkill.Harvesting;
            float best = -1f;

            foreach (var pair in _skillRows)
            {
                int level = skills.LevelOf(pair.Key);
                float progress = skills.Progress01(pair.Key);
                bool maxed = level >= skills.MaxLevel;

                pair.Value.Level.text = level.ToString();
                pair.Value.Fill.style.width = new StyleLength(Length.Percent(progress * 100f));
                pair.Value.Fill.EnableInClassList("skill__fill--max", maxed);

                if (!maxed && progress > best) { best = progress; closest = pair.Key; }
            }

            if (_skillNext != null)
                _skillNext.text = best < 0f
                    ? "всё изучено"
                    : "скоро: " + FarmerSkills.NameOf(closest) + " — " + skills.NextRewardOf(closest);
        }

        private void RefreshStorageSummary()
        {
            if (_storageSummary == null) return;

            if (_storage == null) { _storageSummary.text = "Склад: —"; return; }

            bool slots = _storage.CapacityMode == InventoryCapacity.Slots;

            // Ячейки, а не виды ресурсов: строка обязана считать ровно то же, чем склад
            // меряет свою полноту, иначе «12 / 48» перестанет объяснять отказ.
            _storageSummary.text = slots
                ? "Склад: " + _storage.UsedSlots + " / " + _storage.Capacity + " ячеек, " + _storage.TotalUnits + " ед."
                : "Склад: " + _storage.TotalUnits + " ед.";

            // Полный склад останавливает доставки фермера. Он должен быть виден до того,
            // как игрок заметит, что счётчик перестал расти, — и при ЛЮБОМ режиме ёмкости:
            // привязка тревоги к Slots делала отказ в режиме Units невидимым.
            bool full = _storage.IsFull;
            _storageSummary.EnableInClassList("row--alert", full);
            if (full) _storageSummary.text += " — полон!";
        }

        // ---- меню продажи ----

        /// <summary>
        /// Показать меню продажи ресурса, пришвартованное к элементу, по которому кликнули.
        /// <para>
        /// Собрано вручную, а не через <see cref="GenericDropdownMenu"/>: тот подгоняет ширину под
        /// элемент-якорь, и от маленькой ячейки меню становилось нечитаемо узким. Публичное, потому
        /// что окно склада тоже его поднимает, а дубль меню означал бы два комплекта цен.
        /// </para>
        /// </summary>
        public void ShowSellMenu(ResourceDefinition resource, VisualElement anchor)
        {
            var shop = Shop.Instance;
            if (resource == null || shop == null || _storage == null || _contextMenu == null || anchor == null) return;

            int have = _storage.GetAmount(resource);
            if (have <= 0) { CloseSellMenu(); return; }

            _contextTitle.text = resource.DisplayName + " — " + have + " шт.";
            _contextItems.Clear();

            if (resource.SellPrice <= 0)
            {
                var none = new Label("не продаётся");
                none.AddToClassList("ctx__item");
                _contextItems.Add(none);
            }
            else
            {
                AddSellOption(shop, resource, 1, have);
                AddSellOption(shop, resource, 10, have);
                AddSellOption(shop, resource, have, have, "Продать всё");
            }

            _contextMenu.style.display = DisplayStyle.Flex;
            _contextMenu.style.left = anchor.worldBound.xMax + 8f;
            _contextMenu.style.top = anchor.worldBound.yMin;
        }

        private void AddSellOption(Shop shop, ResourceDefinition resource, int amount, int have,
                                   string caption = null)
        {
            if (amount <= 0) return;

            int actual = Mathf.Min(amount, have);
            string label = (caption ?? ("Продать " + amount)) + "   +" + shop.SellValue(resource, actual) + " зол.";

            var button = new Button(() =>
            {
                shop.TrySell(resource, actual);
                CloseSellMenu();
            }) { text = label };

            button.AddToClassList("ctx__item");
            button.SetEnabled(actual > 0 && actual <= have);
            _contextItems.Add(button);
        }

        public void CloseSellMenu()
        {
            if (_contextMenu != null) _contextMenu.style.display = DisplayStyle.None;
        }

        // ---- HUD не ловит клики ----

        /// <summary>
        /// Сделать HUD табло: клик перехватывают только кнопки, всё остальное уходит в мир.
        /// <para>
        /// UI Toolkit съедает клик под любым нарисованным элементом — а «нарисован» тут не только
        /// тёмный прямоугольник панели. Контейнер <c>hud</c> тянется по ширине панели инструментов
        /// и по высоте всей колонки, и эта прозрачная четверть экрана глотала клики молча: игрок
        /// видел грядку сквозь пустоту, целился в неё и не понимал, почему она не берётся. Сами
        /// панели ничем не лучше — сквозь них нарочно видно ферму, а нажать в них нечего.
        /// </para>
        /// <para>
        /// Правило простое: отвечает на клик — ловит клик. В HUD отвечают только кнопки.
        /// </para>
        /// <para>
        /// Разом по поддереву, а не атрибутом <c>picking-mode</c> у каждой строки в UXML: первая же
        /// добавленная потом строка тихо вернула бы мёртвую зону, и связать её с этим местом было
        /// бы нечем.
        /// </para>
        /// </summary>
        private static void MakeReadout(VisualElement hud)
        {
            if (hud == null) return;

            hud.pickingMode = PickingMode.Ignore;
            hud.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            hud.Query<Button>().ForEach(b => b.pickingMode = PickingMode.Position);
        }

        private void OnCarryChanged(Transform carried) => ApplyBusy();

        private void OnGatherChanged(Gatherable node) => ApplyBusy();

        /// <summary>
        /// Пока игрок ведёт ношу или собирает узел, колонка статуса отступает.
        /// <para>
        /// Клики она больше не перехватывает, но остаётся тёмной доской над углом поля, и грядка
        /// в руке уезжала бы под неё вслепую. Заодно это и объяснение: панель, которая при первом
        /// же переносе отходит в сторону, читается как табло, а не как преграда.
        /// </para>
        /// </summary>
        private void ApplyBusy()
        {
            if (_panels == null) return;

            _panels.EnableInClassList(BusyClass, DragFocus.ByPlayer || GatherFocus.Current != null);
        }

        // ---- сворачивание панелей ----

        /// <summary>
        /// Спрятать колонку с панелями. Клики она не ест (см. <see cref="MakeReadout"/>), но
        /// закрывает собой угол поля — на маленьком экране игрок должен уметь убрать её с глаз,
        /// не теряя панель инструментов.
        /// </summary>
        public void TogglePanels()
        {
            _collapsed = !_collapsed;
            PlayerPrefs.SetInt(PrefCollapsed, _collapsed ? 1 : 0);
            ApplyCollapsed();

            CloseSellMenu();
            Farm.Juice.Sfx.Play(b => _collapsed ? b.UiClose : b.UiOpen);
        }

        /// <summary>
        /// Показать или спрятать плашки уровней над грядками.
        /// <para>
        /// Плашка висит над каждой грядкой, и на разросшейся ферме их десятки — поле начинает
        /// читаться как таблица. Уровень нужен, когда прикидываешь, что с чем слить; всё
        /// остальное время на ферму хочется просто смотреть.
        /// </para>
        /// </summary>
        public void ToggleBadges()
        {
            if (_badges != null) _badges.Toggle();

            PlayerPrefs.SetInt(PrefBadges, _badges != null && _badges.Visible ? 1 : 0);
            ApplyBadges();

            Farm.Juice.Sfx.Play(b => b.UiClick);
        }

        private void ApplyBadges()
        {
            if (_badgesButton == null) return;

            bool on = _badges != null && _badges.Visible;
            // × (U+00D7), а не ✕ (U+2715): второго в Inter нет, и динамический атлас рисует
            // на его месте пустой квадрат-«тофу». Правило: символ в тексте интерфейса обязан
            // быть в шрифте — проверять надо весь текст, а не только новый.
            _badgesButton.text = on ? "Уровни" : "Уровни ×";
            _badgesButton.EnableInClassList("btn--muted", !on);
        }

        private void ApplyCollapsed()
        {
            if (_panels != null) _panels.style.display = _collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (_collapseButton != null) _collapseButton.text = _collapsed ? "Показать" : "Скрыть";
        }

        /// <summary>Любой клик вне меню закрывает его.</summary>
        private void OnRootPointerDown(PointerDownEvent evt)
        {
            if (_contextMenu == null || _contextMenu.style.display == DisplayStyle.None) return;

            var target = evt.target as VisualElement;
            if (target != null && (target == _contextMenu || _contextMenu.Contains(target))) return;

            CloseSellMenu();
        }

        // ---- фермер и ферма ----

        private void RefreshFarmer()
        {
            if (_farmer == null)
            {
                if (_farmerState != null) _farmerState.text = "нет фермера";
                return;
            }

            if (_farmerState != null) _farmerState.text = Describe(_farmer.State);

            // Мысль живёт здесь, а не над головой: в мире висит только редкий символ,
            // а текст, постоянно висящий над персонажем, перестают замечать.
            if (_farmerThought != null) _farmerThought.text = _farmer.Thought;

            if (_needs != null)
            {
                SetMeter(_satietyFill, _satietyLabel, "Сытость", _needs.Satiety01, _needs.IsHungry);
                SetMeter(_hydrationFill, _hydrationLabel, "Вода", _needs.Hydration01, _needs.IsThirsty);
                SetMeter(_energyFill, _energyLabel, "Бодрость", _needs.Energy01, _needs.IsTired);
            }

            var inv = _farmer.Inventory;
            if (inv == null) return;

            int free = inv.FreeUnits;
            int capacity = free == int.MaxValue ? 0 : inv.TotalUnits + free;
            float fill = capacity > 0 ? Mathf.Clamp01(inv.TotalUnits / (float)capacity) : 0f;

            string text = capacity > 0
                ? "Рюкзак " + inv.TotalUnits + " / " + capacity
                : "Рюкзак " + inv.TotalUnits;

            SetMeter(_carryFill, _carryLabel, text, fill, false, appendPercent: false);

            RefreshHire();
        }

        /// <summary>
        /// Строка и кнопка найма (Ф3). Нанятый фермер собирает только рутину — ступени
        /// до <see cref="FarmerAgent.HelperMaxTier"/>; дорогое всегда остаётся рукам игрока.
        /// </summary>
        private void RefreshHire()
        {
            if (_farmerHire == null && _hireButton == null) return;

            if (_hireMessageTimer > 0f)
            {
                _hireMessageTimer -= _refreshInterval;
                if (_farmerHire != null) _farmerHire.text = _hireMessage;
            }
            else if (_farmerHire != null)
            {
                // Строка говорит на языке роли: подсобник собирает, мастеровой ведёт
                // станки, возчик расширяет доску заказов, сторож наёмным не бывает.
                string hiredDoes, idleMeans;
                switch (_farmer.Role)
                {
                    case ResidentRole.Craftsman:
                        hiredDoes = " · ведёт станки";
                        idleMeans = "не нанят — станки без мастера";
                        break;
                    case ResidentRole.Carter:
                        hiredDoes = " · доска заказов шире на один";
                        idleMeans = "не нанят — доска заказов обычная";
                        break;
                    case ResidentRole.Watchman:
                        hiredDoes = "";
                        idleMeans = "жалования не берёт — ночь его смена";
                        break;
                    case ResidentRole.Builder:
                        hiredDoes = " · достраивает покупки";
                        idleMeans = "не нанят — стройки идут сами, медленно";
                        break;
                    default:
                        hiredDoes = " · собирает ступени 1–" + FarmerAgent.HelperMaxTier;
                        idleMeans = "не нанят — урожай собирает хозяин";
                        break;
                }

                _farmerHire.text = _farmer.IsHired
                    ? "нанят ещё на " + FormatDuration(_farmer.HiredSecondsLeft) + hiredDoes
                    : idleMeans;
            }

            if (_hireButton != null)
            {
                // Сторожа не нанимают вовсе — кнопка врала бы самим существованием.
                bool hireable = _farmer.Role != ResidentRole.Watchman;
                _hireButton.style.display = hireable ? DisplayStyle.Flex : DisplayStyle.None;
                if (hireable)
                    _hireButton.text = (_farmer.IsHired ? "Продлить на сутки — " : "Нанять на сутки — ")
                                       + _farmer.RoleWagePerDay + " зол.";
            }

            RefreshWageSummary();
        }

        private void OnHireClicked()
        {
            if (_farmer == null) return;

            if (_farmer.TryHire(86400.0, _farmer.RoleWagePerDay))
            {
                Punch(_farmerHire, 0.25f);
                Farm.Juice.Sfx.Play(b => b.UiOpen);
            }
            else
            {
                // Отказ обязан быть заметным: строка называет и причину, и цену.
                _hireMessage = "не хватает золота — жалование " + _farmer.RoleWagePerDay + " зол.";
                _hireMessageTimer = 2.5f;
                Farm.Juice.Sfx.Play(b => b.UiClose);
            }
        }

        private void RefreshFarm()
        {
            RefreshFarmLevel();

            if (_plotsReady != null)
                _plotsReady.text = "Готово к сбору: " + GrowableRegistry.ReadyCount + " из " + GrowableRegistry.Count;

            if (_plotsNext == null) return;

            // Только для маленьких ферм — просмотреть пару десятков грядок десять раз в секунду бесплатно.
            double soonest = double.MaxValue;
            var all = GrowableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                double t = all[i].TimeUntilReady;
                if (t > 0.0 && t < soonest) soonest = t;
            }

            _plotsNext.text = soonest < double.MaxValue
                ? "Следующая через " + FormatDuration(soonest)
                : "Всё поспело";
        }

        // ---- уровень фермы ----

        private void OnFarmLevelChanged(int level) => RefreshFarmLevel();

        /// <summary>
        /// Строка уровня, обещание следующей ступени и кнопка расширения.
        /// <para>
        /// Цена стоит в строке, а не в кнопке: она из трёх частей, и кнопка с такой подписью
        /// перестала бы быть кнопкой. Кнопка называет то, что игрок получит, — новый край.
        /// </para>
        /// </summary>
        private void RefreshFarmLevel()
        {
            if (_farmLevel != null)
                _farmLevel.text = "Ферма: уровень " + FarmLevels.Current + " из " + FarmLevels.MaxLevel;

            // В гостях кнопки нет вовсе: чужую ферму не расширяют, и предлагать это — врать.
            bool canBuy = !GuestMode.IsGuest && !FarmLevels.IsMax;
            if (_farmExpand != null)
            {
                _farmExpand.style.display = canBuy ? DisplayStyle.Flex : DisplayStyle.None;
                if (canBuy) _farmExpand.text = "Расширить до " + Meters(Mathf.RoundToInt(FarmLevels.Next.Radius));
            }

            if (_farmLevelNote == null) return;

            // Пока висит ответ на нажатие (успех или отказ) — обещание ждёт: перебивать
            // названную причину обратно ценой значит прятать отказ.
            if (_farmMessageTimer > 0f)
            {
                _farmMessageTimer -= _refreshInterval;
                _farmLevelNote.text = _farmMessage;
                return;
            }

            if (FarmLevels.IsMax) { _farmLevelNote.text = "ферма выросла во всю долину"; return; }

            var next = FarmLevels.Next;
            _farmLevelNote.text = "дальше: " + PriceOf(next) + " — " + next.Promise;
        }

        /// <summary>Цена ступени одной строкой; даровые части (нулевые) молчат.</summary>
        private static string PriceOf(FarmLevels.Step step)
        {
            var text = new System.Text.StringBuilder();

            if (step.Gold > 0) text.Append(step.Gold).Append(" зол.");
            if (step.Boards > 0) Plus(text).Append(step.Boards).Append(" досок");
            if (step.Dew > 0) Plus(text).Append(step.Dew).Append(" росы");

            return text.Length > 0 ? text.ToString() : "даром";

            static System.Text.StringBuilder Plus(System.Text.StringBuilder b) =>
                b.Length > 0 ? b.Append(" + ") : b;
        }

        /// <summary>
        /// Метры по-русски: 21 метр, 22 метра, 25 метров. Число, не согласованное с
        /// существительным, читается как вывод программы, а не как речь про свою ферму.
        /// </summary>
        private static string Meters(int value)
        {
            int hundreds = value % 100;
            if (hundreds >= 11 && hundreds <= 14) return value + " метров";

            int last = value % 10;
            if (last == 1) return value + " метр";
            if (last >= 2 && last <= 4) return value + " метра";
            return value + " метров";
        }

        private void OnExpandClicked()
        {
            // Кнопки в гостях не видно, но событие можно прислать и мимо неё — правило
            // держится здесь, а не одной только видимостью.
            if (GuestMode.IsGuest) return;

            if (FarmLevels.TryBuyNext(out string refusal))
            {
                _farmMessage = "ферма выросла: " + Meters(Mathf.RoundToInt(FarmLevels.Radius));
                _farmMessageTimer = 2f;
                Punch(_farmLevel, 0.28f);
                Farm.Juice.Sfx.Play(b => b.UiOpen);
            }
            else
            {
                // Отказ обязан быть заметным: показываем ровно ту причину, которую назвала
                // лестница, — «не хватает 20 — доска» точнее любого общего «нельзя».
                _farmMessage = refusal;
                _farmMessageTimer = 2.5f;
                Farm.Juice.Sfx.Play(b => b.UiClose);
            }

            // Не ждать очередного тика: отклик на своё же нажатие обязан быть мгновенным.
            RefreshFarmLevel();
        }

        /// <summary>
        /// Срок по-людски: рост теперь меряется реальными часами, и «10800.0 c» на плашке
        /// читалось бы как ошибка, а не как обещание. Секунды показываем только под минутой —
        /// там счёт уже идёт на глазах.
        /// <para>
        /// Публичный, потому что срок в игре не один: тем же голосом говорят таймер грядки,
        /// остаток найма и смена доски заказов (<see cref="TasksWindow"/>). Второй такой же
        /// метод рядом — это две разных «2 ч 40 мин» через месяц.
        /// </para>
        /// </summary>
        public static string FormatDuration(double seconds)
        {
            if (seconds >= 3600.0)
            {
                int hours = (int)(seconds / 3600.0);
                int minutes = (int)(seconds % 3600.0 / 60.0);
                return minutes > 0 ? hours + " ч " + minutes + " мин" : hours + " ч";
            }

            if (seconds >= 60.0) return (int)(seconds / 60.0) + " мин";
            return Mathf.CeilToInt((float)seconds) + " с";
        }

        private void OnNetStatusChanged(string line) => RefreshNetStatus(line);

        /// <summary>
        /// Сетевая строка в панели фермы. Пустая строка состояния — тоже состояние:
        /// до первого события показываем, за кого мы на сервере (или что играем без сети).
        /// <para>
        /// Отказ красится <c>row--alert</c>, а не тихим <c>row--muted</c>: правило проекта —
        /// отказ системы обязан быть заметным, а «без сети», набранное самой блёклой строкой
        /// экрана, — то же молчание, только буквами.
        /// </para>
        /// </summary>
        private void RefreshNetStatus(string line)
        {
            if (_netStatus == null) return;

            bool bad = NetStatus.Bad || !NetSession.LoggedIn;

            if (string.IsNullOrEmpty(line))
                line = NetSession.LoggedIn ? "онлайн: " + NetSession.PlayerName : "без сети";

            _netStatus.text = line;
            _netStatus.EnableInClassList("row--muted", !bad);
            _netStatus.EnableInClassList("row--alert", bad);
        }

        private static void SetMeter(VisualElement fill, Label label, string caption, float value01,
                                     bool low, bool appendPercent = true)
        {
            if (fill != null)
            {
                fill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(value01) * 100f));
                fill.EnableInClassList("meter__fill--low", low);
            }

            if (label != null)
                label.text = appendPercent
                    ? caption + " " + Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f) + "%"
                    : caption;
        }

        private static string Describe(FarmerState state)
        {
            switch (state)
            {
                case FarmerState.Idle: return "стоит";
                case FarmerState.Wandering: return "бродит";
                case FarmerState.GoingToHarvest: return "идёт за урожаем";
                case FarmerState.Harvesting: return "собирает";
                case FarmerState.ReturningHome: return "несёт домой";
                case FarmerState.Depositing: return "разгружается";
                case FarmerState.GoingToService: return "идёт подкрепиться";
                case FarmerState.UsingService: return "ест и пьёт";
                case FarmerState.GoingToMarket: return "идёт на рынок";
                case FarmerState.AtMarket: return "торгует";
                case FarmerState.GoingToSleep: return "идёт спать";
                case FarmerState.Sleeping: return "спит";
                case FarmerState.GoingToImprove: return "идёт обустраивать";
                case FarmerState.Improving: return "мастерит";
                case FarmerState.Thinking: return "прикидывает";
                case FarmerState.Relaxing: return "отдыхает";
                case FarmerState.Awaiting: return "ждёт урожай";
                case FarmerState.GoingToTidy: return "идёт прибраться";
                case FarmerState.Hauling: return "переставляет";
                case FarmerState.GoingToCraft: return "идёт к станку";
                case FarmerState.Crafting: return "у станка";
                case FarmerState.GoingToLoad: return "идёт за коробом";
                case FarmerState.DeliveringOrder: return "везёт заказ";
                case FarmerState.GoingToBuild: return "идёт на стройку";
                case FarmerState.Constructing: return "строит";
                default: return state.ToString();
            }
        }
    }
}

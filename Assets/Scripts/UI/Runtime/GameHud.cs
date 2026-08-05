using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;
using Farm.Characters;

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

        private VisualElement _root;
        private Label _goldValue;
        private Label _clockValue;
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
        private Label _skillNext;
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
            _storageSummary = root.Q<Label>("storage-summary");
            _farmerState = root.Q<Label>("farmer-state");
            _farmerThought = root.Q<Label>("farmer-thought");
            _satietyLabel = root.Q<Label>("satiety-label");
            _hydrationLabel = root.Q<Label>("hydration-label");
            _energyLabel = root.Q<Label>("energy-label");
            _carryLabel = root.Q<Label>("carry-label");
            _plotsReady = root.Q<Label>("plots-ready");
            _plotsNext = root.Q<Label>("plots-next");
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

            BuildSkillRows();

            // После сборки строк навыков: они создаются кодом и приходят с обычным picking.
            MakeReadout(_hud);

            DragFocus.Changed += OnCarryChanged;
            GatherFocus.Changed += OnGatherChanged;
            ApplyBusy();

            Bind();
            Refresh();
        }

        private void OnDisable()
        {
            if (_storage != null) _storage.Changed -= OnStorageChanged;
            if (_wallet != null) _wallet.Changed -= OnWalletChanged;
            if (_clock != null) _clock.DayStarted -= OnDayStarted;
            if (_root != null) _root.UnregisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);

            DragFocus.Changed -= OnCarryChanged;
            GatherFocus.Changed -= OnGatherChanged;

            _storage = null;
            _wallet = null;
            _clock = null;
            _root = null;
        }

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

        // ---- навыки ----

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
            _badgesButton.text = on ? "Уровни" : "Уровни ✕";
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
        }

        private void RefreshFarm()
        {
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
                ? "Следующая через " + soonest.ToString("F1") + " c"
                : "Всё поспело";
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
                default: return state.ToString();
            }
        }
    }
}

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

        private UIDocument _document;
        private ShopWindow _shop;
        private InventoryWindow _inventory;
        private SettingsWindow _settings;
        private VisualElement _panels;
        private Button _collapseButton;
        private bool _collapsed;

        private VisualElement _root;
        private Label _goldValue;
        private Label _clockValue;
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

            _panels = root.Q<VisualElement>("hud-panels");
            _collapseButton = root.Q<Button>("collapse-button");
            if (_collapseButton != null) _collapseButton.clicked += TogglePanels;
            ApplyCollapsed();

            if (_farmer == null) _farmer = FindFirstObjectByType<FarmerAgent>();
            if (_farmer != null) _needs = _farmer.GetComponent<CharacterNeeds>();

            BuildSkillRows();
            Bind();
            Refresh();
        }

        private void OnDisable()
        {
            if (_storage != null) _storage.Changed -= OnStorageChanged;
            if (_wallet != null) _wallet.Changed -= OnWalletChanged;
            if (_root != null) _root.UnregisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);

            _storage = null;
            _wallet = null;
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
        }

        private void OnStorageChanged(IInventory inventory) => RefreshStorageSummary();

        private void OnWalletChanged(Wallet wallet, int delta)
        {
            RefreshGold();

            // Счётчик обязан отреагировать физически: цифра, которая просто меняется,
            // не читается как награда.
            if (_goldValue != null) StartCoroutine(PunchGold(delta > 0 ? 0.28f : 0.14f));
        }

        private System.Collections.IEnumerator PunchGold(float strength)
        {
            const float duration = 0.26f;
            float t = 0f;

            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / duration;
                float s = 1f + Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * strength;
                _goldValue.style.scale = new StyleScale(new Scale(new Vector2(s, s)));
                yield return null;
            }

            _goldValue.style.scale = new StyleScale(new Scale(Vector2.one));
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

            _storageSummary.text = _storage.CapacityMode == InventoryCapacity.Slots
                ? "Склад: " + _storage.DistinctCount + " / " + _storage.Capacity + " ячеек, " + _storage.TotalUnits + " ед."
                : "Склад: " + _storage.TotalUnits + " ед.";
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

        // ---- сворачивание панелей ----

        /// <summary>
        /// Спрятать колонку с панелями. UI Toolkit съедает клики под любым нарисованным элементом,
        /// и панели были мёртвой зоной над третью поля — игрок должен уметь убрать их с дороги,
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
                case FarmerState.Sleeping: return "спит";
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

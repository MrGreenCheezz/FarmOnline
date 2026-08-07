using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Окно магазина: вкладки по категориям, строка на товар, кнопки покупки, гаснущие,
    /// когда цену не оплатить.
    /// <para>
    /// Всё читает из <see cref="Shop"/> и сам никогда не трогает ни кошелёк, ни склад — цены,
    /// лимиты и доставка есть правила магазина, и их дубль здесь — ровно тот путь, которым
    /// двое расходятся.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Shop Window")]
    public sealed class ShopWindow : MonoBehaviour
    {
        private static readonly ShopCategory[] Categories =
        {
            ShopCategory.Plants,
            ShopCategory.Animals,
            ShopCategory.Ore,
            ShopCategory.Buildings,
            ShopCategory.Decor
        };

        [Tooltip("Сколько секунд держать сообщение об отказе.")]
        [SerializeField, Min(0.5f)] private float _messageDuration = 2.5f;

        private VisualElement _overlay;
        private VisualElement _tabs;
        private VisualElement _list;
        private Label _gold;
        private Label _message;

        private Shop _shop;
        private Wallet _wallet;
        private IInventory _storage;

        private readonly List<ShopItemDefinition> _buffer = new List<ShopItemDefinition>();
        private readonly List<Button> _tabButtons = new List<Button>();
        private ShopCategory _active = ShopCategory.Plants;
        private float _messageTimer;

        public bool IsOpen => _overlay != null && _overlay.style.display != DisplayStyle.None;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) { enabled = false; return; }

            _overlay = root.Q<VisualElement>("shop-overlay");
            _tabs = root.Q<VisualElement>("shop-tabs");
            _list = root.Q<VisualElement>("shop-list");
            _gold = root.Q<Label>("shop-gold");
            _message = root.Q<Label>("shop-message");

            var close = root.Q<Button>("shop-close");
            if (close != null) close.clicked += Close;

            // Клик по затемнению закрывает, клик по самому окну — не должен.
            if (_overlay != null) _overlay.RegisterCallback<ClickEvent>(OnOverlayClick);

            BindShop();
            BuildTabs();
            Close();
        }

        private void OnDisable()
        {
            if (_wallet != null) _wallet.Changed -= OnWalletChanged;
            if (_storage != null) _storage.Changed -= OnStorageChanged;
            if (_shop != null)
            {
                _shop.Refused -= OnRefused;
                _shop.Bought -= OnBought;
            }

            _wallet = null;
            _storage = null;
            _shop = null;
        }

        private void Update()
        {
            if (_messageTimer <= 0f) return;

            _messageTimer -= Time.unscaledDeltaTime;
            if (_messageTimer <= 0f && _message != null) _message.text = "";
        }

        public void Open()
        {
            BindShop();
            if (_overlay != null) _overlay.style.display = DisplayStyle.Flex;
            RebuildList();
        }

        public void Close()
        {
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        private void OnOverlayClick(ClickEvent evt)
        {
            if (evt.target == _overlay) Close();
        }

        private void BindShop()
        {
            if (_shop == null)
            {
                _shop = Shop.Instance != null ? Shop.Instance : FindFirstObjectByType<Shop>();
                if (_shop != null)
                {
                    _shop.Refused += OnRefused;
                    _shop.Bought += OnBought;
                }
            }

            if (_wallet == null)
            {
                _wallet = Wallet.Instance != null ? Wallet.Instance : FindFirstObjectByType<Wallet>();
                if (_wallet != null) _wallet.Changed += OnWalletChanged;
            }

            if (_storage == null)
            {
                _storage = FarmingRuntime.Sink as IInventory;
                if (_storage != null) _storage.Changed += OnStorageChanged;
            }

            RefreshGold();
        }

        private void OnWalletChanged(Wallet wallet, int delta)
        {
            RefreshGold();
            if (IsOpen) RebuildList();
        }

        private void OnStorageChanged(IInventory inventory)
        {
            if (IsOpen) RebuildList();
        }

        private void OnBought(Shop shop, ShopItemDefinition item) => ShowMessage("Куплено: " + item.DisplayName, false);
        private void OnRefused(Shop shop, string reason) => ShowMessage(reason, true);

        private void ShowMessage(string text, bool isError)
        {
            if (_message == null) return;
            _message.text = text;
            _message.style.color = isError ? new Color(0.9f, 0.55f, 0.51f) : new Color(0.59f, 0.82f, 0.65f);
            _messageTimer = _messageDuration;
        }

        private void RefreshGold()
        {
            if (_gold != null) _gold.text = (_wallet != null ? _wallet.Gold : 0) + " зол.";
        }

        private void BuildTabs()
        {
            if (_tabs == null) return;

            _tabs.Clear();
            _tabButtons.Clear();

            foreach (var category in Categories)
            {
                var captured = category;
                var button = new Button(() => SelectTab(captured)) { text = Caption(category) };
                button.AddToClassList("tab");
                _tabs.Add(button);
                _tabButtons.Add(button);
            }

            SelectTab(_active);
        }

        /// <summary>Переключить вкладку. Публичный — другие системы могут открыть магазин сразу на категории.</summary>
        public void SelectTab(ShopCategory category)
        {
            _active = category;

            for (int i = 0; i < _tabButtons.Count; i++)
                _tabButtons[i].EnableInClassList("tab--active", Categories[i] == category);

            RebuildList();
        }

        private void RebuildList()
        {
            if (_list == null) return;

            _list.Clear();

            var catalog = _shop != null ? _shop.Catalog : null;
            if (catalog == null)
            {
                var warning = new Label("каталог не назначен");
                warning.AddToClassList("row");
                warning.AddToClassList("row--muted");
                _list.Add(warning);
                return;
            }

            catalog.GetByCategory(_active, _buffer);
            if (_buffer.Count == 0)
            {
                var empty = new Label("в этой вкладке пока пусто");
                empty.AddToClassList("row");
                empty.AddToClassList("row--muted");
                _list.Add(empty);
                return;
            }

            foreach (var item in _buffer) _list.Add(BuildRow(item));
        }

        private VisualElement BuildRow(ShopItemDefinition item)
        {
            var row = new VisualElement();
            row.AddToClassList("shop-item");

            var icon = new VisualElement();
            icon.AddToClassList("shop-item__icon");
            if (item.Icon != null) icon.style.backgroundImage = new StyleBackground(item.Icon);
            row.Add(icon);

            var text = new VisualElement();
            text.AddToClassList("shop-item__text");

            var name = new Label(item.DisplayName);
            name.AddToClassList("shop-item__name");
            text.Add(name);

            if (!string.IsNullOrEmpty(item.Description))
            {
                var desc = new Label(item.Description);
                desc.AddToClassList("shop-item__desc");
                text.Add(desc);
            }

            string reason = null;
            bool affordable = _shop != null && _shop.CanBuy(item, out reason);
            bool locked = _shop != null && _shop.ProgressLock(item, out _); // причина уже в reason: CanBuy зовёт ProgressLock первым

            string priceText = item.Price.ToString();
            if (item.MaxOwned > 0 && _shop != null)
                priceText += "   [" + _shop.OwnedCount(item) + " / " + item.MaxOwned + "]";

            var price = new Label(priceText);
            price.AddToClassList("shop-item__price");
            // Дорого и заперто — разные вещи: дорогое копится, запертое требует уровня.
            price.EnableInClassList("shop-item__price--unaffordable", !affordable && !locked);
            text.Add(price);

            // Замок — видимой строкой, не тултипом: рантайм UI Toolkit тултипы не рисует,
            // а выключенная кнопка не примет клик, чтобы сказать причину голосом Refused.
            if (locked && !string.IsNullOrEmpty(reason))
            {
                var lockLine = new Label(reason);
                lockLine.AddToClassList("shop-item__lock");
                text.Add(lockLine);
            }

            row.Add(text);

            var buy = new Button(() => { if (_shop != null) _shop.TryBuy(item); }) { text = "Купить" };
            buy.AddToClassList("btn");
            buy.AddToClassList("btn--accent");
            buy.SetEnabled(affordable);
            row.Add(buy);

            return row;
        }

        private static string Caption(ShopCategory category)
        {
            switch (category)
            {
                case ShopCategory.Plants: return "Растения";
                case ShopCategory.Animals: return "Животные";
                case ShopCategory.Ore: return "Руда";
                case ShopCategory.Buildings: return "Постройки";
                case ShopCategory.Decor: return "Декор";
                default: return category.ToString();
            }
        }
    }
}

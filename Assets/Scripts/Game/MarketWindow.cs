using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;
using Farm.Net;

namespace Farm.Game
{
    /// <summary>
    /// Окно рынка: лоты игроков и продажа со склада. Вся честность сделок живёт
    /// в <see cref="NetMarket"/> — окно только показывает и зовёт.
    /// <para>
    /// Одно окно двумя секциями, а не двумя вкладками: продать и купить игрок решает,
    /// глядя на одни и те же цены, и прятать половину картины за переключателем незачем.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Market Window")]
    public sealed class MarketWindow : MonoBehaviour
    {
        /// <summary>Сколько штук уходит в лот одной кнопкой. Второй кнопкой уходит весь запас.</summary>
        private const int LotAmount = 10;

        private VisualElement _root;
        private VisualElement _overlay;
        private VisualElement _list;
        private Label _message;
        private Button _openButton;

        private bool _refreshing;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>()?.rootVisualElement;
            if (_root == null) { enabled = false; return; }

            _overlay = _root.Q<VisualElement>("market-overlay");
            _list = _root.Q<VisualElement>("market-list");
            _message = _root.Q<Label>("market-message");
            _openButton = _root.Q<Button>("market-button");

            if (_openButton != null) _openButton.clicked += Open;

            var close = _root.Q<Button>("market-close");
            if (close != null) close.clicked += Hide;

            NetMarket.Changed += Refresh;
            NetMarket.Refused += OnRefused;
            GuestMode.Changed += OnGuestChanged;
            OnGuestChanged();
        }

        private void OnDisable()
        {
            if (_openButton != null) _openButton.clicked -= Open;
            NetMarket.Changed -= Refresh;
            NetMarket.Refused -= OnRefused;
            GuestMode.Changed -= OnGuestChanged;
        }

        /// <summary>Гостю рынок не показывается вовсе: торговать чужим складом нельзя.</summary>
        private void OnGuestChanged()
        {
            if (_openButton != null)
                _openButton.style.display = GuestMode.IsGuest ? DisplayStyle.None : DisplayStyle.Flex;
            if (GuestMode.IsGuest) Hide();
        }

        private void Open()
        {
            if (_overlay == null) return;

            if (!NetMarket.Available(out string reason))
            {
                NetStatus.Set(reason);
                return;
            }

            _overlay.style.display = DisplayStyle.Flex;
            Refresh();
        }

        private void Hide()
        {
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
        }

        private bool IsOpen => _overlay != null && _overlay.resolvedStyle.display != DisplayStyle.None;

        private async void Refresh()
        {
            if (!IsOpen || _list == null || _refreshing) return;
            _refreshing = true;

            try
            {
                Message("рынок загружается…");

                var res = await ApiClient.GetMarketAsync();
                if (!IsOpen) return;   // окно закрыли, пока ехал ответ

                if (!res.Transport || res.Value == null || !res.Value.ok)
                {
                    Message("рынок не отвечает" +
                            (res.Value != null && res.Value.error != null ? ": " + res.Value.error : ""), bad: true);
                    return;
                }

                Render(res.Value.lots);
                Message("продавец получает 1.25 цены, покупатель платит 1.5 — разница сгорает");
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void Render(MarketLot[] lots)
        {
            var registry = ContentRegistry.Instance;
            _list.Clear();

            // --- продать своё ---
            Section("ВЫСТАВИТЬ СО СКЛАДА");

            var storage = FarmingRuntime.Sink as Inventory;
            var entries = new List<InventoryEntry>();
            if (storage != null)
                foreach (var entry in storage.Entries)
                    if (entry.Resource != null && entry.Amount > 0) entries.Add(entry);

            if (entries.Count == 0)
            {
                Note("на складе пусто");
            }
            else
            {
                entries.Sort((a, b) => b.Amount.CompareTo(a.Amount));
                foreach (var entry in entries)
                {
                    var resource = entry.Resource;
                    var row = Row(resource.DisplayName, "на складе " + entry.Amount);

                    int some = Mathf.Min(LotAmount, entry.Amount);
                    row.Add(ActionButton("Лот ×" + some, "btn", () => NetMarket.Sell(resource, some)));

                    if (entry.Amount > some)
                    {
                        // Кламп потолком сервера: «Всё ×4752» стабильно получало бы
                        // машинное bad_amount — кнопка не должна предлагать невозможное.
                        // И называться «Всё» она вправе, только когда продаёт всё.
                        int all = Mathf.Min(entry.Amount, NetMarket.MaxLotAmount);
                        if (all > some)
                        {
                            string caption = all < entry.Amount ? "×" + all + " (потолок лота)" : "Всё ×" + all;
                            row.Add(ActionButton(caption, "btn btn--ghost", () => NetMarket.Sell(resource, all)));
                        }
                    }

                    _list.Add(row);
                }
            }

            // --- купить чужое ---
            Section("ЛОТЫ ИГРОКОВ");

            if (lots == null || lots.Length == 0)
            {
                Note("пока никто ничего не выставил");
                return;
            }

            foreach (var lot in lots)
            {
                var resource = registry != null ? registry.Resource(lot.resourceId) : null;
                string name = resource != null ? resource.DisplayName : lot.resourceId;
                bool mine = lot.sellerId == NetSession.PlayerId;

                var row = Row(lot.amount + " × " + name,
                              mine ? "твой лот — ждёт покупателя" : "продаёт " + lot.sellerName);

                var captured = lot;
                if (!mine)
                {
                    row.Add(ActionButton(lot.gold + " зол.", "btn btn--accent", () => NetMarket.Buy(captured)));
                }
                else
                {
                    // Документированная отмена (ONLINE.md): выкупить свой лот, ценой спреда.
                    // Раньше выход был описан в доке и не нажимался нигде.
                    row.Add(ActionButton("Выкупить — " + lot.gold + " зол.", "btn btn--ghost",
                                         () => NetMarket.Buy(captured)));
                }

                _list.Add(row);
            }
        }

        private void OnRefused(string reason) => Message(reason, bad: true);

        private void Message(string text, bool bad = false)
        {
            if (_message == null) return;
            _message.text = text;
            _message.EnableInClassList("window__message--bad", bad);
        }

        // ---- столярка, та же что в SocialWindows ----

        private void Section(string title)
        {
            var label = new Label(title);
            label.AddToClassList("panel__title");
            label.AddToClassList("friends-section");
            _list.Add(label);
        }

        private void Note(string text)
        {
            var label = new Label(text);
            label.AddToClassList("inbox-line");
            _list.Add(label);
        }

        private static VisualElement Row(string name, string note)
        {
            var row = new VisualElement();
            row.AddToClassList("friend");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("friend__name");
            row.Add(nameLabel);

            var noteLabel = new Label(note);
            noteLabel.AddToClassList("friend__note");
            row.Add(noteLabel);

            return row;
        }

        private static Button ActionButton(string text, string classes, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            foreach (var cls in classes.Split(' '))
                button.AddToClassList(cls);
            return button;
        }
    }
}

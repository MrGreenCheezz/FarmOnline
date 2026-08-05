using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;
using Farm.Net;

namespace Farm.Game
{
    /// <summary>
    /// Социальный слой HUD: окно друзей (заявки, визиты, подарки), гостевая плашка в
    /// топбаре и окно «пока тебя не было». Живёт на том же объекте HUD, что и GameHud,
    /// но в сборке Farm.Game — ему нужны GameFlow и OnlineFlow, которых Farm.UI не видит.
    /// <para>
    /// Про клики: окна лежат вне поддерева <c>hud</c>, поэтому проход MakeReadout их не
    /// касается и они ловят клики целиком, как любое окно. Гостевые элементы топбара —
    /// внутри hud: кнопка вернёт себе picking от MakeReadout, подписи и не должны ловить.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/Social Windows")]
    public sealed class SocialWindows : MonoBehaviour
    {
        /// <summary>Сколько единиц уходит в один подарок. Жест, а не перекачка склада.</summary>
        private const int GiftAmount = 5;

        private VisualElement _root;
        private VisualElement _friendsOverlay;
        private VisualElement _friendsList;
        private TextField _friendsName;
        private Label _friendsMessage;
        private VisualElement _inboxOverlay;
        private VisualElement _inboxList;

        private Button _friendsButton;
        private Label _guestTitle;
        private Label _guestHelp;
        private Button _guestHome;

        // Хозяйские элементы, которые гостю не положены: чужое золото, чужой склад,
        // чужой магазин, чужой фермер.
        private VisualElement _goldValue;
        private VisualElement _inventoryButton;
        private VisualElement _shopButton;
        private VisualElement _badgesButton;
        private VisualElement _panelFarmer;
        private VisualElement _panelSkills;

        private bool _refreshing;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>()?.rootVisualElement;
            if (_root == null) { enabled = false; return; }

            _friendsOverlay = _root.Q<VisualElement>("friends-overlay");
            _friendsList = _root.Q<VisualElement>("friends-list");
            _friendsName = _root.Q<TextField>("friends-name");
            _friendsMessage = _root.Q<Label>("friends-message");
            _inboxOverlay = _root.Q<VisualElement>("inbox-overlay");
            _inboxList = _root.Q<VisualElement>("inbox-list");

            _friendsButton = _root.Q<Button>("friends-button");
            _guestTitle = _root.Q<Label>("guest-title");
            _guestHelp = _root.Q<Label>("guest-help");
            _guestHome = _root.Q<Button>("guest-home");

            _goldValue = _root.Q<VisualElement>("gold-value");
            _inventoryButton = _root.Q<VisualElement>("inventory-button");
            _shopButton = _root.Q<VisualElement>("shop-button");
            _badgesButton = _root.Q<VisualElement>("badges-button");
            _panelFarmer = _root.Q<VisualElement>("panel-farmer");
            _panelSkills = _root.Q<VisualElement>("panel-skills");

            if (_friendsButton != null) _friendsButton.clicked += ToggleFriends;
            if (_guestHome != null) _guestHome.clicked += OnlineFlow.GoHome;

            var friendsClose = _root.Q<Button>("friends-close");
            if (friendsClose != null) friendsClose.clicked += HideFriends;

            var invite = _root.Q<Button>("friends-invite");
            if (invite != null) invite.clicked += OnInvite;

            var inboxClose = _root.Q<Button>("inbox-close");
            if (inboxClose != null) inboxClose.clicked += HideInbox;

            GuestMode.Changed += OnGuestChanged;
            GuestMode.HelpRefused += OnHelpRefused;
            NetEvents.InboxReady += OnInboxReady;

            OnGuestChanged();
        }

        private void OnDisable()
        {
            GuestMode.Changed -= OnGuestChanged;
            GuestMode.HelpRefused -= OnHelpRefused;
            NetEvents.InboxReady -= OnInboxReady;
        }

        // ---- гостевая плашка ----

        private void OnGuestChanged()
        {
            bool guest = GuestMode.IsGuest;

            if (_guestTitle != null) _guestTitle.text = guest ? "В гостях у " + GuestMode.OwnerName : "";
            if (_guestHelp != null)
                _guestHelp.text = guest ? "помощь " + GuestMode.HelpUsed + " / " + GuestMode.HelpLimit : "";

            Show(_guestTitle, guest);
            Show(_guestHelp, guest);
            Show(_guestHome, guest);

            Show(_goldValue, !guest);
            Show(_inventoryButton, !guest);
            Show(_shopButton, !guest);
            Show(_friendsButton, !guest);
            Show(_badgesButton, !guest);
            Show(_panelFarmer, !guest);
            Show(_panelSkills, !guest);
        }

        private void OnHelpRefused()
        {
            NetStatus.Set("помощь здесь исчерпана — до " + GuestMode.HelpLimit + " грядок за визит");
        }

        // ---- окно друзей ----

        private void ToggleFriends()
        {
            if (_friendsOverlay == null) return;
            if (_friendsOverlay.resolvedStyle.display != DisplayStyle.None) { HideFriends(); return; }

            _friendsOverlay.style.display = DisplayStyle.Flex;
            RefreshFriends();
        }

        private void HideFriends()
        {
            if (_friendsOverlay != null) _friendsOverlay.style.display = DisplayStyle.None;
        }

        private void HideInbox()
        {
            if (_inboxOverlay != null) _inboxOverlay.style.display = DisplayStyle.None;
        }

        private async void RefreshFriends()
        {
            if (_friendsList == null || _refreshing) return;

            if (!NetSession.LoggedIn)
            {
                Message("без сети друзей не видно — войди с главного меню");
                _friendsList.Clear();
                return;
            }

            _refreshing = true;
            Message("загружаю…");

            try
            {
                var res = await ApiClient.GetFriendsAsync();
                if (!res.Transport || res.Value == null || !res.Value.ok)
                {
                    Message("не получилось: " + (res.Transport && res.Value != null ? res.Value.error : "сеть молчит"));
                    return;
                }

                _friendsList.Clear();
                Message("");

                var incoming = res.Value.incoming;
                if (incoming != null && incoming.Length > 0)
                {
                    Section("ПРОСЯТСЯ В ДРУЗЬЯ");
                    foreach (var entry in incoming)
                    {
                        var row = Row(entry.name, "");
                        row.Add(ActionButton("Принять", "btn btn--accent", () => Accept(entry.playerId)));
                        _friendsList.Add(row);
                    }
                }

                var friends = res.Value.friends;
                if (friends != null && friends.Length > 0)
                {
                    Section("ДРУЗЬЯ");
                    foreach (var entry in friends)
                    {
                        int id = entry.playerId;
                        string name = entry.name;

                        var row = Row(name, Seen(entry.lastSeen));
                        row.Add(ActionButton("В гости", "btn btn--accent", () =>
                        {
                            HideFriends();
                            OnlineFlow.Visit(id, name);
                        }));
                        row.Add(ActionButton("Подарить", "btn", () => ShowGiftPicker(id, name)));
                        _friendsList.Add(row);
                    }
                }

                var outgoing = res.Value.outgoing;
                if (outgoing != null && outgoing.Length > 0)
                {
                    Section("ЖДУТ ОТВЕТА");
                    foreach (var entry in outgoing)
                        _friendsList.Add(Row(entry.name, "позван(а)"));
                }

                if ((incoming == null || incoming.Length == 0) &&
                    (friends == null || friends.Length == 0) &&
                    (outgoing == null || outgoing.Length == 0))
                {
                    Message("пока никого — позови друга по имени его фермы");
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        private async void OnInvite()
        {
            string name = _friendsName != null ? _friendsName.value.Trim() : "";
            if (name.Length < 2) { Message("имя — от 2 символов"); return; }

            var res = await ApiClient.RequestFriendAsync(name);
            if (!res.Transport) { Message("сеть молчит — попробуй ещё раз"); return; }

            if (res.Value != null && res.Value.ok)
            {
                if (_friendsName != null) _friendsName.value = "";
                Message("заявка отправлена: " + name);
                RefreshFriends();
            }
            else
            {
                string error = res.Value != null ? res.Value.error : "непонятный ответ";
                Message(error == "no_such_player" ? "нет игрока с именем «" + name + "»" : "не получилось: " + error);
            }
        }

        private async void Accept(int playerId)
        {
            var res = await ApiClient.AcceptFriendAsync(playerId);
            if (res.Transport && res.Value != null && res.Value.ok) RefreshFriends();
            else Message("не получилось принять заявку");
        }

        // ---- подарки ----

        /// <summary>
        /// Список «что подарить» на месте списка друзей: своё окно ради шести строк —
        /// это ещё один оверлей, который кто-то забудет закрыть.
        /// </summary>
        private void ShowGiftPicker(int friendId, string friendName)
        {
            if (_friendsList == null) return;

            var storage = FarmingRuntime.Sink as Inventory;
            if (storage == null || storage.Entries.Count == 0)
            {
                Message("на складе пусто — дарить нечего");
                return;
            }

            _friendsList.Clear();
            Section("ЧТО ПОДАРИТЬ: " + friendName);

            // Самое многочисленное — сверху: дарят обычно излишки, а не последнее.
            var entries = new List<InventoryEntry>();
            foreach (var entry in storage.Entries)
                if (entry.Resource != null && entry.Amount > 0) entries.Add(entry);
            entries.Sort((a, b) => b.Amount.CompareTo(a.Amount));

            foreach (var entry in entries)
            {
                var resource = entry.Resource;
                int give = Mathf.Min(GiftAmount, entry.Amount);

                var row = Row(resource.DisplayName, "на складе " + entry.Amount);
                row.Add(ActionButton("Подарить " + give, "btn btn--accent", () =>
                {
                    NetEvents.SendGift(friendId, friendName, resource, give);
                    Message("подарок для " + friendName + " уехал");
                    RefreshFriends();
                }));
                _friendsList.Add(row);
            }

            var back = ActionButton("← к друзьям", "btn btn--ghost", RefreshFriends);
            back.style.marginTop = 8;
            _friendsList.Add(back);
        }

        // ---- пока тебя не было ----

        private void OnInboxReady(List<string> lines)
        {
            if (_inboxOverlay == null || _inboxList == null || lines == null || lines.Count == 0) return;

            _inboxList.Clear();
            foreach (var line in lines)
            {
                var label = new Label("• " + line);
                label.AddToClassList("inbox-line");
                _inboxList.Add(label);
            }

            _inboxOverlay.style.display = DisplayStyle.Flex;
        }

        // ---- мелкая столярка ----

        private void Section(string title)
        {
            var label = new Label(title);
            label.AddToClassList("panel__title");
            label.AddToClassList("friends-section");
            _friendsList.Add(label);
        }

        private static VisualElement Row(string name, string note)
        {
            var row = new VisualElement();
            row.AddToClassList("friend");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("friend__name");
            row.Add(nameLabel);

            if (!string.IsNullOrEmpty(note))
            {
                var noteLabel = new Label(note);
                noteLabel.AddToClassList("friend__seen");
                row.Add(noteLabel);
            }

            return row;
        }

        private static Button ActionButton(string text, string classes, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            foreach (var cls in classes.Split(' '))
                button.AddToClassList(cls);
            return button;
        }

        /// <summary>Когда друг был в игре, по-людски. Сервер обновляет lastSeen на каждом запросе.</summary>
        private static string Seen(double lastSeenUnix)
        {
            double ago = ServerClock.UtcNowUnix - lastSeenUnix;
            if (ago < 300.0) return "в игре недавно";
            if (ago < 3600.0) return (int)(ago / 60.0) + " мин назад";
            if (ago < 86400.0) return (int)(ago / 3600.0) + " ч назад";
            return (int)(ago / 86400.0) + " дн назад";
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Message(string text)
        {
            if (_friendsMessage != null) _friendsMessage.text = text;
        }
    }
}

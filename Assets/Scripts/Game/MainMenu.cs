using System;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Net;

namespace Farm.Game
{
    /// <summary>
    /// Главное меню — теперь и прихожая онлайна. Как и весь интерфейс проекта: раскладка
    /// живёт в UXML, стили в USS, а C# только заполняет именованные элементы и вешает
    /// обработчики.
    /// <para>
    /// Вся сетевая асинхронщина заканчивается здесь, до смены сцены: вход сначала
    /// договаривается с сервером (имя с паролем или продление сеанса, потом снимок фермы),
    /// упаковывает результат в <see cref="GameFlow.StartFarm"/> — и только потом грузит
    /// ферму, чей порядок старта жёсткий и синхронный. Сервер молчит — играем без сети,
    /// и об этом сказано вслух: молчаливый отказ здесь был бы потерянной фермой.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/Main Menu")]
    public sealed class MainMenu : MonoBehaviour
    {
        /// <summary>
        /// Короче этого пароль не принимаем. Проверять длину может только клиент: серверу
        /// уходит предварительный хеш, и о том, что за ним стояло, он не судит (docs/ONLINE.md).
        /// </summary>
        private const int MinPassword = 6;

        private VisualElement _root;
        private Button _play;
        private Button _newGame;
        private Label _saveInfo;
        private Label _netStatus;
        private VisualElement _confirm;

        // Форма входа: живёт одной коробкой, чтобы показываться и прятаться целиком.
        private VisualElement _auth;
        private TextField _nameField;
        private TextField _passwordField;
        private Button _login;
        private Button _register;

        // Хозяйские кнопки — видны только вошедшему.
        private Button _changePassword;
        private Button _switchPlayer;

        private VisualElement _passOverlay;
        private TextField _passCurrent;
        private TextField _passNext;
        private TextField _passRepeat;
        private Label _passMessage;

        /// <summary>Сетевой разговор уже идёт — второй клик не должен начать новый.</summary>
        private bool _busy;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>()?.rootVisualElement;
            if (_root == null) { enabled = false; return; }

            _play = _root.Q<Button>("menu-continue");
            _newGame = _root.Q<Button>("menu-new");
            _saveInfo = _root.Q<Label>("menu-save-info");
            _netStatus = _root.Q<Label>("menu-net-status");
            _confirm = _root.Q<VisualElement>("menu-confirm");

            _auth = _root.Q<VisualElement>("menu-auth");
            _nameField = _root.Q<TextField>("menu-name");
            _passwordField = _root.Q<TextField>("menu-password");
            _login = _root.Q<Button>("menu-login");
            _register = _root.Q<Button>("menu-register");

            _changePassword = _root.Q<Button>("menu-password-change");
            _switchPlayer = _root.Q<Button>("menu-switch");

            _passOverlay = _root.Q<VisualElement>("menu-password-overlay");
            _passCurrent = _root.Q<TextField>("menu-pass-current");
            _passNext = _root.Q<TextField>("menu-pass-next");
            _passRepeat = _root.Q<TextField>("menu-pass-repeat");
            _passMessage = _root.Q<Label>("menu-pass-message");

            // Маску ставим кодом, а не атрибутом UXML: имя атрибута у TextField меняется
            // от версии к версии, а поле пароля, показанное открытым, — не мелочь.
            MakePassword(_passwordField);
            MakePassword(_passCurrent);
            MakePassword(_passNext);
            MakePassword(_passRepeat);

            var quit = _root.Q<Button>("menu-quit");

            if (_play != null) _play.clicked += OnPlay;
            if (_newGame != null) _newGame.clicked += OnNewGame;
            if (_login != null) _login.clicked += OnLogin;
            if (_register != null) _register.clicked += OnRegister;
            if (_changePassword != null) _changePassword.clicked += ShowPassword;
            if (_switchPlayer != null) _switchPlayer.clicked += OnSwitchPlayer;

            if (quit != null)
            {
                // В браузере выходить некуда — кнопку убираем совсем, как и мёртвые пункты
                // вообще: пустая кнопка обещает то, чего нет.
                if (GameFlow.CanQuit) quit.clicked += GameFlow.Quit;
                else quit.style.display = DisplayStyle.None;
            }

            var confirmYes = _root.Q<Button>("menu-confirm-yes");
            var confirmNo = _root.Q<Button>("menu-confirm-no");
            if (confirmYes != null) confirmYes.clicked += OnConfirmNewGame;
            if (confirmNo != null) confirmNo.clicked += HideConfirm;

            var passApply = _root.Q<Button>("menu-pass-apply");
            var passClose = _root.Q<Button>("menu-pass-close");
            if (passApply != null) passApply.clicked += OnChangePassword;
            if (passClose != null) passClose.clicked += HidePassword;

            NetStatus.Changed += OnNetStatusChanged;

            HideConfirm();
            HidePassword();
            Refresh();
        }

        private void OnDisable()
        {
            NetStatus.Changed -= OnNetStatusChanged;
        }

        private void Refresh()
        {
            var save = FarmSave.Exists ? FarmSave.Read() : null;
            bool known = NetSession.HasToken;

            // Два разных меню в одном: гостю — как войти, хозяину — как играть. Показывать
            // и то и другое разом значит спрашивать имя у того, кто уже назвался.
            Show(_auth, !known);
            Show(_play, known);
            Show(_changePassword, known);
            Show(_switchPlayer, known);

            if (_play != null) _play.text = "Играть — " + NetSession.PlayerName;

            if (_saveInfo == null) return;

            _saveInfo.text = save != null ? save.Describe() : "новая ферма ждёт хозяина";
            _saveInfo.EnableInClassList("menu__info--empty", save == null);
        }

        // ---- вход по имени и паролю ----

        private async void OnLogin() { await AuthenticateAsync(register: false); }

        private async void OnRegister() { await AuthenticateAsync(register: true); }

        /// <summary>
        /// Единственная дверь в аккаунт: «Войти» и «Завести ферму» отличаются одним вызовом
        /// и одним словом в отказе — разводить их в два одинаковых метода незачем.
        /// </summary>
        private async Awaitable AuthenticateAsync(bool register)
        {
            if (_busy) return;

            string name = _nameField != null ? _nameField.value.Trim() : "";
            if (name.Length < 2 || name.Length > 24)
            {
                Status("имя: 2–24 символа", bad: true);
                return;
            }

            string password = _passwordField != null ? _passwordField.value : "";
            if (password.Length < MinPassword)
            {
                // До сервера не идём вовсе: он увидит только хеш и о длине пароля не узнает.
                Status("пароль — от " + MinPassword + " символов", bad: true);
                return;
            }

            _busy = true;
            SetInteractable(false);

            try
            {
                Status(register ? "завожу ферму…" : "вхожу…");

                string hash = ApiClient.HashPassword(name, password);
                var res = register
                    ? await ApiClient.RegisterAsync(name, hash)
                    : await ApiClient.LoginAsync(name, hash);

                // Сервера нет — не запирать же игрока в прихожей: локальная ферма ждёт,
                // и сказано об этом прямым текстом.
                if (!res.Transport) { PlayOffline(); return; }

                if (res.Value == null || !res.Value.ok)
                {
                    Status(AuthError(res.Value != null ? res.Value.error : null), bad: true);
                    return;
                }

                // Пароль в поле не оставляем: меню открыто ровно столько, сколько игрок отошёл.
                if (_passwordField != null) _passwordField.value = "";
                Refresh();
                await LoadFarmAsync();
            }
            finally
            {
                _busy = false;
                SetInteractable(true);
            }
        }

        /// <summary>Отказ сервера — по-русски и по делу: код ошибки игроку ничего не объясняет.</summary>
        private static string AuthError(string error)
        {
            switch (error)
            {
                case "name_taken": return "имя занято — выбери другое";
                case "bad_credentials": return "неверное имя или пароль";
                case "too_many_attempts": return "слишком много попыток, подожди минуту";
                case "bad_name": return "имя: 2–24 символа";
                case "bad_password": return "пароль сервер не принял";
                default: return "сервер отказал: " + (error ?? "непонятный ответ");
            }
        }

        // ---- игра ----

        /// <summary>
        /// «Играть» у того, кто уже входил: пароль спрашивать незачем, токен продлевает
        /// сеанс сам. Не продлился — токен мёртв, и честнее сказать это, чем молча
        /// уронить игрока в игру без сети с чужой ревизией фермы.
        /// </summary>
        private async void OnPlay()
        {
            if (_busy) return;

            if (!NetSession.HasToken) { Refresh(); return; }

            _busy = true;
            SetInteractable(false);

            try
            {
                Status("вхожу…");
                var session = await ApiClient.ResumeSessionAsync();
                if (!session.Transport) { PlayOffline(); return; }

                if (session.Value == null || !session.Value.ok)
                {
                    NetSession.Clear();
                    Refresh();
                    Status("сеанс истёк, войди заново", bad: true);
                    return;
                }

                await LoadFarmAsync();
            }
            finally
            {
                _busy = false;
                SetInteractable(true);
            }
        }

        /// <summary>
        /// Забрать серверный снимок и уйти на ферму. Зовётся только после удачного входа:
        /// без токена этому запросу отвечать нечем.
        /// </summary>
        private async Awaitable LoadFarmAsync()
        {
            Status("забираю ферму…");
            var farm = await ApiClient.GetFarmAsync();
            if (!farm.Transport) { PlayOffline(); return; }
            if (farm.Value == null || !farm.Value.ok)
            {
                Status("сервер отказал: " + (farm.Value != null ? farm.Value.error : "непонятный ответ"), bad: true);
                return;
            }

            if (farm.Value.found)
            {
                FarmSaveData data = null;
                try { data = JsonUtility.FromJson<FarmSaveData>(farm.Value.state); }
                catch (Exception e) { Debug.LogError("[Menu] Серверный снимок не разобрался: " + e.Message); }

                if (data == null)
                {
                    // Снимок с сервера нечитаем — играть с локального можно, но писать
                    // поверх серверного нельзя: вдруг там единственная живая копия.
                    NetSession.PutBlocked = true;
                    Status("серверный снимок не читается — играю с локального, на сервер не пишу", bad: true);
                    GameFlow.ContinueLocal();
                    return;
                }

                NetSession.FarmRev = farm.Value.rev;
                double offline = Math.Max(0.0, farm.Value.serverNow - farm.Value.savedAt);
                GameFlow.StartFarm(new PendingLoad { Data = data, OfflineSeconds = offline, Source = "сервер" });
            }
            else
            {
                // На сервере пусто: локальная партия (если есть) поедет туда первым же
                // сейвом — так старые игроки перевозят ферму, не замечая переезда.
                NetSession.FarmRev = -1;
                GameFlow.ContinueLocal();
            }
        }

        /// <summary>Сервер молчит — играем без сети, и это сказано игроку прямым текстом.</summary>
        private void PlayOffline()
        {
            NetSession.LoggedIn = false;
            NetStatus.Set("сервер недоступен — играем без сети");
            GameFlow.ContinueLocal();
        }

        // ---- новая игра ----

        /// <summary>
        /// Новая игра поверх существующей — единственное необратимое действие в меню,
        /// и единственное, которое спрашивает. Спрашиваем и ради серверной партии:
        /// локального сейва может не быть, а ферма на сервере — есть.
        /// </summary>
        private void OnNewGame()
        {
            if (!FarmSave.Exists && !NetSession.HasToken) { OnConfirmNewGame(); return; }
            if (_confirm != null) _confirm.style.display = DisplayStyle.Flex;
        }

        private async void OnConfirmNewGame()
        {
            if (_busy) return;
            _busy = true;
            SetInteractable(false);

            try
            {
                HideConfirm();

                // Продлить сеанс надо и здесь: иначе новая партия играется «без сети» и первый
                // же вечер работы не уедет на сервер. Не вышло — играем без сети, но вслух.
                if (NetSession.HasToken)
                {
                    Status("вхожу…");
                    var session = await ApiClient.ResumeSessionAsync();
                    if (!session.Transport)
                    {
                        NetStatus.Set("сервер недоступен — новая партия без сети");
                    }
                    else if (session.Value == null || !session.Value.ok)
                    {
                        // Сеанс протух. Молчать нельзя: partия пойдёт мимо сервера, и вечер
                        // работы останется в локальном кэше, о чём игрок узнает слишком поздно.
                        NetSession.Clear();
                        NetStatus.Set("сеанс истёк — новая партия без сети, войди заново из меню");
                    }
                }

                GameFlow.NewGame();
            }
            finally
            {
                _busy = false;
                SetInteractable(true);
            }
        }

        private void HideConfirm()
        {
            if (_confirm != null) _confirm.style.display = DisplayStyle.None;
        }

        // ---- смена пароля ----

        private void ShowPassword()
        {
            if (_passOverlay == null) return;

            if (_passCurrent != null) _passCurrent.value = "";
            if (_passNext != null) _passNext.value = "";
            if (_passRepeat != null) _passRepeat.value = "";
            PassMessage("");

            _passOverlay.style.display = DisplayStyle.Flex;
        }

        private void HidePassword()
        {
            if (_passOverlay == null) return;
            _passOverlay.style.display = DisplayStyle.None;
        }

        private async void OnChangePassword()
        {
            if (_busy) return;

            string current = _passCurrent != null ? _passCurrent.value : "";
            string next = _passNext != null ? _passNext.value : "";
            string repeat = _passRepeat != null ? _passRepeat.value : "";

            // Оба отказа разбираются здесь: повтор сервер не видит вовсе, длину — тем более.
            if (next != repeat) { PassMessage("новый пароль и повтор не совпали", bad: true); return; }
            if (next.Length < MinPassword) { PassMessage("новый пароль — от " + MinPassword + " символов", bad: true); return; }
            if (current.Length == 0) { PassMessage("нужен текущий пароль", bad: true); return; }

            _busy = true;
            SetInteractable(false);

            try
            {
                PassMessage("меняю…");

                // Соль хеша — имя игрока: тем же именем он входил, им же сервер найдёт аккаунт.
                string name = NetSession.PlayerName;
                var res = await ApiClient.ChangePasswordAsync(
                    ApiClient.HashPassword(name, current), ApiClient.HashPassword(name, next));

                if (!res.Transport) { PassMessage("сервер не ответил — пароль остался прежним", bad: true); return; }

                if (res.Value == null || !res.Value.ok)
                {
                    string error = res.Value != null ? res.Value.error : null;
                    PassMessage(error == "bad_credentials"
                        ? "текущий пароль не подошёл"
                        : AuthError(error), bad: true);
                    return;
                }

                // Новый токен сетевой слой уже положил в NetSession — эта игра остаётся
                // вошедшей, а остальные устройства сервер погасил. Так и говорим.
                HidePassword();
                Status("пароль изменён, другие устройства придётся впустить заново");
            }
            finally
            {
                _busy = false;
                SetInteractable(true);
            }
        }

        // ---- смена игрока ----

        /// <summary>
        /// Устройство переходит к другому хозяину. Локальный сейв уносим вместе с сеансом:
        /// он кэш прежнего игрока, и оставленный — уехал бы на сервер новому первым же
        /// сохранением (пустой ферме сервер отвечает «фермы нет», и в дело идёт локальная).
        /// Ферма прежнего хозяина от этого не страдает — она на сервере.
        /// </summary>
        private async void OnSwitchPlayer()
        {
            if (_busy) return;
            _busy = true;
            SetInteractable(false);

            try
            {
                Status("выхожу…");
                var res = await ApiClient.LogoutAsync();

                NetSession.Clear();
                FarmSave.Delete();
                // Накопитель гостевой помощи — тоже персональный: без чистки помощь
                // аккаунта A влилась бы первому вошедшему здесь аккаунту B (судья этапа 8).
                PlayerPrefs.DeleteKey(Farm.Farming.GuestMode.PendingHelpKey);
                Refresh();

                Status(res.Transport
                    ? "вышли — ферма ждёт на сервере, войди под своим именем"
                    : "сервер не ответил, но с этого устройства мы вышли");
            }
            finally
            {
                _busy = false;
                SetInteractable(true);
            }
        }

        // ---- мелочи ----

        private static void MakePassword(TextField field)
        {
            if (field != null) field.isPasswordField = true;
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetInteractable(bool value)
        {
            if (_play != null) _play.SetEnabled(value);
            if (_newGame != null) _newGame.SetEnabled(value);
            if (_login != null) _login.SetEnabled(value);
            if (_register != null) _register.SetEnabled(value);
            if (_changePassword != null) _changePassword.SetEnabled(value);
            if (_switchPlayer != null) _switchPlayer.SetEnabled(value);
        }

        private void Status(string text, bool bad = false)
        {
            if (_netStatus == null) return;
            _netStatus.text = text;
            _netStatus.EnableInClassList("menu__net-status--bad", bad);
        }

        private void PassMessage(string text, bool bad = false)
        {
            if (_passMessage == null) return;
            _passMessage.text = text;
            _passMessage.EnableInClassList("menu__net-status--bad", bad);
        }

        private void OnNetStatusChanged(string line) => Status(line);
    }
}

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
    /// Вся сетевая асинхронщина заканчивается здесь, до смены сцены: «Играть» сначала
    /// договаривается с сервером (вход, снимок фермы), упаковывает результат в
    /// <see cref="GameFlow.StartFarm"/> — и только потом грузит ферму, чей порядок старта
    /// жёсткий и синхронный. Сервер молчит — играем без сети, и об этом сказано вслух:
    /// молчаливый отказ здесь был бы потерянной фермой.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/Main Menu")]
    public sealed class MainMenu : MonoBehaviour
    {
        private VisualElement _root;
        private Button _play;
        private Button _newGame;
        private Label _saveInfo;
        private Label _netStatus;
        private TextField _nameField;
        private VisualElement _confirm;

        /// <summary>Сетевой разговор уже идёт — второй клик по «Играть» не должен начать новый.</summary>
        private bool _busy;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>()?.rootVisualElement;
            if (_root == null) { enabled = false; return; }

            _play = _root.Q<Button>("menu-continue");
            _newGame = _root.Q<Button>("menu-new");
            _saveInfo = _root.Q<Label>("menu-save-info");
            _netStatus = _root.Q<Label>("menu-net-status");
            _nameField = _root.Q<TextField>("menu-name");
            _confirm = _root.Q<VisualElement>("menu-confirm");

            var quit = _root.Q<Button>("menu-quit");

            if (_play != null) _play.clicked += OnPlay;
            if (_newGame != null) _newGame.clicked += OnNewGame;

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

            NetStatus.Changed += OnNetStatusChanged;

            HideConfirm();
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

            // Поле имени видно только гостю: у кого есть токен, у того есть и имя.
            if (_nameField != null)
                _nameField.style.display = known ? DisplayStyle.None : DisplayStyle.Flex;

            // «Играть» видна всегда, в отличие от старого «Продолжить»: даже без локального
            // сейва партия может ждать на сервере, а новичку первая кнопка и адресована.
            if (_play != null)
                _play.text = known ? "Играть — " + NetSession.PlayerName : "Играть";

            if (_saveInfo == null) return;

            _saveInfo.text = save != null ? save.Describe() : "новая ферма ждёт хозяина";
            _saveInfo.EnableInClassList("menu__info--empty", save == null);
        }

        // ---- вход и запуск ----

        private async void OnPlay()
        {
            if (_busy) return;
            _busy = true;
            SetInteractable(false);

            try
            {
                if (!NetSession.HasToken)
                {
                    string name = _nameField != null ? _nameField.value.Trim() : "";
                    if (name.Length < 2)
                    {
                        Status("имя фермера — от 2 до 24 символов", bad: true);
                        return;
                    }

                    Status("подключаюсь…");
                    var reg = await ApiClient.RegisterAsync(name);
                    if (!reg.Transport) { PlayOffline(); return; }
                    if (reg.Value == null || !reg.Value.ok)
                    {
                        string error = reg.Value != null ? reg.Value.error : null;
                        Status(error == "name_taken"
                            ? "имя занято — выбери другое"
                            : "сервер отказал: " + (error ?? "непонятный ответ"), bad: true);
                        return;
                    }
                }
                else
                {
                    Status("вхожу…");
                    var login = await ApiClient.LoginAsync();
                    if (!login.Transport) { PlayOffline(); return; }
                    if (login.Value == null || !login.Value.ok)
                    {
                        // Токен умер — например, базу на сервере завели заново. Честнее
                        // попросить имя снова, чем бесконечно тыкаться мёртвым ключом.
                        NetSession.Clear();
                        Refresh();
                        Status("вход не удался — введи имя, заведём ферму заново", bad: true);
                        return;
                    }
                }

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
            finally
            {
                _busy = false;
                SetInteractable(true);
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

                // Войти надо и здесь: иначе новая партия играется «без сети» и первый же
                // вечер работы не уедет на сервер. Не вышло — играем без сети, но вслух.
                if (NetSession.HasToken)
                {
                    Status("вхожу…");
                    var login = await ApiClient.LoginAsync();
                    if (!login.Transport) NetStatus.Set("сервер недоступен — новая партия без сети");
                    else if (login.Value == null || !login.Value.ok) NetSession.Clear();
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

        // ---- мелочи ----

        private void SetInteractable(bool value)
        {
            if (_play != null) _play.SetEnabled(value);
            if (_newGame != null) _newGame.SetEnabled(value);
        }

        private void Status(string text, bool bad = false)
        {
            if (_netStatus == null) return;
            _netStatus.text = text;
            _netStatus.EnableInClassList("menu__net-status--bad", bad);
        }

        private void OnNetStatusChanged(string line) => Status(line);
    }
}

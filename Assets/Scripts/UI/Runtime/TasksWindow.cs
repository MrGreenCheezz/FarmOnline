using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Окно «Дела»: доска заказов горожан и лестница вех.
    /// <para>
    /// Зачем одно окно на две вкладки. Заказ и веха отвечают на один вопрос — «чем сейчас
    /// заняться и что за это будет». Разведи их по двум кнопкам топбара, и игрок будет
    /// помнить не свои цели, а расположение кнопок.
    /// </para>
    /// <para>
    /// Окно ничего не считает само: доска выводится из времени (<see cref="FarmOrders"/>),
    /// пороги вех висят на счётчиках фермы (<see cref="FarmAchievements"/>). Здесь только
    /// показ и одно действие — «Сдать». Дубль правил в интерфейсе — ровно тот путь, которым
    /// показанное и настоящее расходятся.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Tasks Window")]
    public sealed class TasksWindow : MonoBehaviour
    {
        /// <summary>Что показано во вкладке.</summary>
        private enum Tab { Orders = 0, Goals = 1 }

        [Tooltip("Сколько секунд держать строку об удаче или отказе под списком.")]
        [SerializeField, Min(0.5f)] private float _messageDuration = 3f;

        [Tooltip("Сколько секунд висит поздравление с вехой.")]
        [SerializeField, Min(1f)] private float _toastDuration = 3.6f;

        private VisualElement _overlay;
        private VisualElement _tabs;
        private VisualElement _list;
        private Label _note;
        private Label _message;
        private Label _toast;
        private Button _openButton;

        // Кнопка и её вкладка идут парой, а не по индексу в перечислении: порядок вкладок
        // в окне — вопрос вёрстки, и связывать его с числами enum значит однажды поменять
        // одно, не тронув другое.
        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<Tab> _tabValues = new List<Tab>();
        private Tab _active = Tab.Orders;

        private IInventory _storage;
        private Wallet _wallet;

        private float _messageTimer;
        private float _toastTimer;

        // Вехи приходят пачкой чаще, чем кажется: одна покупка расширения даёт ступень фермы,
        // один крупный заказ — и счёт заказов, и порог золота. Без очереди из трёх поздравлений
        // игрок увидел бы последнее, а два оплаченных прошли бы молча.
        private readonly Queue<string> _toasts = new Queue<string>();

        // Окно доски меняется само по себе, без всякого события: доска выведена из времени.
        // Держим номер последнего показанного окна, чтобы заметить смену и перерисоваться.
        private long _shownWindow = long.MinValue;

        // Срок на строке считается в минутах, а тикает в секундах: перерисовывать список
        // каждый кадр ради надписи, которая меняется раз в минуту, незачем.
        private float _noteTimer;

        public bool IsOpen => _overlay != null && _overlay.style.display != DisplayStyle.None;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) { enabled = false; return; }

            _overlay = root.Q<VisualElement>("tasks-overlay");
            _tabs = root.Q<VisualElement>("tasks-tabs");
            _list = root.Q<VisualElement>("tasks-list");
            _note = root.Q<Label>("tasks-note");
            _message = root.Q<Label>("tasks-message");
            _toast = root.Q<Label>("achievement-toast");

            _openButton = root.Q<Button>("tasks-button");
            if (_openButton != null) _openButton.clicked += Toggle;

            var close = root.Q<Button>("tasks-close");
            if (close != null) close.clicked += CloseByPlayer;

            // Клик по затемнению закрывает, клик по самому окну — нет. Как в магазине.
            if (_overlay != null) _overlay.RegisterCallback<ClickEvent>(OnOverlayClick);

            FarmOrders.Changed += OnBoardChanged;
            FarmAchievements.Earned_ += OnAchievementEarned;
            GuestMode.Changed += OnGuestChanged;

            Bind();
            BuildTabs();
            Close();
            OnGuestChanged();
        }

        private void OnDisable()
        {
            FarmOrders.Changed -= OnBoardChanged;
            FarmAchievements.Earned_ -= OnAchievementEarned;
            GuestMode.Changed -= OnGuestChanged;

            if (_storage != null) _storage.Changed -= OnStorageChanged;
            if (_wallet != null) _wallet.Changed -= OnWalletChanged;

            _storage = null;
            _wallet = null;

            // Недосказанные поздравления не переживают выключение: всплыть они смогут только
            // на следующей ферме, где им нечего объяснять.
            _toasts.Clear();
            _toastTimer = 0f;
        }

        private void Bind()
        {
            if (_storage == null)
            {
                _storage = FarmingRuntime.Sink as IInventory;
                if (_storage != null) _storage.Changed += OnStorageChanged;
            }

            // Кошелёк нужен ради вехи «первая тысяча в кубышке»: её полоса растёт от денег,
            // и без подписки она стояла бы неподвижной до следующего открытия окна.
            if (_wallet == null)
            {
                _wallet = Wallet.Instance != null ? Wallet.Instance : FindFirstObjectByType<Wallet>();
                if (_wallet != null) _wallet.Changed += OnWalletChanged;
            }
        }

        // ---- открытие ----

        public void Open()
        {
            Bind();
            if (_overlay != null) _overlay.style.display = DisplayStyle.Flex;
            Message("");
            Rebuild();
            Farm.Juice.Sfx.Play(b => b.UiOpen);
        }

        public void Close()
        {
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
        }

        public void Toggle()
        {
            if (IsOpen) CloseByPlayer();
            else Open();
        }

        /// <summary>
        /// Закрытие рукой игрока: то же, что <see cref="Close"/>, но со звуком. Звук вешать
        /// на сам Close нельзя — его же зовут при старте и при уходе в гости, и игра
        /// щёлкала бы окном, которого никто не открывал.
        /// </summary>
        private void CloseByPlayer()
        {
            Close();
            Farm.Juice.Sfx.Play(b => b.UiClose);
        }

        private void OnOverlayClick(ClickEvent evt)
        {
            if (evt.target == _overlay) CloseByPlayer();
        }

        /// <summary>
        /// В гостях дел нет: склад чужой, счётчики чужие, сдавать нечего (<see cref="FarmOrders.TryFill"/>
        /// откажет и сама). Кнопку прячем — так же, как «Друзья» в <c>SocialWindows</c>.
        /// Правило держится и здесь, а не только видимостью кнопки: окно могло остаться
        /// открытым с момента до визита.
        /// </summary>
        private void OnGuestChanged()
        {
            bool guest = GuestMode.IsGuest;

            if (_openButton != null)
                _openButton.style.display = guest ? DisplayStyle.None : DisplayStyle.Flex;

            if (guest) Close();
        }

        // ---- события ----

        private void OnBoardChanged()
        {
            if (IsOpen) Rebuild();
        }

        private void OnStorageChanged(IInventory inventory)
        {
            if (IsOpen) Rebuild();
        }

        private void OnWalletChanged(Wallet wallet, int delta)
        {
            if (IsOpen && _active == Tab.Goals) Rebuild();
        }

        /// <summary>
        /// Веха взята. Награда уже начислена системой — здесь только объявление: прибитая
        /// табличка под топбаром и звук. Не модальное окно: веха берётся посреди сбора, и
        /// диалог поперёк хода превратил бы награду в помеху.
        /// </summary>
        private void OnAchievementEarned(Achievement achievement)
        {
            ShowToast("Веха взята: " + achievement.Title +
                      (achievement.Gold > 0 ? "   +" + achievement.Gold + " зол." : ""));

            if (IsOpen && _active == Tab.Goals) Rebuild();
        }

        /// <summary>Поставить поздравление в очередь; висящее сейчас досматривается до конца.</summary>
        private void ShowToast(string text)
        {
            if (_toast == null) return;

            _toasts.Enqueue(text);
            if (_toastTimer <= 0f) NextToast();
        }

        private void NextToast()
        {
            if (_toasts.Count == 0)
            {
                _toast.style.display = DisplayStyle.None;
                return;
            }

            _toast.text = _toasts.Dequeue();
            _toast.style.display = DisplayStyle.Flex;
            _toast.style.opacity = 1f;
            _toastTimer = _toastDuration;

            // Звук — на показ, а не на событие: три вехи одним махом дали бы один слипшийся
            // щелчок, а так каждая объявляется своим, когда до неё дошла очередь.
            Farm.Juice.Sfx.Play(b => b.UiOpen);
        }

        private void Update()
        {
            // Время интерфейса — немасштабируемое: окно живёт и на паузе.
            float dt = Time.unscaledDeltaTime;

            if (_toastTimer > 0f)
            {
                _toastTimer -= dt;

                // Тает последнюю секунду: гаснущая с первого кадра надпись читается хуже,
                // чем висящая и потом исчезающая.
                if (_toastTimer <= 0f) NextToast();
                else if (_toastTimer < 1f) _toast.style.opacity = _toastTimer;
            }

            if (_messageTimer > 0f)
            {
                _messageTimer -= dt;
                if (_messageTimer <= 0f) Message("");
            }

            if (!IsOpen) return;

            _noteTimer -= dt;
            if (_noteTimer > 0f) return;
            _noteTimer = 1f;

            // Смена окна доски — единственное, что меняет заказы без события.
            if (_active == Tab.Orders && FarmOrders.Window != _shownWindow) { Rebuild(); return; }

            RefreshNote();
        }

        // ---- вкладки ----

        private void BuildTabs()
        {
            if (_tabs == null) return;

            _tabs.Clear();
            _tabButtons.Clear();
            _tabValues.Clear();

            AddTab(Tab.Orders, "Заказы");
            AddTab(Tab.Goals, "Успехи");

            SelectTab(_active);
        }

        private void AddTab(Tab tab, string caption)
        {
            var button = new Button(() => SelectTab(tab)) { text = caption };
            button.AddToClassList("tab");
            _tabs.Add(button);
            _tabButtons.Add(button);
            _tabValues.Add(tab);
        }

        private void SelectTab(Tab tab)
        {
            _active = tab;

            for (int i = 0; i < _tabButtons.Count; i++)
                _tabButtons[i].EnableInClassList("tab--active", _tabValues[i] == tab);

            Rebuild();
        }

        // ---- отрисовка ----

        private void Rebuild()
        {
            if (_list == null) return;

            _list.Clear();
            RefreshNote();

            if (_active == Tab.Orders) BuildOrders();
            else BuildGoals();
        }

        /// <summary>Строка над списком: у заказов — срок доски, у успехов — счёт взятого.</summary>
        private void RefreshNote()
        {
            if (_note == null) return;

            if (_active == Tab.Orders)
            {
                double left = FarmOrders.SecondsLeft;
                _note.text = left > 0.0
                    ? "новые заказы через " + GameHud.FormatDuration(left)
                    : "доска меняется прямо сейчас";
            }
            else
            {
                _note.text = "взято " + FarmAchievements.EarnedCount + " из " + FarmAchievements.List.Count;
            }
        }

        private void BuildOrders()
        {
            _shownWindow = FarmOrders.Window;

            var board = FarmOrders.Board();
            if (board.Count == 0)
            {
                // Пустая доска — не сбой, а состояние: заказы составляются из того, что игрок
                // умеет растить, и на голой ферме составлять их не из чего. Молчать нельзя.
                var empty = new Label("горожанам пока нечего у тебя просить — посади хоть что-нибудь");
                empty.AddToClassList("vacant");
                _list.Add(empty);
                return;
            }

            foreach (var order in board) _list.Add(BuildOrderCard(order));
        }

        private VisualElement BuildOrderCard(FarmOrder order)
        {
            bool filled = FarmOrders.IsFilled(order.Id);

            var card = new VisualElement();
            card.AddToClassList("task");
            card.EnableInClassList("task--done", filled);

            var head = new VisualElement();
            head.AddToClassList("task__head");

            var name = new Label(order.Customer);
            name.AddToClassList("task__name");
            head.Add(name);

            var gold = new Label("+" + order.Gold + " зол.");
            gold.AddToClassList("task__gold");
            head.Add(gold);

            card.Add(head);

            // Чего не хватает — собираем по тем же строкам, что и рисуем: второй проход по
            // складу разошёлся бы с показанными полосами при первом же изменении.
            var missing = new List<string>();

            for (int i = 0; i < order.Lines.Count; i++)
            {
                var line = order.Lines[i];
                if (!line.IsValid) continue;

                int have = filled ? line.Amount : order.Have(_storage, i);

                // «Кукуруза — 2», а не «2 Кукуруза»: названия ресурсов лежат в ассетах в
                // именительном падеже, и склонять их числом нечем. Тире вместо падежа
                // читается как список недостачи, а не как сломанная фраза.
                if (have < line.Amount) missing.Add(line.Resource.DisplayName + " — " + (line.Amount - have));

                card.Add(BuildLine(line.Resource.Icon, line.Resource.DisplayName, have, line.Amount));
            }

            if (filled)
            {
                var stamp = new Label("СДАНО");
                stamp.AddToClassList("task__stamp");
                card.Add(stamp);
                return card;
            }

            bool ready = missing.Count == 0 && _storage != null;

            var send = new Button(() => Fill(order)) { text = "Сдать" };
            send.AddToClassList("btn");
            send.AddToClassList("btn--accent");
            send.AddToClassList("task__action");
            send.SetEnabled(ready);
            card.Add(send);

            if (!ready)
            {
                // Выключенная кнопка обязана объясняться: правило заметного отказа
                // действует и до нажатия, а не только после.
                var note = new Label(_storage == null
                    ? "склад недоступен"
                    : "не хватает: " + string.Join(", ", missing));
                note.AddToClassList("task__short");
                card.Add(note);

                send.tooltip = note.text;
            }

            return card;
        }

        private void BuildGoals()
        {
            var list = FarmAchievements.List;
            for (int i = 0; i < list.Count; i++)
            {
                var achievement = list[i];
                bool has = FarmAchievements.Has(achievement.Id);

                var card = new VisualElement();
                card.AddToClassList("task");
                card.AddToClassList("task--goal");
                card.EnableInClassList("task--done", has);

                var head = new VisualElement();
                head.AddToClassList("task__head");

                var title = new Label(achievement.Title);
                title.AddToClassList("task__name");
                head.Add(title);

                var gold = new Label("+" + achievement.Gold + " зол.");
                gold.AddToClassList("task__gold");
                head.Add(gold);

                card.Add(head);

                // Взятая веха показывает свой порог, а не текущий счётчик: «10 000 / 100»
                // на «Сто корзин» читалось бы как ошибка вёрстки.
                int progress = has
                    ? achievement.Goal
                    : Mathf.Min(FarmAchievements.Progress(achievement), achievement.Goal);

                card.Add(BuildLine(null, Counter(achievement.Counter), progress, achievement.Goal));

                if (has)
                {
                    var stamp = new Label("ВЗЯТО");
                    stamp.AddToClassList("task__stamp");
                    stamp.AddToClassList("task__stamp--gold");
                    card.Add(stamp);
                }

                _list.Add(card);
            }
        }

        /// <summary>Строка «иконка — сделано/нужно» с жёлобом под ней. Общая для обеих вкладок.</summary>
        private static VisualElement BuildLine(Sprite icon, string caption, int have, int need)
        {
            var line = new VisualElement();
            line.AddToClassList("task-line");

            var head = new VisualElement();
            head.AddToClassList("task-line__head");

            // Иконки нет у вех: считаются урожаи и слияния, а не товар. Пустую рамку в
            // этом случае не ставим — она читалась бы как незагрузившаяся картинка.
            if (icon != null)
            {
                var image = new VisualElement();
                image.AddToClassList("task-line__icon");
                image.style.backgroundImage = new StyleBackground(icon);
                head.Add(image);
            }

            var name = new Label(caption);
            name.AddToClassList("task-line__name");
            head.Add(name);

            bool full = have >= need;

            var count = new Label(have + " / " + need);
            count.AddToClassList("task-line__count");
            count.EnableInClassList("task-line__count--full", full);
            head.Add(count);

            line.Add(head);

            var track = new VisualElement();
            track.AddToClassList("task-bar");

            var fill = new VisualElement();
            fill.AddToClassList("task-bar__fill");
            fill.EnableInClassList("task-bar__fill--full", full);
            fill.style.width = Length.Percent(need > 0 ? Mathf.Clamp01((float)have / need) * 100f : 0f);
            track.Add(fill);

            line.Add(track);
            return line;
        }

        /// <summary>За чем следит веха — словами, а не именем перечисления.</summary>
        private static string Counter(AchievementCounter counter)
        {
            switch (counter)
            {
                case AchievementCounter.Harvested: return "собрано урожаев";
                case AchievementCounter.Merges: return "слито пар";
                case AchievementCounter.FarmLevel: return "ступень фермы";
                case AchievementCounter.OrdersFilled: return "сдано заказов";
                case AchievementCounter.Gold: return "золота в кубышке";
                default: return "";
            }
        }

        // ---- сдача ----

        private void Fill(FarmOrder order)
        {
            if (FarmOrders.TryFill(order, out string refusal))
            {
                Message("заказ сдан: +" + order.Gold + " зол.", good: true);
                Farm.Juice.Sfx.Play(b => b.UiOpen);
            }
            else
            {
                // Отказ обязан быть заметным: показываем ровно ту причину, которую назвала
                // доска, — «на складе не всё, что просят» точнее общего «нельзя».
                Message(refusal, good: false);
                Farm.Juice.Sfx.Play(b => b.UiClose);
            }

            // Не ждать события: отклик на своё же нажатие обязан быть мгновенным. TryFill
            // поднимет Changed и сам, но перерисовка тут дешевле, чем догадка о порядке.
            Rebuild();
        }

        /// <summary>
        /// Строка под списком. Удача говорит зелёным, отказ — красным голосом
        /// <c>.window__message</c>: покрашенный подсказкой отказ это молчание буквами.
        /// </summary>
        private void Message(string text, bool good = false)
        {
            if (_message == null) return;

            _message.text = text;
            _message.EnableInClassList("window__message--good", good);
            _message.EnableInClassList("window__message--hint", string.IsNullOrEmpty(text));
            _messageTimer = string.IsNullOrEmpty(text) ? 0f : _messageDuration;
        }
    }
}

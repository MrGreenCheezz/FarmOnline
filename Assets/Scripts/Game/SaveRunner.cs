using System;
using UnityEngine;
using Farm.Characters;
using Farm.Farming;
using Farm.Net;

namespace Farm.Game
{
    /// <summary>
    /// Хранитель партии в сцене фермы: раскладывает готовый снимок на старте и складывает
    /// партию обратно — по таймеру, после значимых действий, при выходе и при возврате в меню.
    /// <para>
    /// Порядок исполнения поздний намеренно: к моменту его <c>Start</c> склад уже стал стоком,
    /// фермер нашёл свой дом, а стартовые грядки посадились. Только теперь сцену можно честно
    /// разобрать и собрать заново из снимка. Снимок к этому моменту уже добыт — сервер или
    /// диск опрашивало меню, сюда асинхронность не заходит.
    /// </para>
    /// <para>
    /// Локальная запись остаётся всегда — это кэш и оффлайн-запас. Поверх неё, когда мы
    /// вошли на сервер, тот же JSON уезжает PUT-ом: два хранилища, один текст.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Farm/Save Runner")]
    public sealed class SaveRunner : MonoBehaviour
    {
        [Tooltip("Как часто сохраняться само, секунд. 0 — только вручную и на выходе.")]
        [SerializeField, Min(0f)] private float _autosaveInterval = 60f;

        [Tooltip("Через сколько секунд после значимого действия (сбор, слияние) сохраниться.\n" +
                 "Вкладку в вебе закрывают не спрашивая — ждать планового автосейва слишком долго.")]
        [SerializeField, Min(0f)] private float _actionSaveDelay = 4f;

        [Tooltip("Писать в консоль о каждом сохранении. Полезно, пока систему обкатывают.")]
        [SerializeField] private bool _logSaves = true;

        private static SaveRunner _instance;
        private float _timer;
        private float _actionTimer = -1f;
        private bool _pushInFlight;
        private string _queuedJson;   // сейв, случившийся, пока предыдущий PUT ещё летел

        /// <summary>Партию уже загрузили — с этого момента её не стыдно сохранять.</summary>
        public bool Ready { get; private set; }

        private void Awake()
        {
            _instance = this;
            _timer = _autosaveInterval;
        }

        private void OnEnable()
        {
            FarmingEvents.Harvested += OnSignificantAction;
            FarmingEvents.Merged += OnSignificantAction;
            FarmingEvents.TierAscended += OnTierAscended;

            // Уход — такое же значимое действие, как сбор: он тратит дефицит (ведро,
            // подкормку), и потерять его закрытой вкладкой обиднее, чем лишний PUT.
            FarmWater.Poured += OnCared;
            FarmFertilizer.Applied += OnCared;
            FarmFeed.Fed += OnCared;   // мешок стоит три часа — терять его закрытой вкладкой нельзя
        }

        private void OnDisable()
        {
            FarmingEvents.Harvested -= OnSignificantAction;
            FarmingEvents.Merged -= OnSignificantAction;
            FarmingEvents.TierAscended -= OnTierAscended;
            FarmWater.Poured -= OnCared;
            FarmFertilizer.Applied -= OnCared;
            FarmFeed.Fed -= OnCared;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Start()
        {
            var pending = GameFlow.ConsumePending();

            // Гости и хозяева расходятся здесь и только здесь: дальше весь код спрашивает
            // GuestMode, а не гадает по обстоятельствам.
            if (pending != null && pending.IsGuest) GuestMode.Enter(pending.OwnerId, pending.OwnerName);
            else GuestMode.Exit();

            // Зерно характеров — ДО загрузки: его читает и живой приезд (ColonyRoster),
            // и миграционный пересев внутри FarmSave.ApplyFarmer (житель старой партии,
            // которого сейв ещё не знает). Позже было бы поздно.
            ColonyRoster.PlayerSeed = GuestMode.IsGuest ? null : NetSession.PlayerName;

            // Своё окно восстановления вокруг всего старта: FarmLevels.RestoreState ниже
            // поднимает Changed до Apply, и без окна каждая загрузка «объявляла» бы приезд
            // давно живущих жителей и выдавала фантомные вехи ступеней (золото которых тут же
            // затирал Wallet.RestoreState). Окно счётное — Apply внутри открывает своё.
            FarmingRuntime.BeginRestore();
            try
            {
                // Уровень фермы ставится ВСЕГДА и раньше всего остального, даже когда снимка нет.
                // FarmLevels — статика, а она переживает смену сцены: без этой строки «Начать
                // заново» после шестой ступени оставляло бы уровень 6 при земле в 13 метров,
                // а возвращение из гостей уносило бы домой чужой уровень — и записывало его
                // в свой сейв первым же автосохранением.
                FarmLevels.RestoreState(pending != null && pending.Data != null ? pending.Data.FarmLevel : 1);

                if (pending != null)
                {
                    if (pending.Data != null)
                    {
                        FarmSave.Apply(pending.Data, pending.OfflineSeconds);
                        if (_logSaves)
                            Debug.Log("[Save] Загружено (" + pending.Source + ", оффлайн " +
                                      Mathf.RoundToInt((float)pending.OfflineSeconds) + " с): " + pending.Data.Describe());
                    }
                    else
                    {
                        Debug.LogWarning("[Save] Просили продолжить, но снимок пуст — играем с нуля");
                    }
                }

                // Свежая партия: счётчики и вехи — статики и переживают смену сцены, ровно
                // как FarmLevels строкой выше. Без сброса «Начать заново» наследовала бы
                // прожитое старой партии и вписывала чужие числа в свежий сейв.
                if (pending == null || pending.Data == null)
                {
                    FarmProgress.RestoreState(0, 0);
                    FarmAchievements.RestoreState(System.Array.Empty<string>(), 0);
                }
            }
            finally
            {
                FarmingRuntime.EndRestore();
            }

            // Добрые дела из гостей вливаются дома: гостевая сцена жила на чужом
            // FarmProgress, и счёт помощи ждал в PlayerPrefs (GuestMode.ReportHelp).
            if (!GuestMode.IsGuest)
            {
                int pendingHelp = PlayerPrefs.GetInt(GuestMode.PendingHelpKey, 0);
                if (pendingHelp > 0)
                {
                    PlayerPrefs.DeleteKey(GuestMode.PendingHelpKey);
                    FarmProgress.AddHelpGiven(pendingHelp);
                    // Влитое — в сейв поскорее: prefs уже стёрты, и закрытая до автосейва
                    // вкладка потеряла бы счёт добрых дел.
                    RequestSoon();
                }
            }

            // Первый Check — сразу после восстановления, а не на первом сборе: он же лениво
            // подписывает кошелёк, и золото входящего ящика (ежедневка, рынок) проверяет
            // пороги вех без ожидания живого события фермы.
            if (!GuestMode.IsGuest) FarmAchievements.Check();

            // Свежая партия — пересеять характеры жителей от имени игрока: у каждого хозяина
            // свои люди, а не общий на всех слепок от имени объекта сцены. Зерно — имя игрока
            // плюс имя жителя: одно зерно на всех дало бы колонию клонов с одним характером.
            // Загруженная партия сюда не попадает: её характеры уже данные из сейва.
            bool freshFarm = pending == null || pending.Data == null;
            if (!GuestMode.IsGuest && freshFarm && !string.IsNullOrEmpty(NetSession.PlayerName))
            {
                foreach (var resident in FarmerRegistry.All)
                {
                    var traits = resident != null ? resident.Traits : null;
                    if (traits != null) traits.Reroll(NetSession.PlayerName + "·" + resident.name);
                }
            }

            Ready = true;

            // Дома — забрать накопившееся у друзей: подарки, помощь, отложенные награды.
            if (!GuestMode.IsGuest) NetEvents.PlayInbox(this);
        }

        private void Update()
        {
            if (!Ready) return;

            if (_actionTimer >= 0f)
            {
                _actionTimer -= Time.unscaledDeltaTime;
                if (_actionTimer < 0f)
                {
                    _timer = _autosaveInterval;   // только что сохранились — плановый пойдёт заново
                    Save("после действия");
                    return;
                }
            }

            if (_autosaveInterval <= 0f) return;

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;

            _timer = _autosaveInterval;
            Save("автосохранение");
        }

        private void OnSignificantAction(Growable plot, HarvestResult result) => RequestSoon();
        private void OnSignificantAction(Growable survivor, Growable absorbed) => RequestSoon();
        private void OnTierAscended(Growable survivor, GrowableDefinition from) => RequestSoon();
        private void OnCared(Growable plot) => RequestSoon();

        /// <summary>Попросить сохраниться скоро, но не сейчас: серия сборов подряд — один сейв.</summary>
        public void RequestSoon()
        {
            if (!Ready || _actionSaveDelay <= 0f) return;
            if (_actionTimer < 0f) _actionTimer = _actionSaveDelay;
        }

        /// <summary>Свернули игру на телефоне — это тот же выход, только без предупреждения.</summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused) Save("сворачивание");
        }

        private void OnApplicationQuit() => Save("выход");

        public void Save(string reason)
        {
            if (!Ready) return;

            // В гостях не сохраняется НИЧЕГО: снимок чужой, и записать его локально значило
            // бы перетереть собственную партию гостя фермой друга. Жёсткий страж, а не
            // договорённость: сюда сходятся и таймер, и пауза, и выход.
            if (GuestMode.IsGuest) return;

            var data = FarmSave.Capture();
            string json = FarmSave.Serialize(data);
            if (!FarmSave.WriteJson(json)) return;

            if (_logSaves) Debug.Log("[Save] Сохранено (" + reason + "): " + data.Describe());

            PushToServer(json);
        }

        /// <summary>
        /// Отправить снимок на сервер. Огонь-и-забыл: игра не ждёт сеть никогда, а об
        /// исходе сообщает строка состояния — отказ обязан быть заметным, но не блокирующим.
        /// </summary>
        private async void PushToServer(string json)
        {
            if (!NetSession.LoggedIn || NetSession.PutBlocked) return;

            // Один PUT в полёте: свежий снимок не теряется, а дожидается своей очереди —
            // иначе два PUT-а наперегонки сами устроили бы себе конфликт ревизий.
            if (_pushInFlight)
            {
                _queuedJson = json;
                return;
            }

            _pushInFlight = true;
            try
            {
                var res = await ApiClient.PutFarmAsync(json, NetSession.FarmRev);
                if (!res.Transport) return;   // NetStatus уже рассказал про сеть

                if (res.Value != null && res.Value.ok)
                {
                    NetSession.FarmRev = res.Value.rev;
                    NetStatus.Set("сохранено на сервере");
                }
                else if (res.Status == 409)
                {
                    // Кто-то другой пишет эту же ферму — вторая вкладка или второе устройство.
                    // Молча перетирать друг друга нельзя; остановиться молча — тоже.
                    NetSession.PutBlocked = true;
                    NetStatus.Set("ферма открыта в другом окне — на сервер не сохраняю");
                    Debug.LogError("[Save] Конфликт ревизий: ферма открыта где-то ещё, серверные сейвы остановлены");
                }
                else
                {
                    NetStatus.Fail("сохранение", res.Value != null ? res.Value.error : ("HTTP " + res.Status));
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
            finally
            {
                _pushInFlight = false;

                var queued = _queuedJson;
                _queuedJson = null;
                if (queued != null) PushToServer(queued);
            }
        }

        /// <summary>
        /// Сохранить, если в сцене есть кому. Зовётся из <see cref="GameFlow"/>, которому
        /// в момент перехода уже не на что опереться — сцена вот-вот выгрузится.
        /// </summary>
        public static void SaveIfPossible(string reason)
        {
            if (_instance != null) _instance.Save(reason);
        }
    }
}

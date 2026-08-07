using System;
using System.Collections.Generic;
using UnityEngine;
using Farm.Farming;
using Farm.Net;

namespace Farm.Game
{
    /// <summary>
    /// Асинхронная социалка на событиях сервера: помощь гостя уезжает хозяину, награда —
    /// самому себе, подарки — друзьям, а при входе домой всё накопившееся проигрывается.
    /// <para>
    /// Формы payload определяет только этот класс: сервер хранит непрозрачные строки,
    /// а значит, отправитель и получатель обязаны читать одну и ту же страницу — эту.
    /// Контракт типов — docs/ONLINE.md, «Типы событий».
    /// </para>
    /// </summary>
    public static class NetEvents
    {
        [Serializable]
        public sealed class HelpPayload
        {
            /// <summary>Стабильное имя грядки; пустое — событие от клиента без GUID-ов.</summary>
            public string Uid;

            public Vector3 Position;
            public string GrowableId;
            public int Level;
            public string ResourceId;
            public int Amount;
        }

        [Serializable]
        public sealed class RewardPayload
        {
            public int Gold;
        }

        [Serializable]
        public sealed class GiftPayload
        {
            public string ResourceId;
            public int Amount;
        }

        /// <summary>
        /// Payload рыночных событий. Их создаёт ТОЛЬКО сервер: в POST /api/events эти типы
        /// не входят, и клиентская подделка «мне продали за миллион» умирает на сервере.
        /// </summary>
        [Serializable]
        public sealed class MarketPayload
        {
            public string ResourceId;
            public int Amount;
            public int Gold;
        }

        [Serializable]
        public sealed class CarePayload
        {
            public string Uid;

            /// <summary>Номер посева. Пришёл не тот — грядку пересадили, событие честно протухло.</summary>
            public int CycleId;
        }

        /// <summary>Доля продажной цены, которую гость получает за собранную грядку.</summary>
        private const float HelpRewardShare = 0.05f;

        /// <summary>Золото гостю за полив чужой грядки. Жест вежливости, а не заработок.</summary>
        private const int CareRewardGold = 3;

        /// <summary>Дальше этого от записанной точки грядку хозяина не признаём той самой.</summary>
        private const float HelpMatchRadius = 0.75f;

        /// <summary>Сводка «пока тебя не было» готова — UI показывает окно.</summary>
        public static event Action<List<string>> InboxReady;

        // Подписка на помощь гостя. BeforeSceneLoad — после ResetStatics самого GuestMode,
        // который чистит подписчиков; без переподписки события помощи улетали бы в пустоту.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            InboxReady = null;
            GuestMode.Helped -= OnHelped;
            GuestMode.Helped += OnHelped;
            GuestMode.Cared -= OnCared;
            GuestMode.Cared += OnCared;
        }

        /// <summary>
        /// Гость полил чужую грядку: событие ухода — хозяину, символическая награда — себе.
        /// Награда едет событием, а не кладётся в кошелёк: гостевая сцена — черновик, и всё,
        /// что гость «заработал» в ней, его ферма получит только при следующем входе домой.
        /// </summary>
        private static async void OnCared(CareReport report)
        {
            if (!NetSession.LoggedIn || !GuestMode.IsGuest) return;

            var care = JsonUtility.ToJson(new CarePayload { Uid = report.Uid, CycleId = report.CycleId });

            try
            {
                var sent = await ApiClient.SendEventAsync(GuestMode.OwnerId, "care", care);
                if (!sent.Transport || sent.Value == null || !sent.Value.ok) return;

                var reward = JsonUtility.ToJson(new RewardPayload { Gold = CareRewardGold });
                await ApiClient.SendEventAsync(NetSession.PlayerId, "help_reward", reward);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Гость собрал грядку хозяину: событие помощи — хозяину, отложенная награда — себе.
        /// Сеть упала — помощь пропала, и об этом скажет NetStatus; лимит при этом уже
        /// потрачен. Щедрее наоборот было бы копить и слать пачкой, но вкладку гостя
        /// закрывают не спрашивая — надёжнее отправлять каждую сразу.
        /// </summary>
        private static async void OnHelped(HelpReport report)
        {
            if (!NetSession.LoggedIn || !GuestMode.IsGuest) return;

            int ownerId = GuestMode.OwnerId;

            var help = JsonUtility.ToJson(new HelpPayload
            {
                Uid = report.Uid,
                Position = report.Position,
                GrowableId = report.GrowableId,
                Level = report.Level,
                ResourceId = report.ResourceId,
                Amount = report.Amount,
            });

            try
            {
                var sent = await ApiClient.SendEventAsync(ownerId, "help", help);
                if (!sent.Transport || sent.Value == null || !sent.Value.ok) return;

                int gold = HelpRewardGold(report.ResourceId, report.Amount);
                var reward = JsonUtility.ToJson(new RewardPayload { Gold = gold });
                await ApiClient.SendEventAsync(NetSession.PlayerId, "help_reward", reward);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static int HelpRewardGold(string resourceId, int amount)
        {
            var registry = ContentRegistry.Instance;
            var resource = registry != null ? registry.Resource(resourceId) : null;
            int price = resource != null ? resource.SellPrice : 0;
            return Mathf.Max(1, Mathf.RoundToInt(price * amount * HelpRewardShare));
        }

        /// <summary>
        /// Отправить другу подарок со склада. Списание — сразу: подарок, который можно
        /// «отправить» дважды с одного запаса, — это станок для дюпа.
        /// </summary>
        public static async void SendGift(int friendId, string friendName, ResourceDefinition resource, int amount)
        {
            if (!NetSession.LoggedIn || resource == null || amount <= 0) return;

            // Сейвы стоят после конфликта ревизий — подарок при этом спишется со склада,
            // который никуда не запишется. Та же причина, по которой закрыт рынок.
            if (NetSession.PutBlocked)
            {
                NetStatus.Set("ферма открыта в другом окне — подарки закрыты");
                return;
            }

            var storage = FarmingRuntime.Sink as Inventory;
            if (storage == null) return;

            // Дарится только обычный сорт — как и на рынке: получатель зачисляет подарок
            // по идентификатору ресурса, сорта в payload нет, и отборное доехало бы к другу
            // обычным. Молча потерять надбавку за уход хуже, чем честно отказать.
            int taken = storage.TryRemove(resource, amount, ResourceGrade.Common);
            if (taken <= 0)
            {
                NetStatus.Set(storage.GetAmount(resource) > 0
                    ? "дарить можно обычный сорт — отборное довезёт только твоя лавка"
                    : "на складе нет: " + resource.DisplayName);
                return;
            }

            var payload = JsonUtility.ToJson(new GiftPayload { ResourceId = resource.Id, Amount = taken });
            var sent = await ApiClient.SendEventAsync(friendId, "gift", payload);

            if (sent.Transport && sent.Value != null && sent.Value.ok)
            {
                NetStatus.Set("подарок для " + friendName + " отправлен: " + taken + " × " + resource.DisplayName);
                FarmProgress.NoteGiftSent();
                SaveRunner.SaveIfPossible("после подарка");
            }
            else
            {
                // Сеть съела подарок — вернуть на склад, а не сделать вид, что так и было.
                storage.Add(resource, taken);
                if (sent.Transport)
                {
                    // Новые серверные замки подарков (этап 5) — человеческим языком,
                    // а не кодом: отказ обязан быть понятен, не только заметен.
                    string code = sent.Value != null ? sent.Value.error : null;
                    switch (code)
                    {
                        case "not_in_snapshot":
                            NetStatus.Set("сервер ещё не видел этого на складе — подожди пару секунд после сбора");
                            break;
                        case "gift_daily_limit":
                            NetStatus.Set("щедрость на сегодня исчерпана — потолок ценности подарков в сутки");
                            break;
                        case "bad_gift":
                            // Сервер отвечает bad_gift и на перебор штук (MAX_GIFT_AMOUNT,
                            // зеркалит server.py), и на ресурс вне каталога — текст шире.
                            NetStatus.Set("такой подарок не пройдёт — слишком много за раз или рынок его не знает");
                            break;
                        default:
                            NetStatus.Fail("подарок", code ?? "непонятный ответ");
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Забрать и проиграть входящие события у себя дома. Порядок жёсткий:
        /// применить → сохраниться → ack. Упади мы между сейвом и ack — продублируется
        /// разве что подарок; обратный порядок терял бы подарки насовсем.
        /// </summary>
        public static async void PlayInbox(SaveRunner runner)
        {
            if (!NetSession.LoggedIn || runner == null) return;

            try
            {
                var summary = new List<string>();

                // Разово — о слиянии 2.0: у слитых грядок урожай стал линейным (был ×2 за
                // уровень), и молча уменьшившийся «+N» при сборе читался бы как поломка.
                // Только партиям, жившим до правила: новичку «пересчитано» — обрывок чужого
                // changelog, его учит MergeHint. Ключ помечается в ShowSummary после
                // фактического показа — сгоревшая до показа строка не вернулась бы никогда.
                // Оффлайн-игрок (PlayInbox требует входа) увидит её при первом онлайн-входе:
                // ключ до тех пор не тратится.
                bool livedBeforeMerge2 = FarmProgress.TotalHarvested + FarmProgress.TotalMerges > 0;
                if (livedBeforeMerge2 && PlayerPrefs.GetInt(MergeNoticeKey, 0) == 0)
                {
                    _markMergeNotice = true;
                    summary.Add("слияние пересчитано: уровни складываются (потолок 20), урожай " +
                                "линеен уровню, а две двадцатки перерождаются в новую ступень");
                }

                // Ежедневная награда — в ту же сводку «пока тебя не было»: игрок открывает игру
                // и одним окном узнаёт всё, что случилось без него. Отдельное поздравление поверх
                // сводки было бы вторым окном подряд на пустом месте.
                await ClaimDaily(summary);

                var res = await ApiClient.GetEventsAsync();
                if (!res.Transport || res.Value == null || !res.Value.ok)
                {
                    ShowSummary(summary);
                    return;
                }

                var events = res.Value.events;
                if (events == null || events.Length == 0)
                {
                    if (summary.Count > 0) runner.Save("ежедневная награда");
                    ShowSummary(summary);
                    return;
                }

                var ids = new List<int>(events.Length);

                foreach (var ev in events)
                {
                    if (ev == null) continue;
                    ids.Add(ev.id);

                    try { Apply(ev, summary); }
                    catch (Exception e) { Debug.LogException(e); }
                }

                if (ids.Count == 0)
                {
                    ShowSummary(summary);
                    return;
                }

                runner.Save("события друзей");

                var ack = await ApiClient.AckEventsAsync(ids.ToArray());
                if (!ack.Transport || ack.Value == null || !ack.Value.ok)
                    Debug.LogWarning("[События] ack не прошёл — при следующем входе возможен повтор");

                ShowSummary(summary);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Спросить сервер про ежедневную награду и начислить её. Сервер сторожит календарь,
        /// золото кладёт клиент — как и всё остальное в модели доверия Ф1 (docs/ONLINE.md).
        /// Повторный вызов за те же сутки вернёт <c>claimed = false</c> и ничего не даст.
        /// </summary>
        private static async Awaitable ClaimDaily(List<string> summary)
        {
            try
            {
                var res = await ApiClient.ClaimDailyAsync();
                if (!res.Transport || res.Value == null || !res.Value.ok || !res.Value.claimed) return;

                var wallet = Wallet.Instance;
                if (wallet != null && res.Value.gold > 0) wallet.Add(res.Value.gold);

                // «Завтра +N» — чтобы серия существовала для игрока, а не только в базе:
                // без этой строки о лестнице ежедневки не знал никто, и рвать её было
                // не жалко. Числа зеркалят server.py (DAILY_BASE_GOLD 120 + 60/день,
                // потолок серии 7) — поменяешь там, поменяй и здесь.
                const int step = 60, maxStreak = 7;
                int streak = Mathf.Max(1, res.Value.streak);
                string tomorrow = streak < maxStreak
                    ? "завтра +" + (res.Value.gold + step) + " зол."
                    : "потолок серии";

                summary.Add(streak > 1
                    ? "ежедневная награда: +" + res.Value.gold + " зол. (день " + streak + " подряд · " + tomorrow + ")"
                    : "ежедневная награда: +" + res.Value.gold + " зол. (заходи завтра — будет +" + (res.Value.gold + step) + ")");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private const string MergeNoticeKey = "Farm.Merge2Notice";
        private static bool _markMergeNotice;

        private static void ShowSummary(List<string> summary)
        {
            if (summary == null || summary.Count == 0) return;

            var handler = InboxReady;
            if (handler == null) return;

            try { handler(summary); }
            catch (Exception e) { Debug.LogException(e); }

            // Разовая строка о слиянии 2.0 считается показанной только здесь — когда сводка
            // реально дошла до окна. Save сразу: WebGL без него теряет PlayerPrefs с вкладкой.
            if (_markMergeNotice)
            {
                _markMergeNotice = false;
                PlayerPrefs.SetInt(MergeNoticeKey, 1);
                PlayerPrefs.Save();
            }
        }

        private static void Apply(EventEntry ev, List<string> summary)
        {
            var registry = ContentRegistry.Instance;

            switch (ev.type)
            {
                case "gift":
                {
                    var gift = JsonUtility.FromJson<GiftPayload>(ev.payload);
                    var resource = gift != null && registry != null ? registry.Resource(gift.ResourceId) : null;
                    if (resource == null || gift.Amount <= 0)
                    {
                        Debug.LogWarning("[События] Подарок не разобрался: " + ev.payload);
                        return;
                    }

                    // С переливом, как market_returned: отвергнуть собственность из-за
                    // потолка склада нельзя — раньше излишек молча исчезал, а сводка
                    // врала полным числом.
                    NetMarket.AddWithOverflow(resource, gift.Amount);
                    summary.Add(ev.fromName + " прислал(а) " + gift.Amount + " × " + resource.DisplayName);
                    return;
                }

                case "help":
                {
                    var help = JsonUtility.FromJson<HelpPayload>(ev.payload);
                    if (help == null) return;

                    var plot = FindHelpedPlot(help);
                    if (plot == null)
                    {
                        // Грядку слили, передвинули или собрали сами — событие протухло.
                        // GUID-ов у грядок пока нет, потеря в пользу игрока (см. ONLINE.md).
                        Debug.Log("[События] Помощь " + ev.fromName + " не нашла грядку " + help.GrowableId);
                        return;
                    }

                    HarvestResult result;
                    if (plot.TryHarvest(out result))
                        summary.Add(ev.fromName + " собрал(а) для тебя " + result.Amount + " × "
                                    + (result.Resource != null ? result.Resource.DisplayName : "?"));
                    return;
                }

                case "care":
                {
                    var care = JsonUtility.FromJson<CarePayload>(ev.payload);
                    if (care == null || string.IsNullOrEmpty(care.Uid)) return;

                    var plot = FindPlotByUid(care.Uid);

                    // Три честных «протухло»: грядки нет, посев сменился, уже полита —
                    // в том числе вторым другом, чьё событие пришло раньше. Полив идёт
                    // мимо колодца: воду хозяина чужая забота тратить не может.
                    if (plot == null || plot.CycleId != care.CycleId || !plot.CanWater) return;
                    if (!plot.TryWater(FarmWater.CycleFraction)) return;

                    summary.Add(ev.fromName + " полил(а) твою грядку");
                    return;
                }

                case "market_sold":
                {
                    var deal = JsonUtility.FromJson<MarketPayload>(ev.payload);
                    if (deal == null || deal.Gold <= 0) return;

                    var wallet = Wallet.Instance;
                    if (wallet != null) wallet.Add(deal.Gold);

                    var sold = registry != null ? registry.Resource(deal.ResourceId) : null;
                    summary.Add("на рынке купили " + deal.Amount + " × "
                                + (sold != null ? sold.DisplayName : deal.ResourceId)
                                + ": +" + deal.Gold + " зол.");
                    return;
                }

                case "market_returned":
                {
                    var back = JsonUtility.FromJson<MarketPayload>(ev.payload);
                    var returned = back != null && registry != null ? registry.Resource(back.ResourceId) : null;
                    if (returned == null || back.Amount <= 0) return;

                    // С переливом: вернувшийся лот — собственность игрока, и потолок склада
                    // не повод её уничтожить.
                    NetMarket.AddWithOverflow(returned, back.Amount);
                    summary.Add("лот не нашёл покупателя, вернулось " + back.Amount + " × " + returned.DisplayName);
                    return;
                }

                case "help_reward":
                {
                    var reward = JsonUtility.FromJson<RewardPayload>(ev.payload);
                    if (reward == null || reward.Gold <= 0) return;

                    var wallet = Wallet.Instance;
                    if (wallet != null) wallet.Add(reward.Gold);
                    summary.Add("награда за помощь друзьям: +" + reward.Gold + " зол.");
                    return;
                }

                default:
                    Debug.LogWarning("[События] Неизвестный тип '" + ev.type + "' — пропущен");
                    return;
            }
        }

        private static Growable FindPlotByUid(string uid)
        {
            var all = GrowableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var plot = all[i];
                if (plot != null && plot.Uid == uid) return plot;
            }
            return null;
        }

        /// <summary>
        /// Найти у себя грядку, которую собрал гость. Главный ключ — стабильный Uid;
        /// событие без него (старый клиент) ищется по культуре, уровню и полуметру
        /// от записанной точки. Спелость обязательна в обоих путях: неспелая под тем же
        /// uid значит «хозяин успел собрать сам» — событие честно протухло.
        /// </summary>
        private static Growable FindHelpedPlot(HelpPayload help)
        {
            var all = GrowableRegistry.All;

            if (!string.IsNullOrEmpty(help.Uid))
            {
                for (int i = 0; i < all.Count; i++)
                {
                    var plot = all[i];
                    if (plot == null || plot.Uid != help.Uid) continue;
                    return plot.IsReady ? plot : null;
                }
                return null;
            }

            float bestSqr = HelpMatchRadius * HelpMatchRadius;
            Growable best = null;

            for (int i = 0; i < all.Count; i++)
            {
                var plot = all[i];
                if (plot == null || !plot.IsReady) continue;
                if (plot.Definition == null || plot.Definition.Id != help.GrowableId) continue;
                if (plot.Level != help.Level) continue;

                var d = plot.transform.position - help.Position;
                float sqr = d.x * d.x + d.z * d.z;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = plot;
            }

            return best;
        }
    }
}

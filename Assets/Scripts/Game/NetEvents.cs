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

        /// <summary>Доля продажной цены, которую гость получает за собранную грядку.</summary>
        private const float HelpRewardShare = 0.05f;

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

            var storage = FarmingRuntime.Sink as Inventory;
            if (storage == null) return;

            int taken = storage.TryRemove(resource, amount);
            if (taken <= 0)
            {
                NetStatus.Set("на складе нет: " + resource.DisplayName);
                return;
            }

            var payload = JsonUtility.ToJson(new GiftPayload { ResourceId = resource.Id, Amount = taken });
            var sent = await ApiClient.SendEventAsync(friendId, "gift", payload);

            if (sent.Transport && sent.Value != null && sent.Value.ok)
            {
                NetStatus.Set("подарок для " + friendName + " отправлен: " + taken + " × " + resource.DisplayName);
                SaveRunner.SaveIfPossible("после подарка");
            }
            else
            {
                // Сеть съела подарок — вернуть на склад, а не сделать вид, что так и было.
                storage.Add(resource, taken);
                if (sent.Transport)
                    NetStatus.Fail("подарок", sent.Value != null ? sent.Value.error : "непонятный ответ");
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
                var res = await ApiClient.GetEventsAsync();
                if (!res.Transport || res.Value == null || !res.Value.ok) return;

                var events = res.Value.events;
                if (events == null || events.Length == 0) return;

                var summary = new List<string>();
                var ids = new List<int>(events.Length);

                foreach (var ev in events)
                {
                    if (ev == null) continue;
                    ids.Add(ev.id);

                    try { Apply(ev, summary); }
                    catch (Exception e) { Debug.LogException(e); }
                }

                if (ids.Count == 0) return;

                runner.Save("события друзей");

                var ack = await ApiClient.AckEventsAsync(ids.ToArray());
                if (!ack.Transport || ack.Value == null || !ack.Value.ok)
                    Debug.LogWarning("[События] ack не прошёл — при следующем входе возможен повтор");

                if (summary.Count > 0)
                {
                    var handler = InboxReady;
                    if (handler != null)
                    {
                        try { handler(summary); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
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

                    FarmingRuntime.Sink.Add(resource, gift.Amount);
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

        /// <summary>
        /// Найти у себя грядку, которую собрал гость: та же культура, тот же уровень,
        /// всё ещё спелая и в полуметре от записанной точки.
        /// </summary>
        private static Growable FindHelpedPlot(HelpPayload help)
        {
            var all = GrowableRegistry.All;
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

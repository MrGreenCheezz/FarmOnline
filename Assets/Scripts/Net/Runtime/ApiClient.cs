using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Farm.Net
{
    /// <summary>
    /// Итог сетевого вызова. <see cref="Transport"/> — «ответ доехал и распарсился»;
    /// логическую удачу (<c>Value.ok</c>) проверяет вызывающий: 409 по rev или 401 по токену
    /// приходят сюда как нормальные ответы с честным статусом, а не как сбой транспорта.
    /// </summary>
    public readonly struct NetResult<T>
    {
        /// <summary>Ответ доехал и распарсился — пусть даже это JSON с ошибкой и не-2xx статусом.</summary>
        public readonly bool Transport;

        /// <summary>HTTP-статус; 0 — до сервера не добрались вовсе.</summary>
        public readonly long Status;

        /// <summary>Описание транспортной беды; null, когда <see cref="Transport"/> истинен.</summary>
        public readonly string Error;

        /// <summary>Распарсенный ответ; default при транспортной ошибке.</summary>
        public readonly T Value;

        public NetResult(bool transport, long status, string error, T value)
        {
            Transport = transport;
            Status = status;
            Error = error;
            Value = value;
        }
    }

    /// <summary>
    /// Тонкая обёртка над UnityWebRequest под контракт docs/ONLINE.md. Здесь только транспорт:
    /// собрать запрос, дождаться, распарсить, доложить о сбое в <see cref="NetStatus"/>.
    /// Политика — ретраи, оффлайн-режим, что делать с 409 — целиком у вызывающих:
    /// у входа, автосейва и панели друзей она разная, и общей быть не может.
    /// </summary>
    public static class ApiClient
    {
        /// <summary>Секунд до отказа. Асинхронной социалке ждать дольше незачем: лучше честный сбой в HUD.</summary>
        private const int TimeoutSeconds = 10;

        // ---- ядро ----

        private static async Awaitable<NetResult<T>> Send<T>(
            string method, string path, string body, bool auth, string revHeader = null)
        {
            using (var req = new UnityWebRequest(NetConfig.Url(path), method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = TimeoutSeconds;

                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.SetRequestHeader("Content-Type", "application/json");
                }

                if (auth) req.SetRequestHeader("X-Farm-Token", NetSession.Token);
                if (revHeader != null) req.SetRequestHeader("X-Farm-Rev", revHeader);

                // Ждём опросом, а не await у AsyncOperation: на awaiter операции полагаться
                // нельзя — опрос isDone одинаково честно работает и в WebGL, и в редакторе.
                var op = req.SendWebRequest();
                while (!op.isDone) await Awaitable.NextFrameAsync();

                long status = req.responseCode;
                string text = req.downloadHandler.text;

                // Транспортная беда — когда ответа нет совсем. Не-2xx с телом — это разговор
                // с сервером: он шлёт {"ok":false,"error":"…"} и честный статус, парсим как всё.
                if (req.result != UnityWebRequest.Result.Success && string.IsNullOrEmpty(text))
                    return new NetResult<T>(false, status, req.error, default);

                try
                {
                    T value = JsonUtility.FromJson<T>(text);
                    return new NetResult<T>(true, status, null, value);
                }
                catch (Exception e)
                {
                    // Дошло, но не наш JSON (прокси, чужая страница) — для вызывающего это
                    // такой же транспортный сбой: ответа, с которым можно работать, нет.
                    return new NetResult<T>(false, status, "битый ответ: " + e.Message, default);
                }
            }
        }

        /// <summary>Доложить о транспортном сбое. Логика — правило «отказ обязан быть заметным».</summary>
        private static NetResult<T> Report<T>(NetResult<T> result, string context)
        {
            if (!result.Transport) NetStatus.Fail(context, result.Error);
            return result;
        }

        // ---- аккаунт ----

        /// <summary>
        /// Завести аккаунт. При удаче токен, номер и имя сразу ложатся в <see cref="NetSession"/> —
        /// потерять токен между ответом и записью значит потерять ферму навсегда.
        /// </summary>
        public static async Awaitable<NetResult<RegisterResponse>> RegisterAsync(string name)
        {
            string body = JsonUtility.ToJson(new RegisterRequest { name = name });
            var result = Report(await Send<RegisterResponse>("POST", "/api/register", body, auth: false), "регистрация");

            if (result.Transport && result.Value != null && result.Value.ok)
            {
                NetSession.Token = result.Value.token;
                NetSession.PlayerId = result.Value.playerId;
                NetSession.PlayerName = result.Value.name;
                NetSession.LoggedIn = true;
                ServerClock.ApplyServerTime(result.Value.serverNow);
            }

            return result;
        }

        /// <summary>Войти по токену из <see cref="NetSession"/>. Тело пустое, но не null: сервер ждёт JSON.</summary>
        public static async Awaitable<NetResult<LoginResponse>> LoginAsync()
        {
            var result = Report(await Send<LoginResponse>("POST", "/api/login", "{}", auth: true), "вход");

            if (result.Transport && result.Value != null && result.Value.ok)
            {
                NetSession.PlayerId = result.Value.playerId;
                NetSession.PlayerName = result.Value.name;
                NetSession.LoggedIn = true;
                ServerClock.ApplyServerTime(result.Value.serverNow);
            }

            return result;
        }

        // ---- время ----

        /// <summary>Спросить серверное время. Ответ сразу уточняет <see cref="ServerClock"/> — за этим и ходим.</summary>
        public static async Awaitable<NetResult<TimeResponse>> GetTimeAsync()
        {
            var result = Report(await Send<TimeResponse>("GET", "/api/time", null, auth: false), "время сервера");

            if (result.Transport && result.Value != null && result.Value.ok)
                ServerClock.ApplyServerTime(result.Value.serverNow);

            return result;
        }

        // ---- ферма ----

        /// <summary>Забрать свою ферму. <c>found:false</c> — фермы на сервере ещё нет, и это не отказ.</summary>
        public static async Awaitable<NetResult<FarmResponse>> GetFarmAsync()
        {
            var result = Report(await Send<FarmResponse>("GET", "/api/farm", null, auth: true), "загрузка фермы");

            if (result.Transport && result.Value != null && result.Value.ok)
                ServerClock.ApplyServerTime(result.Value.serverNow);

            return result;
        }

        /// <summary>Посмотреть ферму друга (или свою по номеру). Чужую отдают только друзьям.</summary>
        public static async Awaitable<NetResult<FarmResponse>> GetFarmAsync(int playerId)
        {
            return Report(await Send<FarmResponse>("GET", "/api/farm/" + playerId, null, auth: true), "ферма друга");
        }

        /// <summary>
        /// Сохранить ферму на сервер. <paramref name="expectedRev"/> уходит в X-Farm-Rev:
        /// при расхождении сервер ответит 409 — ферма открыта в другом окне (−1 — не проверять).
        /// </summary>
        public static async Awaitable<NetResult<PutFarmResponse>> PutFarmAsync(string stateJson, int expectedRev)
        {
            return Report(
                await Send<PutFarmResponse>("PUT", "/api/farm", stateJson, auth: true,
                    revHeader: expectedRev.ToString()),
                "сохранение");
        }

        // ---- друзья ----

        /// <summary>Списки друзей и заявок в обе стороны.</summary>
        public static async Awaitable<NetResult<FriendsResponse>> GetFriendsAsync()
        {
            return Report(await Send<FriendsResponse>("GET", "/api/friends", null, auth: true), "список друзей");
        }

        /// <summary>Позвать в друзья по имени. Встречную заявку сервер сразу превращает в дружбу.</summary>
        public static async Awaitable<NetResult<OkResponse>> RequestFriendAsync(string name)
        {
            string body = JsonUtility.ToJson(new FriendRequestBody { name = name });
            return Report(await Send<OkResponse>("POST", "/api/friends/request", body, auth: true), "заявка в друзья");
        }

        /// <summary>Принять входящую заявку.</summary>
        public static async Awaitable<NetResult<OkResponse>> AcceptFriendAsync(int playerId)
        {
            string body = JsonUtility.ToJson(new FriendAcceptBody { playerId = playerId });
            return Report(await Send<OkResponse>("POST", "/api/friends/accept", body, auth: true), "подтверждение дружбы");
        }

        // ---- события ----

        /// <summary>Несъеденные входящие события: подарки и помощь, накопившиеся без нас.</summary>
        public static async Awaitable<NetResult<EventsResponse>> GetEventsAsync()
        {
            return Report(await Send<EventsResponse>("GET", "/api/events", null, auth: true), "события");
        }

        /// <summary>
        /// Отметить события съеденными. Зовётся ПОСЛЕ того, как они проиграны игроку:
        /// подарок, потерянный между ack и показом, не вернуть.
        /// </summary>
        public static async Awaitable<NetResult<OkResponse>> AckEventsAsync(int[] ids)
        {
            string body = JsonUtility.ToJson(new AckBody { ids = ids });
            return Report(await Send<OkResponse>("POST", "/api/events/ack", body, auth: true), "отметка событий");
        }

        /// <summary>Отправить другу событие (подарок, помощь). Payload — непрозрачная json-строка.</summary>
        public static async Awaitable<NetResult<OkResponse>> SendEventAsync(int toPlayerId, string type, string payload)
        {
            string body = JsonUtility.ToJson(new SendEventBody { toPlayerId = toPlayerId, type = type, payload = payload });
            return Report(await Send<OkResponse>("POST", "/api/events", body, auth: true), "отправка события");
        }
    }
}

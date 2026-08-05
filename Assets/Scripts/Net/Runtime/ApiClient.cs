using System;
using System.Security.Cryptography;
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

        // ---- пароль ----

        /// <summary>
        /// Предварительный хеш пароля: <c>sha256(имя_в_нижнем_регистре + ":" + пароль)</c>,
        /// 64 шестнадцатеричных символа в нижнем регистре. Именно он уходит в поле
        /// <c>password</c> — сам пароль не покидает устройство никогда.
        /// <para>
        /// Это <b>не замена https</b>: перехваченный хеш — тот же ключ к ферме. Смысл в другом —
        /// не дать утечь самому паролю, который игрок почти наверняка повторил на почте и ещё
        /// в трёх местах. Имя как соль лишает трафик и второй подсказки: одинаковые пароли
        /// разных игроков выглядят по-разному.
        /// </para>
        /// <para>
        /// Регистр имени снимается тем же способом, что и на сервере (уникальность имени
        /// без учёта регистра), иначе «Маша» и «маша» дали бы разные хеши одного пароля.
        /// </para>
        /// </summary>
        public static string HashPassword(string name, string password)
        {
            string source = (name ?? "").Trim().ToLowerInvariant() + ":" + (password ?? "");

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        // ---- аккаунт ----

        /// <summary>
        /// Завести аккаунт. При удаче токен, номер и имя сразу ложатся в <see cref="NetSession"/>.
        /// <para>
        /// <paramref name="password"/> — <b>уже хеш</b> из <see cref="HashPassword"/>: вызывающий
        /// хеширует сам. Так видно на месте вызова, что открытый пароль дальше поля ввода не идёт,
        /// и так эти методы можно проверить, не заводя ни одного настоящего пароля.
        /// </para>
        /// </summary>
        public static async Awaitable<NetResult<AuthResponse>> RegisterAsync(string name, string password)
        {
            string body = JsonUtility.ToJson(new AuthRequest { name = name, password = password });
            var result = Report(await Send<AuthResponse>("POST", "/api/register", body, auth: false), "регистрация");
            AdoptAccount(result);
            return result;
        }

        /// <summary>
        /// Войти по имени и паролю. Ответ несёт <b>новый</b> токен сессии этого устройства —
        /// вход с телефона не выгоняет из игры на компьютере.
        /// <para>
        /// <paramref name="password"/> — <b>уже хеш</b> из <see cref="HashPassword"/>, см.
        /// <see cref="RegisterAsync"/>.
        /// </para>
        /// </summary>
        public static async Awaitable<NetResult<AuthResponse>> LoginAsync(string name, string password)
        {
            string body = JsonUtility.ToJson(new AuthRequest { name = name, password = password });
            var result = Report(await Send<AuthResponse>("POST", "/api/login", body, auth: false), "вход");
            AdoptAccount(result);
            return result;
        }

        /// <summary>
        /// Продолжить сеанс сохранённым токеном, не спрашивая пароль, — обычный путь запуска.
        /// <c>401 bad_token</c> здесь не беда, а ответ: токен протух, надо показать вход паролем.
        /// Тело пустое, но не null: сервер ждёт JSON.
        /// </summary>
        public static async Awaitable<NetResult<SessionResponse>> ResumeSessionAsync()
        {
            var result = Report(await Send<SessionResponse>("POST", "/api/session", "{}", auth: true), "сеанс");

            if (result.Transport && result.Value != null && result.Value.ok)
            {
                NetSession.PlayerId = result.Value.playerId;
                NetSession.PlayerName = result.Value.name;
                NetSession.LoggedIn = true;
                ServerClock.ApplyServerTime(result.Value.serverNow);
            }

            return result;
        }

        /// <summary>
        /// Сменить пароль. Сервер гасит <b>все</b> сессии игрока, включая эту, и тут же выдаёт ей
        /// новый токен — он немедленно ложится в <see cref="NetSession"/>, иначе следующий же
        /// запрос ушёл бы с погашенным.
        /// <para>
        /// <paramref name="current"/> и <paramref name="next"/> — <b>уже хеши</b> из
        /// <see cref="HashPassword"/>, оба посолены именем игрока. Имени в подписи нет намеренно:
        /// в теле запроса оно не участвует (игрока называет токен), а три строковых параметра
        /// подряд — приглашение однажды перепутать их местами.
        /// </para>
        /// </summary>
        public static async Awaitable<NetResult<PasswordResponse>> ChangePasswordAsync(
            string current, string next)
        {
            string body = JsonUtility.ToJson(new PasswordRequest { current = current, next = next });
            var result = Report(
                await Send<PasswordResponse>("POST", "/api/password", body, auth: true), "смена пароля");

            if (result.Transport && result.Value != null && result.Value.ok
                && !string.IsNullOrEmpty(result.Value.token))
                NetSession.Token = result.Value.token;

            return result;
        }

        /// <summary>
        /// Погасить эту сессию на сервере. Забыть аккаунт локально — отдельное решение вызывающего
        /// (<see cref="NetSession.Clear"/>): «выйти сейчас» и «стереть ферму с этого устройства» —
        /// разные намерения, и сшивать их здесь нельзя.
        /// </summary>
        public static async Awaitable<NetResult<OkResponse>> LogoutAsync()
        {
            return Report(await Send<OkResponse>("POST", "/api/logout", "{}", auth: true), "выход");
        }

        /// <summary>
        /// Забрать ежедневную награду. Календарь сторожит сервер: местные часы игрок переводит,
        /// а суточный цикл самой фермы (240 секунд) к календарю отношения не имеет.
        /// <para>
        /// Ответ с <c>claimed = false</c> — не отказ, а «сегодня уже брал»: звать эту ручку при
        /// каждом входе нормально и безопасно.
        /// </para>
        /// </summary>
        public static async Awaitable<NetResult<DailyResponse>> ClaimDailyAsync()
        {
            return Report(await Send<DailyResponse>("POST", "/api/daily", "{}", auth: true), "ежедневная награда");
        }

        /// <summary>
        /// Общий хвост register/login: удачный ответ — это и есть аккаунт. Токен пишется первым:
        /// потерять его между ответом и записью значит потерять доступ к только что заведённой ферме.
        /// </summary>
        private static void AdoptAccount(NetResult<AuthResponse> result)
        {
            if (!result.Transport || result.Value == null || !result.Value.ok) return;

            NetSession.Token = result.Value.token;
            NetSession.PlayerId = result.Value.playerId;
            NetSession.PlayerName = result.Value.name;
            NetSession.LoggedIn = true;
            ServerClock.ApplyServerTime(result.Value.serverNow);
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

        /// <summary>
        /// Позвать в друзья по имени. Заявка именно ждёт ответа — дружба сама собой не случается;
        /// что вышло, называет <c>status</c> в <see cref="FriendRequestResponse"/>, и сказать это
        /// игроку обязан вызывающий: «уже друзья» и «заявка ушла» — разные новости.
        /// </summary>
        public static async Awaitable<NetResult<FriendRequestResponse>> RequestFriendAsync(string name)
        {
            string body = JsonUtility.ToJson(new FriendRequestBody { name = name });
            return Report(
                await Send<FriendRequestResponse>("POST", "/api/friends/request", body, auth: true),
                "заявка в друзья");
        }

        /// <summary>Принять входящую заявку.</summary>
        public static async Awaitable<NetResult<OkResponse>> AcceptFriendAsync(int playerId)
        {
            string body = JsonUtility.ToJson(new FriendAcceptBody { playerId = playerId });
            return Report(await Send<OkResponse>("POST", "/api/friends/accept", body, auth: true), "подтверждение дружбы");
        }

        /// <summary>
        /// Оборвать связь с игроком: дружбу, чужую заявку или свою. Что именно оборвалось,
        /// сервер называет в <c>removed</c> — три разных поступка за одной кнопкой, и слова
        /// игроку нужны разные.
        /// </summary>
        public static async Awaitable<NetResult<FriendRemoveResponse>> RemoveFriendAsync(int playerId)
        {
            string body = JsonUtility.ToJson(new FriendRemoveBody { playerId = playerId });
            return Report(
                await Send<FriendRemoveResponse>("POST", "/api/friends/remove", body, auth: true),
                "разрыв дружбы");
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

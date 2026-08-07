using System;

namespace Farm.Net
{
    // DTO под JsonUtility, поля — ровно как в контракте docs/ONLINE.md (сервер пишется
    // параллельно по тому же документу). Имена полей — это и есть протокол: переименуешь
    // поле — JsonUtility молча оставит default, и сломается не компиляция, а игра.
    // Типы по контракту: id/playerId/rev — int, времена — unix-секунды double,
    // state/payload — непрозрачные json-строки (произвольную вложенность JsonUtility не умеет).

    /// <summary>
    /// Тело POST /api/register и POST /api/login — они различаются только смыслом, не формой.
    /// В <c>password</c> лежит не пароль, а предварительный хеш (см. <see cref="ApiClient.HashPassword"/>):
    /// сам пароль по сети не ходит никогда.
    /// </summary>
    [Serializable]
    public class AuthRequest
    {
        public string name;
        public string password;
    }

    /// <summary>Ответ /api/register и /api/login: аккаунт плюс новый токен сессии этого устройства.</summary>
    [Serializable]
    public class AuthResponse
    {
        public bool ok;
        public int playerId;
        public string name;
        public string token;
        public double serverNow;
        public string error;
    }

    /// <summary>
    /// POST /api/session — ответ. Токена здесь нет намеренно: сеанс продлевается тем же
    /// токеном, что уже лежит в <see cref="NetSession"/>, новый выдаёт только вход паролем.
    /// </summary>
    [Serializable]
    public class SessionResponse
    {
        public bool ok;
        public int playerId;
        public string name;
        public double serverNow;
        public string error;
    }

    /// <summary>POST /api/password — тело. Оба поля — предварительные хеши, не пароли.</summary>
    [Serializable]
    public class PasswordRequest
    {
        public string current;
        public string next;
    }

    /// <summary>
    /// POST /api/password — ответ. Токен приходит новый: смена пароля гасит все прочие сессии,
    /// и старый токен этого устройства вместе с ними.
    /// </summary>
    [Serializable]
    public class PasswordResponse
    {
        public bool ok;
        public string token;
        public string error;
    }

    /// <summary>GET /api/time — ответ.</summary>
    [Serializable]
    public class TimeResponse
    {
        public bool ok;
        public double serverNow;
        public string error;
    }

    /// <summary>GET /api/farm и /api/farm/{id} — ответ. <c>found:false</c> — фермы ещё нет, это не отказ.</summary>
    [Serializable]
    public class FarmResponse
    {
        public bool ok;
        public bool found;
        public string state;
        public double savedAt;
        public int rev;
        public double serverNow;
        public string ownerName;
        public string error;
    }

    /// <summary>PUT /api/farm — ответ.</summary>
    [Serializable]
    public class PutFarmResponse
    {
        public bool ok;
        public int rev;
        public double savedAt;
        public string error;
    }

    /// <summary>Один друг (или заявка) в списках /api/friends.</summary>
    [Serializable]
    public class FriendEntry
    {
        public int playerId;
        public string name;
        public double lastSeen;
    }

    /// <summary>GET /api/friends — ответ.</summary>
    [Serializable]
    public class FriendsResponse
    {
        public bool ok;
        public FriendEntry[] friends;
        public FriendEntry[] incoming;
        public FriendEntry[] outgoing;
        public string error;
    }

    /// <summary>POST /api/friends/request — тело.</summary>
    [Serializable]
    public class FriendRequestBody
    {
        public string name;
    }

    /// <summary>
    /// POST /api/friends/request — ответ. <c>status</c> называет, что вышло из заявки:
    /// <c>pending</c> — ушла и ждёт, <c>incoming_exists</c> — встречная уже была (примите её),
    /// <c>already_friends</c> — вы и так друзья. Разные исходы требуют разных слов игроку,
    /// поэтому голого <c>ok</c> здесь мало.
    /// </summary>
    [Serializable]
    public class FriendRequestResponse
    {
        public bool ok;
        public string status;
        public string error;
    }

    /// <summary>POST /api/friends/accept — тело.</summary>
    [Serializable]
    public class FriendAcceptBody
    {
        public int playerId;
    }

    /// <summary>POST /api/friends/remove — тело. Одна кнопка на три случая, их различает сервер.</summary>
    [Serializable]
    public class FriendRemoveBody
    {
        public int playerId;
    }

    /// <summary>
    /// POST /api/friends/remove — ответ. <c>removed</c> говорит, что именно оборвали:
    /// <c>friend</c> — дружбу, <c>incoming</c> — отклонили чужую заявку, <c>outgoing</c> —
    /// отозвали свою. Игроку это разные поступки, и сообщение о них разное.
    /// </summary>
    [Serializable]
    public class FriendRemoveResponse
    {
        public bool ok;
        public string removed;
        public string error;
    }

    /// <summary>Одно входящее событие (подарок, помощь) в /api/events.</summary>
    [Serializable]
    public class EventEntry
    {
        public int id;
        public int fromPlayerId;
        public string fromName;
        public string type;
        public string payload;
        public double createdAt;
    }

    /// <summary>GET /api/events — ответ.</summary>
    [Serializable]
    public class EventsResponse
    {
        public bool ok;
        public EventEntry[] events;
        public string error;
    }

    /// <summary>POST /api/events — тело.</summary>
    [Serializable]
    public class SendEventBody
    {
        public int toPlayerId;
        public string type;
        public string payload;
    }

    /// <summary>POST /api/events/ack — тело.</summary>
    [Serializable]
    public class AckBody
    {
        public int[] ids;
    }

    /// <summary>Ответ без полезной нагрузки: только удача и код ошибки.</summary>
    [Serializable]
    public class OkResponse
    {
        public bool ok;
        public string error;
    }

    /// <summary>
    /// POST /api/daily — ежедневная награда. <c>claimed</c> ложно, когда за сегодня уже брали:
    /// это не отказ, а нормальный ответ, и золото в нём ноль.
    /// </summary>
    [Serializable]
    public class DailyResponse
    {
        public bool ok;
        public bool claimed;
        public int gold;
        public int streak;
        public double serverNow;
        public string error;
    }

    /// <summary>Один лот рынка. <c>gold</c> — что заплатит покупатель.</summary>
    [Serializable]
    public class MarketLot
    {
        public int id;
        public int sellerId;
        public string sellerName;
        public string resourceId;
        public int amount;
        public int gold;
    }

    /// <summary>GET /api/market.</summary>
    [Serializable]
    public class MarketResponse
    {
        public bool ok;
        public MarketLot[] lots;
        public string error;
    }

    /// <summary>POST /api/market/list — тело.</summary>
    [Serializable]
    public class MarketListBody
    {
        public string resourceId;
        public int amount;
    }

    /// <summary>POST /api/market/list — ответ: лот принят, вот его цены.</summary>
    [Serializable]
    public class MarketListedResponse
    {
        public bool ok;
        public int lotId;
        public int sellerGold;
        public int buyerGold;
        public string error;
    }

    /// <summary>POST /api/market/buy — тело.</summary>
    [Serializable]
    public class MarketBuyBody
    {
        public int lotId;
    }

    /// <summary>
    /// POST /api/market/buy — ответ. Покупатель списывает золото и кладёт ресурс
    /// ТОЛЬКО из этого ответа: событие ему не шлётся, чтобы не зачислить дважды.
    /// </summary>
    [Serializable]
    public class MarketBoughtResponse
    {
        public bool ok;
        public string resourceId;
        public int amount;
        public int gold;

        /// <summary>Возврат продавцовой доли при самовыкупе своего лота (иначе 0):
        /// отмена стоит ровно спред, и возврат едет сразу в ответе, не событием.</summary>
        public int refund;

        public string error;
    }
}

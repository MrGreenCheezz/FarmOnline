using System;

namespace Farm.Net
{
    // DTO под JsonUtility, поля — ровно как в контракте docs/ONLINE.md (сервер пишется
    // параллельно по тому же документу). Имена полей — это и есть протокол: переименуешь
    // поле — JsonUtility молча оставит default, и сломается не компиляция, а игра.
    // Типы по контракту: id/playerId/rev — int, времена — unix-секунды double,
    // state/payload — непрозрачные json-строки (произвольную вложенность JsonUtility не умеет).

    /// <summary>POST /api/register — тело.</summary>
    [Serializable]
    public class RegisterRequest
    {
        public string name;
    }

    /// <summary>POST /api/register — ответ.</summary>
    [Serializable]
    public class RegisterResponse
    {
        public bool ok;
        public int playerId;
        public string name;
        public string token;
        public double serverNow;
        public string error;
    }

    /// <summary>POST /api/login — ответ.</summary>
    [Serializable]
    public class LoginResponse
    {
        public bool ok;
        public int playerId;
        public string name;
        public double serverNow;
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

    /// <summary>POST /api/friends/accept — тело.</summary>
    [Serializable]
    public class FriendAcceptBody
    {
        public int playerId;
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
}

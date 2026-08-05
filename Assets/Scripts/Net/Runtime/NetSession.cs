using UnityEngine;

namespace Farm.Net
{
    /// <summary>
    /// Кто мы для сервера. Две половины с разной судьбой: токен, номер и имя переживают
    /// перезапуск (PlayerPrefs), а состояние текущего сеанса — вход, известная ревизия фермы,
    /// блокировка сохранения — живёт только до закрытия игры и стартует чистым.
    /// <para>
    /// Аккаунт — это имя и пароль, и они здесь не хранятся вовсе. Токен — сессия <b>этого
    /// устройства</b>: одна строка на браузер, поэтому вход с телефона не выкидывает из игры
    /// на компьютере. Чистка браузерного хранилища больше не значит потерю фермы — игрок
    /// входит паролем заново.
    /// </para>
    /// </summary>
    public static class NetSession
    {
        private const string TokenKey = "net.token";
        private const string PlayerIdKey = "net.playerId";
        private const string NameKey = "net.name";

        // Кэши поверх PlayerPrefs: null — «ещё не читали». Сами prefs никто, кроме нас,
        // не меняет, поэтому перечитывать на каждый запрос незачем.
        private static string _token;
        private static int? _playerId;
        private static string _playerName;

        /// <summary>
        /// Секрет текущей сессии. Пустая строка — на этом устройстве ещё не входили;
        /// ферма от этого не пропадает, она за именем и паролем.
        /// </summary>
        public static string Token
        {
            get => _token ?? (_token = PlayerPrefs.GetString(TokenKey, ""));
            set
            {
                _token = value ?? "";
                PlayerPrefs.SetString(TokenKey, _token);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Есть ли с чем идти на /api/session, минуя экран пароля.</summary>
        public static bool HasToken => !string.IsNullOrEmpty(Token);

        /// <summary>Серверный номер игрока. 0 — на этом устройстве ещё не входили.</summary>
        public static int PlayerId
        {
            get => _playerId ?? (_playerId = PlayerPrefs.GetInt(PlayerIdKey, 0)).Value;
            set
            {
                _playerId = value;
                PlayerPrefs.SetInt(PlayerIdKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Имя, под которым нас знают друзья.</summary>
        public static string PlayerName
        {
            get => _playerName ?? (_playerName = PlayerPrefs.GetString(NameKey, ""));
            set
            {
                _playerName = value ?? "";
                PlayerPrefs.SetString(NameKey, _playerName);
                PlayerPrefs.Save();
            }
        }

        // ---- сеанс (не в prefs) ----

        /// <summary>Сервер подтвердил нас в этом сеансе (паролем или токеном). Без входа PUT не имеет смысла.</summary>
        public static bool LoggedIn;

        /// <summary>
        /// Последняя известная ревизия серверной фермы — её несём в X-Farm-Rev при сохранении.
        /// −1 — ревизию ещё не узнавали (и сервер её не проверит).
        /// </summary>
        public static int FarmRev = -1;

        /// <summary>
        /// Ферма открыта в другом окне (409 по rev). Автосейв на сервер обязан замолчать:
        /// каждый следующий PUT затирал бы чужую, более свежую партию. Снимается только
        /// перезаходом, когда игрок сам решил, какое окно главное.
        /// </summary>
        public static bool PutBlocked;

        /// <summary>
        /// Забыть себя на этом устройстве: prefs, кэши и состояние сеанса. Аккаунт при этом
        /// цел — ферма живёт на сервере и открывается тем же именем с паролем.
        /// </summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.DeleteKey(PlayerIdKey);
            PlayerPrefs.DeleteKey(NameKey);
            PlayerPrefs.Save();
            ResetRuntime();
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload — чистим явно,
        // как FarmingRuntime.ResetStatics: иначе второй запуск играет «уже вошедшим».
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime()
        {
            LoggedIn = false;
            FarmRev = -1;
            PutBlocked = false;
            // Кэши prefs тоже в ноль: перечитаются лениво из настоящего хранилища.
            _token = null;
            _playerId = null;
            _playerName = null;
        }
    }
}

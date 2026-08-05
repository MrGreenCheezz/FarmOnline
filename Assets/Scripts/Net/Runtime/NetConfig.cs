using System;
using UnityEngine;

namespace Farm.Net
{
    /// <summary>
    /// Откуда брать сервер. Одно значение на всю игру, вычисляется один раз.
    /// <para>
    /// В веб-сборке адрес не настройка, а факт: API и статика живут на одном origin
    /// (так задуман сервер — без CORS), поэтому базу берём из адреса самой страницы.
    /// Вне браузера origin взять неоткуда — там адрес хранится в PlayerPrefs, чтобы
    /// редактор можно было направить на другую машину без пересборки.
    /// </para>
    /// </summary>
    public static class NetConfig
    {
        private const string PrefKey = "net.baseUrl";
        private const string DefaultBaseUrl = "http://localhost:8000";

        private static string _baseUrl;

        /// <summary>База вида <c>scheme://host[:port]</c>, без хвостового слэша.</summary>
        public static string BaseUrl => _baseUrl ?? (_baseUrl = Resolve());

        /// <summary>Полный адрес запроса. <paramref name="path"/> начинается со слэша: «/api/…».</summary>
        public static string Url(string path) => BaseUrl + path;

        private static string Resolve()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Origin страницы, на которой запущена игра. Разбираем через Uri, а не строками:
            // absoluteURL несёт путь и query, и «отрезать после третьего слэша» ломается
            // на адресах без пути. GetLeftPart(Authority) даёт ровно scheme://host[:port].
            string raw = Application.absoluteURL;
            if (!string.IsNullOrEmpty(raw)
                && Uri.TryCreate(raw, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            }

            // Страница открыта не по http (file:// при локальной проверке) — сервера за ней нет,
            // но игра не должна падать: пробуем стандартный локальный адрес.
            return DefaultBaseUrl;
#else
            string stored = PlayerPrefs.GetString(PrefKey, DefaultBaseUrl);
            if (string.IsNullOrEmpty(stored)) stored = DefaultBaseUrl;
            return stored.TrimEnd('/');
#endif
        }

        // Кэш переживает перезапуск Play Mode при отключённом domain reload — чистим явно,
        // иначе смена адреса в PlayerPrefs подействует только после перезапуска редактора.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _baseUrl = null;
        }
    }
}

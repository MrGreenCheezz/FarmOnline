using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Farm.Net;

namespace Farm.Game
{
    /// <summary>
    /// Готовый к раскладке снимок партии, пронесённый через смену сцен.
    /// Вся асинхронщина (сервер, чтение) обязана закончиться <b>до</b> загрузки сцены:
    /// внутри фермы порядок старта жёсткий и синхронный, и ломать его сетевым ожиданием нельзя.
    /// </summary>
    public sealed class PendingLoad
    {
        /// <summary>Снимок партии. Null — начинаем с чистой фермы.</summary>
        public FarmSaveData Data;

        /// <summary>Сколько настенных секунд ферма прожила закрытой — рост это догонит.</summary>
        public double OfflineSeconds;

        /// <summary>Откуда снимок — «сервер», «локально», «визит». Только для лога.</summary>
        public string Source = "локально";

        /// <summary>Это чужая ферма: сцена соберётся в режиме гостя и не сохранит ни байта.</summary>
        public bool IsGuest;
        public int OwnerId;
        public string OwnerName = "";
    }

    /// <summary>
    /// Переходы между меню и фермой и одно решение, которое надо через них пронести:
    /// какую партию раскладывать на старте.
    /// <para>
    /// Статик, а не объект в сцене: он живёт ровно в тот момент, когда сцены нет — старая
    /// выгружена, новая ещё не собрана. Смены сцен не перезагружают домен, поэтому намерение
    /// доезжает до фермы само.
    /// </para>
    /// </summary>
    public static class GameFlow
    {
        public const string MenuScene = "MainMenu";
        public const string FarmScene = "FarmerDemo";

        /// <summary>Оффлайн-дельта длиннее месяца — скорее сломанные часы, чем отпуск.</summary>
        private const double MaxOfflineSeconds = 30.0 * 86400.0;

        /// <summary>Что раскладывать на старте фермы. Null — чистая партия.</summary>
        public static PendingLoad Pending { get; private set; }

        /// <summary>Забрать намерение. Одноразовое: второй раз за партию ферма грузиться не должна.</summary>
        public static PendingLoad ConsumePending()
        {
            var pending = Pending;
            Pending = null;
            return pending;
        }

        /// <summary>Уйти на ферму с готовым снимком (или с null — с чистой фермой).</summary>
        public static void StartFarm(PendingLoad pending)
        {
            Pending = pending;
            SceneManager.LoadScene(FarmScene);
        }

        /// <summary>
        /// Продолжить с локального сохранения — путь игры без сети. Оффлайн-дельту здесь
        /// считает локальный UTC против штампа в сейве: часы игрока можно перевести, но без
        /// сервера ферма и так принадлежит только ему.
        /// </summary>
        public static void ContinueLocal()
        {
            var data = FarmSave.Read();

            double offline = 0.0;
            if (data != null && data.Version >= 2 && data.SavedAtUnix > 1e9)
                offline = Math.Clamp(ServerClock.UtcNowUnix - data.SavedAtUnix, 0.0, MaxOfflineSeconds);

            StartFarm(new PendingLoad { Data = data, OfflineSeconds = offline, Source = "локально" });
        }

        /// <summary>
        /// Начать заново. Старое сохранение стирается здесь, а не при первом автосейве:
        /// иначе новая партия, брошенная на первой минуте, оставила бы игрока с огрызком
        /// вместо его настоящей фермы. Серверную ферму перезапишет первый же автосейв —
        /// подтверждение игрок уже дал в меню.
        /// </summary>
        public static void NewGame()
        {
            FarmSave.Delete();
            NetSession.FarmRev = -1;   // перезаписать серверную не спрашивая: решение принято
            StartFarm(null);
        }

        public static void ToMenu()
        {
            SaveRunner.SaveIfPossible("возврат в меню");
            Pending = null;
            SceneManager.LoadScene(MenuScene);
        }

        /// <summary>
        /// Есть ли куда выходить. В браузере — некуда: вкладку закрывает игрок, а не игра,
        /// и кнопка «Выход» там обещала бы действие, которого не будет.
        /// </summary>
        public static bool CanQuit =>
#if UNITY_WEBGL && !UNITY_EDITOR
            false;
#else
            true;
#endif

        public static void Quit()
        {
            SaveRunner.SaveIfPossible("выход из игры");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Farm.Game
{
    /// <summary>
    /// Переходы между меню и фермой и одно решение, которое надо через них пронести:
    /// продолжать сохранённую партию или начинать новую.
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

        /// <summary>Игрок нажал «Продолжить» — ферме предстоит разложить сохранение.</summary>
        public static bool LoadRequested { get; private set; }

        /// <summary>Забрать намерение. Одноразовое: второй раз за партию ферма грузиться не должна.</summary>
        public static bool ConsumeLoadRequest()
        {
            bool requested = LoadRequested;
            LoadRequested = false;
            return requested;
        }

        public static void Continue()
        {
            LoadRequested = true;
            SceneManager.LoadScene(FarmScene);
        }

        /// <summary>
        /// Начать заново. Старое сохранение стирается здесь, а не при первом автосейве:
        /// иначе новая партия, брошенная на первой минуте, оставила бы игрока с огрызком
        /// вместо его настоящей фермы.
        /// </summary>
        public static void NewGame()
        {
            FarmSave.Delete();
            LoadRequested = false;
            SceneManager.LoadScene(FarmScene);
        }

        public static void ToMenu()
        {
            SaveRunner.SaveIfPossible("возврат в меню");
            LoadRequested = false;
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

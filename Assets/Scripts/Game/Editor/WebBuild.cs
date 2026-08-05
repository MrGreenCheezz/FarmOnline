using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Сборка веб-версии одной командой — и из меню, и из консоли (для CI и для скриптов).
    /// <para>
    /// Настройки выставляются здесь, а не руками в окне Player Settings, по той же причине,
    /// по которой в проекте всё остальное выведено из формул: настройка, живущая только в
    /// инспекторе, однажды разойдётся с тем, как игру на самом деле собирают.
    /// </para>
    /// </summary>
    public static class WebBuild
    {
        /// <summary>Куда кладём собранное. Рядом с проектом, а не внутри Assets.</summary>
        public const string OutputFolder = "Build/Web";

        [MenuItem("Farm/Собрать веб-версию")]
        public static void BuildFromMenu() => Build();

        /// <summary>Точка входа для запуска редактора из консоли (-executeMethod).</summary>
        public static void BuildFromCommandLine()
        {
            bool ok = Build();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Build()
        {
            ApplySettings();

            string output = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".", OutputFolder));
            Directory.CreateDirectory(output);

            var scenes = new[]
            {
                "Assets/Scenes/MainMenu.unity",
                "Assets/Scenes/FarmerDemo.unity"
            };

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None
            };

            Debug.Log("[Web] Сборка в " + output);
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[Web] Готово за " + summary.totalTime.TotalSeconds.ToString("F0") +
                          " с, размер " + (summary.totalSize / 1024f / 1024f).ToString("F1") + " МБ");
                return true;
            }

            Debug.LogError("[Web] Сборка провалилась: " + summary.result +
                           ", ошибок " + summary.totalErrors);
            return false;
        }

        /// <summary>
        /// Настройки, без которых веб-версия либо не соберётся, либо не заработает на
        /// простом статическом сервере.
        /// <para>
        /// Чего здесь нет: уровня качества. Он живёт в `ProjectSettings/QualitySettings.asset`
        /// (`m_PerPlatformDefaultQuality`), потому что задаётся не для сборки, а для платформы.
        /// Веб должен стоять на уровне 1 («PC»): нулевой — «Mobile», а он тянет Forward вместо
        /// Forward+, и рукописные шейдеры остаются без всего света, кроме главного.
        /// </para>
        /// </summary>
        public static void ApplySettings()
        {
            PlayerSettings.companyName = "Farm";
            PlayerSettings.productName = "Ферма";

            // Gzip, хотя brotli на 4.3 МБ меньше (14.4 против 18.7). Причина не в Unity:
            // brotli браузеры соглашаются принимать только по HTTPS. По http://localhost
            // он работает — localhost считается доверенным происхождением, — а по обычному
            // домену Chrome просто не просит br, и сервер честно отвечает 406. Gzip просят
            // везде. Появится настоящий сертификат — сюда вернётся Brotli и заберёт свои
            // 4.3 МБ обратно, а заодно заработает кеш данных: Cache API тоже требует HTTPS.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;

            // Кеш данных в IndexedDB: со второго захода игра стартует заметно быстрее.
            PlayerSettings.WebGL.dataCaching = true;

            // Исключения нужны: без них первая же ошибка в браузере оборачивается
            // молчаливым зависанием вместо сообщения в консоли.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            // Отладочные символы в вебе — это лишний мегабайт ради стектрейсов,
            // которые всё равно читает только разработчик у себя.
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;

            PlayerSettings.WebGL.template = "APPLICATION:Default";
            PlayerSettings.runInBackground = true;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);

            // Размер вместо скорости: игра про ожидание урожая, ей незачем последние проценты
            // производительности, а вот вес страницы игрок чувствует сразу.
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);

            // Обрезка Low. High однажды заподозрили в пропаже текста и снизили до Low —
            // не помогло, виноват был отсутствующий шрифт (см. UIFont). То есть High не
            // оправдан, а просто не проверен: разница между ними 0.9 МБ, и ради неё стоит
            // однажды собрать на High и открыть игру в браузере. Пока не проверено — Low.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);

            // Строка обязана называть то, что выставлено выше: по ней читают лог, когда сборка
            // не грузится в браузере, а 406 от сервера — это ровно спор о сжатии. Врущий лог
            // здесь дороже отсутствующего.
            Debug.Log("[Web] Настройки применены: Gzip, обрезка Low, IL2CPP под размер");
        }
    }
}

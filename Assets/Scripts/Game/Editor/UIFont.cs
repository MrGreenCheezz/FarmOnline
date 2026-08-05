using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Заводит интерфейсу собственный шрифт и прибивает его к <see cref="PanelSettings"/>.
    /// <para>
    /// Зачем это нужно вообще. UI Toolkit, если шрифт нигде не назван, берёт его из
    /// редакторских ресурсов — и в редакторе всё выглядит правильно. В собранной игре
    /// этих ресурсов нет, брать неоткуда, и текст исчезает целиком: разметка считается,
    /// элементы стоят по местам, цвет и размер верные, а букв нет. Ошибок при этом ноль,
    /// поэтому искать причину приходится зондом, а не по логу.
    /// </para>
    /// <para>
    /// Шрифт лежит файлом в проекте, и это не прихоть. Первая попытка сделала его из
    /// встроенного шрифта редактора — получился ассет со ссылкой на
    /// <c>C:/Windows/Fonts/arial.ttf</c>, режимом DynamicOS и флагом
    /// <c>DontSaveInBuild</c>: в редакторе работает, в сборку не попадает, в браузере
    /// системных шрифтов нет вовсе. Шрифт должен лежать в репозитории целиком.
    /// </para>
    /// <para>
    /// Inter взят из поставки самого Unity (<c>Editor/Data/Resources/Fonts</c>): лицензия
    /// SIL OFL разрешает распространение, кириллица на месте, рядом лежит текст лицензии.
    /// Атлас динамический — перечислять заранее все нужные буквы значит однажды
    /// недосчитаться «ё».
    /// </para>
    /// </summary>
    public static class UIFont
    {
        private const string FontsFolder = "Assets/Game/UI/Fonts";
        private const string SourceFontPath = FontsFolder + "/Inter-Regular.ttf";
        private const string FontAssetPath = FontsFolder + "/FarmUI.asset";
        private const string TextSettingsPath = FontsFolder + "/FarmTextSettings.asset";
        private const string PanelSettingsPath = "Assets/Game/UI/GameHudPanelSettings.asset";

        [MenuItem("Farm/Починить шрифт интерфейса")]
        public static void EnsureFromMenu() => Ensure();

        /// <summary>Точка входа для запуска редактора из консоли (-executeMethod).</summary>
        public static void EnsureFromCommandLine()
        {
            bool ok = Ensure();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Ensure()
        {
            if (!Directory.Exists(FontsFolder))
            {
                Directory.CreateDirectory(FontsFolder);
                AssetDatabase.Refresh();
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (source == null)
            {
                Debug.LogError("[Шрифт] Не найден " + SourceFontPath);
                return false;
            }

            var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (fontAsset != null && !SurvivesBuild(fontAsset))
            {
                Debug.LogWarning("[Шрифт] Прежний ассет в сборку не попадёт — пересоздаю");
                AssetDatabase.DeleteAsset(FontAssetPath);
                fontAsset = null;
            }

            if (fontAsset == null)
            {
                fontAsset = FontAsset.CreateFontAsset(source);
                fontAsset.name = "FarmUI";
                fontAsset.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

                // Материал и атлас родились вместе с ассетом, но сами по себе они нигде не
                // лежат: без этого после перезагрузки редактора от них останутся пустые ссылки.
                AddSubAsset(fontAsset.material, fontAsset);
                AddSubAsset(fontAsset.atlasTexture, fontAsset);

                Debug.Log("[Шрифт] Создан шрифтовый ассет из " + source.name);

                if (!SurvivesBuild(fontAsset))
                {
                    Debug.LogError("[Шрифт] Ассет всё равно не годится для сборки: " +
                                   "режим " + fontAsset.atlasPopulationMode + ", флаги " + fontAsset.hideFlags);
                    return false;
                }
            }

            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (textSettings == null)
            {
                textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
                textSettings.name = "FarmTextSettings";
                AssetDatabase.CreateAsset(textSettings, TextSettingsPath);
                Debug.Log("[Шрифт] Созданы настройки текста");
            }

            textSettings.defaultFontAsset = fontAsset;
            EditorUtility.SetDirty(textSettings);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                Debug.LogError("[Шрифт] Не найден " + PanelSettingsPath);
                return false;
            }

            // Через SerializedObject, а не через свойство: имя поля видно прямо в .asset,
            // а публичность свойства зависит от версии UI Toolkit.
            var so = new SerializedObject(panel);
            var property = so.FindProperty("textSettings");
            if (property == null)
            {
                Debug.LogError("[Шрифт] В PanelSettings нет поля textSettings — изменился формат");
                return false;
            }

            property.objectReferenceValue = textSettings;
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Шрифт] Готово: " + panel.name + " → " + textSettings.name + " → " + fontAsset.name +
                      " (режим " + fontAsset.atlasPopulationMode + ", флаги " + fontAsset.hideFlags + ")");
            return true;
        }

        /// <summary>
        /// Доживёт ли шрифт до плеера. Два способа не дожить: флаг «не сохранять в сборку»
        /// и режим DynamicOS — такой ассет ищет шрифт в системе, а в браузере системы нет.
        /// </summary>
        private static bool SurvivesBuild(FontAsset asset) =>
            (asset.hideFlags & HideFlags.DontSaveInBuild) == 0 &&
            asset.atlasPopulationMode != AtlasPopulationMode.DynamicOS;

        private static void AddSubAsset(UnityEngine.Object part, UnityEngine.Object owner)
        {
            if (part == null || AssetDatabase.Contains(part)) return;
            part.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(part, owner);
        }
    }
}

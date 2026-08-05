using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Иконки декора рендером самих префабов: PreviewRenderUtility снимает каждый замысел
    /// с фермерского ракурса в PNG, PNG импортируется спрайтом и назначается товару.
    /// <para>
    /// Рисованных иконок у декора нет и не предвидится — художника в проекте нет, а товар
    /// без картинки в витрине выглядит дыркой. Рендер префаба честнее заглушки: игрок
    /// видит ровно ту вещь, которую покупает. Инструмент повторяемый: PNG перерисовывает,
    /// назначение обновляет.
    /// </para>
    /// </summary>
    public static class OnlineDecorIcons
    {
        private const string IconFolder = "Assets/Game/UI/Icons";
        private const int IconSize = 256;

        [MenuItem("Farm/Онлайн/Отрисовать иконки декора")]
        public static void Render()
        {
            Directory.CreateDirectory(IconFolder);

            var report = new StringBuilder("[Онлайн] Иконки декора:\n");
            int done = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:ShopItemDefinition"))
            {
                var item = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || item.Kind != ShopItemKind.Prop) continue;
                if (item.Improvement == null || item.Prefab == null) continue;

                string pngPath = IconFolder + "/Decor_" + item.Improvement.Id + ".png";
                if (!RenderPrefab(item.Prefab, pngPath))
                {
                    report.Append("  ! ").Append(item.DisplayName).Append(": рендер не удался\n");
                    continue;
                }

                ImportAsSprite(pngPath);

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                if (sprite == null)
                {
                    report.Append("  ! ").Append(item.DisplayName).Append(": спрайт не импортировался\n");
                    continue;
                }

                var so = new SerializedObject(item);
                so.FindProperty("_icon").objectReferenceValue = sprite;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);

                done++;
                report.Append("  — ").Append(item.DisplayName).Append('\n');
            }

            AssetDatabase.SaveAssets();
            report.Append("Готово: ").Append(done).Append(" иконок.");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Снять префаб в PNG. PreviewRenderUtility, а не AssetPreview: тот греет кэш
        /// через delayCall, который из MCP не доживает до выполнения, — а здесь рендер
        /// синхронный и результат в руках сразу.
        /// </summary>
        private static bool RenderPrefab(GameObject prefab, string pngPath)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                preview.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.fieldOfView = 30f;
                preview.camera.nearClipPlane = 0.05f;
                preview.camera.farClipPlane = 100f;

                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                preview.ambientColor = new Color(0.35f, 0.35f, 0.38f);

                var instance = preview.InstantiatePrefabInScene(prefab);

                // Кадрируем по фактическим граням, а не по пивоту: у половины замыслов
                // пивот в углу, и без этого вещь уезжает из кадра.
                var bounds = BoundsOf(instance);
                float radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
                float distance = radius / Mathf.Tan(preview.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;

                // Фермерский ракурс: сверху-сбоку, как игрок и видит вещь на поле.
                var direction = new Vector3(1f, 0.8f, -1f).normalized;
                preview.camera.transform.position = bounds.center + direction * distance;
                preview.camera.transform.LookAt(bounds.center);

                preview.BeginStaticPreview(new Rect(0f, 0f, IconSize, IconSize));
                preview.Render();
                var texture = preview.EndStaticPreview();
                if (texture == null) return false;

                File.WriteAllBytes(pngPath, texture.EncodeToPNG());
                return true;
            }
            finally
            {
                preview.Cleanup();
            }
        }

        private static Bounds BoundsOf(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(instance.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void ImportAsSprite(string pngPath)
        {
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}

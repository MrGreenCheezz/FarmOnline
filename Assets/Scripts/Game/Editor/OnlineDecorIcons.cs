using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Иконки декора рендером самих префабов: каждый замысел снимается с фермерского ракурса
    /// в PNG, PNG импортируется спрайтом и назначается товару.
    /// <para>
    /// Рисованных иконок у декора нет и не предвидится — художника в проекте нет, а товар
    /// без картинки в витрине выглядит дыркой. Рендер префаба честнее заглушки: игрок
    /// видит ровно ту вещь, которую покупает. Инструмент повторяемый: PNG перерисовывает,
    /// назначение обновляет.
    /// </para>
    /// <para>
    /// <b>Почему не PreviewRenderUtility</b> (переписано 05.08.2026). Прошлая версия снимала
    /// префабы им — и все 18 иконок вышли чёрно-малиновыми: PreviewRenderUtility рисует
    /// встроенным пайплайном, а материалы моделей URP-овские, и на встроенном они падают
    /// в шейдер ошибки (чёрный + 255,0,255). Вдобавок EndStaticPreview отдаёт картинку
    /// с непрозрачным фоном. Здесь вместо него настоящая сцена-превью: в ней активен
    /// проектный URP, свои свет и камера, RenderTexture с альфой — то есть ровно тот
    /// рендер, которым игра рисует ферму.
    /// </para>
    /// </summary>
    public static class OnlineDecorIcons
    {
        private const string IconFolder = "Assets/Game/UI/Icons";

        /// <summary>Нетронутые снимки вне Assets — вход для Tools/icons.py, а не иконки.</summary>
        private const string SourceFolder = "Tools/icon-source";

        /// <summary>Размер снимка. 128 — тот же, что у всех фишек: витрина держит один вес.</summary>
        private const int SourceSize = 128;

        /// <summary>
        /// Снимаем вчетверо крупнее и ужимаем боксом. MSAA в RenderTexture URP на своём
        /// промежуточном буфере не гарантирует, а без сглаживания тонкий декор (жерди
        /// плетня, ножки скамьи) осыпается лесенкой; суперсэмплинг не зависит от настроек
        /// пайплайна вообще.
        /// </summary>
        private const int Supersample = 4;

        /// <summary>
        /// Подъём камеры над вещью (Y при X/Z = 1). Декор мелкий и лежачий — бочку и
        /// клумбу узнают сверху; постройка узнаётся фасадом, и с высокой точки от неё
        /// остаётся одна крыша: рынок превращался в синее пятно тента, хлев — в красный
        /// прямоугольник. Поэтому у построек камера ниже.
        /// </summary>
        private const float DecorLift = 0.8f;
        private const float BuildingLift = 0.42f;

        /// <summary>
        /// Снимки декора в <c>Tools/icon-source</c> и назначение готовых фишек товарам.
        /// <para>
        /// Почему рендер не пишет прямо в иконку: витрина показывает декор вперемешку с
        /// растениями, животными и постройками, а те — фишки с цветной плашкой. Голый
        /// рендер на прозрачном фоне рядом с ними читается как вещь из другой игры, и
        /// хуже всего это видно на вкладке построек, где стоят костёр и забор. Плашку
        /// кладёт <c>Tools/icons.py</c> — тот же код, что делает фишки всем остальным,
        /// поэтому семья остаётся одна по определению, а не по внимательности.
        /// </para>
        /// <para>
        /// <b>Иконка декора зависит от привязки</b> (06.08.2026). Восемь товаров декора
        /// из восемнадцати — это три предмета: три фонаря, две бочки и три клумбы
        /// ссылаются на один и тот же префаб, совпадая и по guid, и по fileID. Рендер
        /// тут бессилен — предмет один. Поэтому <c>Tools/icons.py</c> ставит в угол
        /// плашки значок постройки из <c>ImprovementDefinition._anchorBuilding</c>:
        /// «фонарь у хлева» и «шахтёрский фонарь» различаются тем же, чем различаются
        /// в игре. Следствие, о котором стоит знать: сменив привязку замысла, ты
        /// сменил и его иконку, а значок рисуется из <c>icon_&lt;постройка&gt;.png</c> —
        /// того самого снимка, который делает «Отрисовать исходники построек» ниже.
        /// </para>
        /// <para>
        /// Порядок: это → <c>python Tools/icons.py</c> → это ещё раз (назначить).
        /// Первый проход честно скажет, каким товарам фишки ещё нет.
        /// </para>
        /// </summary>
        [MenuItem("Farm/Онлайн/Отрисовать иконки декора")]
        public static void Render()
        {
            string sources = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceFolder));
            Directory.CreateDirectory(sources);
            Directory.CreateDirectory(IconFolder);

            var report = new StringBuilder("[Онлайн] Иконки декора:\n");
            var broken = new List<string>();
            var waiting = new List<string>();
            int done = 0;

            var items = new List<ShopItemDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:ShopItemDefinition"))
            {
                var item = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || item.Kind != ShopItemKind.Prop) continue;
                if (item.Improvement == null || item.Prefab == null) continue;
                items.Add(item);
            }

            // Порядок по имени замысла, а не по порядку файлов на диске: ракурс ниже считается
            // от места в группе, и переустановка проекта не должна перетасовывать иконки.
            items.Sort((a, b) => string.CompareOrdinal(a.Improvement.Id, b.Improvement.Id));
            var yaws = YawPerItem(items, report);

            foreach (var item in items)
            {
                string sourcePath = Path.Combine(sources, "decor_" + item.Improvement.Id + ".png");
                if (!RenderPrefab(item.Prefab, sourcePath, SourceSize, out string why,
                                  DecorLift, yaws[item]))
                {
                    report.Append("  ! ").Append(item.DisplayName).Append(": ").Append(why).Append('\n');
                    broken.Add(item.DisplayName);
                    continue;
                }

                string pngPath = IconFolder + "/Decor_" + item.Improvement.Id + ".png";
                if (!File.Exists(pngPath))
                {
                    waiting.Add(item.DisplayName);
                    continue;
                }

                ImportAsSprite(pngPath);

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                if (sprite == null)
                {
                    report.Append("  ! ").Append(item.DisplayName).Append(": спрайт не импортировался\n");
                    broken.Add(item.DisplayName);
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
            report.Append("Снимки в ").Append(SourceFolder).Append(", назначено: ").Append(done).Append('.');

            // Битая иконка обязана кричать, а не тихо лечь в папку: прошлый раз 18 чёрно-малиновых
            // квадратов доехали до магазина именно потому, что рендер отчитался об успехе.
            if (waiting.Count > 0)
                report.Append(" Ждут фишки (запусти python Tools/icons.py и повтори): ")
                      .Append(string.Join(", ", waiting)).Append('.');

            if (broken.Count > 0)
            {
                report.Append(" НЕ УДАЛОСЬ: ").Append(broken.Count).Append(" — ")
                      .Append(string.Join(", ", broken));
                Debug.LogError(report.ToString());
            }
            else if (waiting.Count > 0) Debug.LogWarning(report.ToString());
            else Debug.Log(report.ToString());
        }

        /// <summary>
        /// Поворот камеры вокруг вещи для каждого товара. У товара со своим префабом — ноль:
        /// весь набор снят одним фермерским ракурсом, и разнобой ради разнобоя тут никому не нужен.
        /// <para>
        /// Поворот появляется только там, где ОДИН префаб продаётся несколькими товарами
        /// (три фонаря, две бочки — совпадают и guid, и fileID, снимки выходили байт в байт).
        /// Значок постройки-хозяйки в углу плашки различает их логически, но витрина всё равно
        /// показывала одну и ту же картинку три раза. Поворот честен: это та же вещь с другой
        /// стороны, ничего не выдумано — в отличие от подкраски, которая соврала бы, что
        /// предметы разные.
        /// </para>
        /// <para>
        /// Шаг 50°, а не 120°: на 120° половина вещей уходит затылком к камере (у скамьи и
        /// указателя есть перёд), а 50° хватает, чтобы силуэт заметно поехал. Первый в группе
        /// остаётся на нуле — у товара, который был один, иконка не меняется без причины.
        /// </para>
        /// </summary>
        private static Dictionary<ShopItemDefinition, float> YawPerItem(
            List<ShopItemDefinition> items, StringBuilder report)
        {
            const float YawStep = 50f;

            var byPrefab = new Dictionary<GameObject, List<ShopItemDefinition>>();
            foreach (var item in items)
            {
                if (!byPrefab.TryGetValue(item.Prefab, out var list))
                    byPrefab[item.Prefab] = list = new List<ShopItemDefinition>();
                list.Add(item);
            }

            var yaws = new Dictionary<ShopItemDefinition, float>();
            foreach (var pair in byPrefab)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                    yaws[pair.Value[i]] = pair.Value.Count > 1 ? i * YawStep : 0f;

                if (pair.Value.Count > 1)
                {
                    report.Append("  общий префаб у ").Append(pair.Value.Count).Append(" товаров, развожу ракурсом: ");
                    for (int i = 0; i < pair.Value.Count; i++)
                        report.Append(i > 0 ? ", " : "").Append(pair.Value[i].Improvement.Id)
                              .Append(' ').Append(i * YawStep).Append('°');
                    report.Append('\n');
                }
            }
            return yaws;
        }

        /// <summary>
        /// Исходники построек тем же ракурсом, что и декор, — в <c>Tools/icon-source</c>,
        /// откуда их забирает <c>Tools/icons.py</c> и подкладывает под фишку.
        /// <para>
        /// Зачем: до этого постройки жили превьюшками редактора, снятыми почти сверху.
        /// Рынок с такого ракурса — синее пятно тента, силос и плавильня — два одинаковых
        /// цилиндра. Фермерский ракурс сверху-сбоку показывает ту же вещь так, как игрок
        /// видит её на ферме, и постройки перестают путаться между собой.
        /// </para>
        /// <para>
        /// Порядок: сначала это, потом <c>python Tools/icons.py</c> — иконка собирается
        /// из исходника, а не из себя самой.
        /// </para>
        /// </summary>
        [MenuItem("Farm/Онлайн/Отрисовать исходники построек")]
        public static void RenderBuildingSources()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceFolder));
            Directory.CreateDirectory(folder);

            var report = new StringBuilder("[Онлайн] Исходники построек:\n");
            var broken = new List<string>();
            int done = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:BuildingDefinition"))
            {
                var building = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (building == null || building.Prefab == null) continue;

                string pngPath = Path.Combine(folder, "icon_" + building.Id + ".png");
                if (!RenderPrefab(building.Prefab, pngPath, SourceSize, out string why, BuildingLift))
                {
                    report.Append("  ! ").Append(building.DisplayName).Append(": ").Append(why).Append('\n');
                    broken.Add(building.DisplayName);
                    continue;
                }

                done++;
                report.Append("  — ").Append(building.DisplayName).Append('\n');
            }

            report.Append("Готово: ").Append(done).Append(" исходников. Теперь python Tools/icons.py");

            if (broken.Count > 0)
            {
                report.Append(" НЕ УДАЛОСЬ: ").Append(broken.Count).Append(" — ")
                      .Append(string.Join(", ", broken));
                Debug.LogError(report.ToString());
            }
            else Debug.Log(report.ToString());
        }

        /// <summary>
        /// Снять префаб в PNG через сцену-превью и обычную камеру: так рендерит проектный
        /// URP, а не встроенный пайплайн. Возвращает false с причиной — молчаливого отказа
        /// у этого инструмента быть не должно.
        /// </summary>
        private static bool RenderPrefab(GameObject prefab, string pngPath, int size, out string why,
                                         float lift = DecorLift, float yaw = 0f)
        {
            int shot = size * Supersample;

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject camGo = null, keyGo = null, fillGo = null, rimGo = null;
            RenderTexture target = null;
            var previous = RenderTexture.active;

            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                if (instance == null) { why = "префаб не создался в сцене-превью"; return false; }
                instance.transform.position = Vector3.zero;

                camGo = MakeSceneObject("Камера превью", scene, typeof(Camera));
                var camera = camGo.GetComponent<Camera>();
                camera.scene = scene;              // culling только по сцене-превью
                camera.enabled = false;            // рисуем вручную, а не редакторным циклом
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.fieldOfView = 30f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 100f;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.cullingMask = ~0;

                // Кадрируем по фактическим граням, а не по пивоту: у половины замыслов
                // пивот в углу, и без этого вещь уезжает из кадра.
                var bounds = BoundsOf(instance);
                float radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
                float distance = radius / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;

                // Фермерский ракурс: сверху-сбоку, как игрок и видит вещь на поле.
                // Поворот вокруг вертикали — только у товаров, делящих один префаб (см. YawPerItem);
                // подъём при этом не трогаем, иначе разъехались бы ещё и высоты кадра.
                var direction = Quaternion.Euler(0f, yaw, 0f) * new Vector3(1f, lift, -1f).normalized;
                camera.transform.position = bounds.center + direction * distance;
                camera.transform.LookAt(bounds.center);

                // Три источника вместо ambient: RenderSettings принадлежат активной сцене,
                // а не превью, и рассчитывать на них нельзя — тень уйдёт в чёрный провал.
                keyGo = MakeLight("Ключевой", scene, new Vector3(40f, -30f, 0f), 1.5f, new Color(1f, 0.97f, 0.9f));
                fillGo = MakeLight("Заполняющий", scene, new Vector3(15f, 150f, 0f), 0.7f, new Color(0.72f, 0.78f, 0.9f));
                rimGo = MakeLight("Контровой", scene, new Vector3(-35f, 60f, 0f), 0.45f, new Color(0.85f, 0.9f, 1f));

                var descriptor = new RenderTextureDescriptor(shot, shot, GraphicsFormat.R8G8B8A8_SRGB, 24)
                {
                    msaaSamples = 1,
                    sRGB = true
                };
                target = new RenderTexture(descriptor) { filterMode = FilterMode.Bilinear };
                target.Create();

                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;

                RenderTexture.active = target;
                var full = new Texture2D(shot, shot, TextureFormat.RGBA32, false, false);
                full.ReadPixels(new Rect(0f, 0f, shot, shot), 0, 0);
                full.Apply();
                RenderTexture.active = previous;

                var icon = Downsample(full, size, Supersample);
                Object.DestroyImmediate(full);

                if (!LooksRendered(icon, out why)) { Object.DestroyImmediate(icon); return false; }

                File.WriteAllBytes(pngPath, icon.EncodeToPNG());
                Object.DestroyImmediate(icon);
                return true;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (keyGo != null) Object.DestroyImmediate(keyGo);
                if (fillGo != null) Object.DestroyImmediate(fillGo);
                if (rimGo != null) Object.DestroyImmediate(rimGo);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static GameObject MakeSceneObject(string name, Scene scene, params System.Type[] components)
        {
            // CreateGameObjectWithHideFlags, а не new GameObject: тот пометил бы открытую
            // сцену игрока грязной из-за нашей временной камеры.
            var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static GameObject MakeLight(string name, Scene scene, Vector3 euler, float intensity, Color color)
        {
            var go = MakeSceneObject(name, scene, typeof(Light));
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;   // тень на прозрачном фоне падать некуда
            go.transform.rotation = Quaternion.Euler(euler);
            return go;
        }

        /// <summary>
        /// Бокс-фильтр со взвешиванием по альфе. Усреднять цвет вместе с прозрачными
        /// пикселями нельзя: фон у нас чёрный, и по кромке силуэта появилась бы тёмная кайма.
        /// </summary>
        private static Texture2D Downsample(Texture2D source, int size, int factor)
        {
            var src = source.GetPixels32();
            int wide = source.width;
            var dst = new Color32[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int sy = 0; sy < factor; sy++)
                for (int sx = 0; sx < factor; sx++)
                {
                    var p = src[(y * factor + sy) * wide + (x * factor + sx)];
                    float w = p.a / 255f;
                    r += p.r * w; g += p.g * w; b += p.b * w; a += w;
                }

                int n = factor * factor;
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(a / n) * 255f);
                if (a < 0.0001f) dst[y * size + x] = new Color32(0, 0, 0, 0);
                else dst[y * size + x] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(r / a), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(g / a), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(b / a), 0, 255),
                    alpha);
            }

            var result = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            result.SetPixels32(dst);
            result.Apply();
            return result;
        }

        /// <summary>
        /// Отличить картинку от аварии. Шейдер ошибки даёт ровно два цвета — чёрный и
        /// малиновый; пустой кадр не даёт ни одного непрозрачного пикселя. И то и другое
        /// доезжало до магазина, поэтому проверка стоит до записи файла, а не после.
        /// </summary>
        private static bool LooksRendered(Texture2D icon, out string why)
        {
            var pixels = icon.GetPixels32();
            var unique = new HashSet<int>();
            int opaque = 0, magenta = 0;

            foreach (var p in pixels)
            {
                if (p.a < 8) continue;
                opaque++;
                if (p.r > 200 && p.g < 60 && p.b > 200) magenta++;
                unique.Add((p.r >> 3) << 10 | (p.g >> 3) << 5 | (p.b >> 3));
            }

            if (opaque < pixels.Length / 100) { why = "кадр пустой — вещь не попала в объектив"; return false; }
            if (magenta > opaque / 20) { why = "малиновый шейдер ошибки — материалы не того пайплайна"; return false; }
            if (unique.Count < 8) { why = "в кадре " + unique.Count + " цветов — это не картинка, а заливка"; return false; }

            why = null;
            return true;
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

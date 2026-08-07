using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Продолжение коротких линий: живность до 12-й ступени и посевы до 12-й.
    /// <para>
    /// Дерево и руда давно доходят до t12, а живность обрывалась на олене (t5), посевы —
    /// на златоцвете (t6). Обе к тому же были «вершинами» лестницы слияния: две двадцатки
    /// оленя перерождаться уже некуда, и половина фермы упиралась в потолок вдвое ниже
    /// другой половины (решение владельца 07.08.2026).
    /// </para>
    /// <para>
    /// Инструмент, а не разовый скрипт, по тем же причинам, что и
    /// <see cref="OnlineTierContent"/>: вся арифметика из <see cref="TierEconomy"/>, флаги —
    /// клонированием живого ассета верхней ступени своей линии. Повторный запуск дозаполняет
    /// недостающее и не трогает готовое.
    /// </para>
    /// <para>
    /// Порядок целиком: это → <c>python Tools/icons.py</c> → это ещё раз (иконки назначатся)
    /// → Farm → Онлайн → Заполнить лестницу слияния → Экспортировать каталог для сервера.
    /// </para>
    /// </summary>
    public static class OnlineTierLadder
    {
        private const string Farming = "Assets/Game/Farming";
        private const string Pets = "Assets/ExternalAssets/Kenney/CubePets/Models";
        private const string Nature = "Assets/ExternalAssets/Kenney/NatureKit/Models";
        private const string SourceFolder = "Tools/icon-source";

        /// <summary>
        /// База цены грядки. Выводится из живых ассетов: златоцвет (t6) стоит 27684 = 15 × 4.5⁵,
        /// и посевы просто продолжают свою кривую.
        /// <para>
        /// У живности база исторически вдвое выше (олень t5 = 24604 = 60 × 4.5⁴), и продолжить
        /// её значило бы к двенадцатой ступени просить 919 млн против 383 млн у дерева —
        /// <b>за одинаковый доход</b>: цена продажи зависит только от ступени, а не от линии.
        /// Поэтому наверху живность приравнена к дереву (25). Стык выходит мягким: 24604 → 46132
        /// вместо обычного ×4.5, и вход в новую половину лестницы не отвесный.
        /// </para>
        /// </summary>
        private const int LiveGoldBase = 25;
        private const int CropGoldBase = 15;

        private sealed class Spec
        {
            public int Tier;
            public bool Live;             // живность или посев
            public string PlotId;         // id растимого: у живности зверь, у посева = ResourceId
            public string ResourceId;     // id того, что снимается
            public string PlotName;
            public string ResourceName;
            public string Model;          // fbx без расширения
            public Color32 Tint;          // только посевы: цвет листвы
            public float Size;            // наибольший габарит в метрах, см. Normalize
            public string PrevResource;   // ресурс предыдущей ступени своей линии
        }

        // Живность. Модели — Kenney CubePets, они уже цветные, и перекрашивать их нечем
        // и незачем: зверь узнаётся силуэтом. Имена продолжают язык линий — обычные звери
        // внизу, небесный ряд наверху, как у деревьев (лунное, солнечное, грозовое, мировое).
        private static readonly Spec[] Specs =
        {
            new Spec { Tier = 6,  Live = true, PlotId = "fox",      ResourceId = "firefur",
                       PlotName = "Огнёвка", ResourceName = "Огненный мех",
                       Model = "animal-fox", Size = 0.9f, PrevResource = "antler" },
            new Spec { Tier = 7,  Live = true, PlotId = "parrot",   ResourceId = "plume",
                       PlotName = "Райская птица", ResourceName = "Радужное перо",
                       Model = "animal-parrot", Size = 0.7f, PrevResource = "firefur" },
            new Spec { Tier = 8,  Live = true, PlotId = "tiger",    ResourceId = "moonfang",
                       PlotName = "Лунный тигр", ResourceName = "Лунный клык",
                       Model = "animal-tiger", Size = 1.3f, PrevResource = "plume" },
            new Spec { Tier = 9,  Live = true, PlotId = "giraffe",  ResourceId = "skyhorn",
                       PlotName = "Небесный жираф", ResourceName = "Небесный рог",
                       Model = "animal-giraffe", Size = 1.7f, PrevResource = "moonfang" },
            new Spec { Tier = 10, Live = true, PlotId = "lion",     ResourceId = "sunmane",
                       PlotName = "Солнечный лев", ResourceName = "Солнечная грива",
                       Model = "animal-lion", Size = 1.4f, PrevResource = "skyhorn" },
            new Spec { Tier = 11, Live = true, PlotId = "elephant", ResourceId = "titantusk",
                       PlotName = "Исполин", ResourceName = "Бивень исполина",
                       Model = "animal-elephant", Size = 1.9f, PrevResource = "sunmane" },
            new Spec { Tier = 12, Live = true, PlotId = "polar",    ResourceId = "starclaw",
                       PlotName = "Звёздный медведь", ResourceName = "Звёздный коготь",
                       // Крупнее слона: вершина линии обязана и выглядеть вершиной —
                       // размер на ферме читается раньше цены.
                       Model = "animal-polar", Size = 2.1f, PrevResource = "titantusk" },

            // Посевы. Моделей растений в наборе мало, поэтому линию держит цвет — тот же
            // приём, которым живут двенадцать деревьев на четырёх моделях.
            new Spec { Tier = 7,  PlotId = "moonroot",  ResourceId = "moonroot",
                       PlotName = "Лунный корень", ResourceName = "Лунный корень",
                       Model = "crop_turnip", Size = 0.6f, Tint = new Color32(205, 220, 240, 255),
                       PrevResource = "goldbloom" },
            new Spec { Tier = 8,  PlotId = "sunroot",   ResourceId = "sunroot",
                       PlotName = "Солнечный корень", ResourceName = "Солнечный корень",
                       Model = "crop_carrot", Size = 0.65f, Tint = new Color32(245, 190, 85, 255),
                       PrevResource = "moonroot" },
            new Spec { Tier = 9,  PlotId = "spiritherb", ResourceId = "spiritherb",
                       PlotName = "Духова трава", ResourceName = "Духова трава",
                       Model = "crops_leafsStageB", Size = 0.7f, Tint = new Color32(180, 240, 210, 255),
                       PrevResource = "sunroot" },
            new Spec { Tier = 10, PlotId = "stormberry", ResourceId = "stormberry",
                       PlotName = "Грозовая ягода", ResourceName = "Грозовая ягода",
                       Model = "crop_melon", Size = 0.75f, Tint = new Color32(135, 115, 185, 255),
                       PrevResource = "spiritherb" },
            new Spec { Tier = 11, PlotId = "worldfruit", ResourceId = "worldfruit",
                       PlotName = "Мировой плод", ResourceName = "Мировой плод",
                       Model = "crop_pumpkin", Size = 0.8f, Tint = new Color32(225, 245, 225, 255),
                       PrevResource = "stormberry" },
            new Spec { Tier = 12, PlotId = "starstalk", ResourceId = "starstalk",
                       PlotName = "Звёздный стебель", ResourceName = "Звёздный стебель",
                       Model = "crops_bambooStageB", Size = 0.95f, Tint = new Color32(198, 150, 250, 255),
                       PrevResource = "worldfruit" },
        };

        [MenuItem("Farm/Онлайн/Досоздать живность и посевы до 12")]
        public static void Build()
        {
            var report = new StringBuilder("[Онлайн] Живность и посевы до t12:\n");
            var broken = new List<string>();
            var waitingIcons = new List<string>();

            string sources = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceFolder));
            Directory.CreateDirectory(sources);
            EnsureFolder("Assets/Game/Prefabs/Animals");
            EnsureFolder("Assets/Game/Prefabs/Plants");

            foreach (var spec in Specs)
            {
                try
                {
                    BuildOne(spec, sources, report, waitingIcons);
                }
                catch (System.Exception e)
                {
                    broken.Add(spec.PlotId + ": " + e.Message);
                    Debug.LogException(e);
                }
            }

            RegisterInShopCatalog(report);

            var registry = ContentRegistry.Instance;
            if (registry != null) { registry.Rebuild(); EditorUtility.SetDirty(registry); }

            AssetDatabase.SaveAssets();

            if (waitingIcons.Count > 0)
                report.Append("Ждут фишек (python Tools/icons.py, затем запустить это снова): ")
                      .Append(string.Join(", ", waitingIcons)).Append('\n');

            if (broken.Count > 0)
            {
                report.Append("НЕ УДАЛОСЬ: ").Append(string.Join("; ", broken));
                Debug.LogError(report.ToString());
            }
            else if (waitingIcons.Count > 0) Debug.LogWarning(report.ToString());
            else Debug.Log(report.ToString());
        }

        private static void BuildOne(Spec spec, string sources, StringBuilder report, List<string> waitingIcons)
        {
            var prefab = spec.Live ? AnimalPrefab(spec) : PlantPrefab(spec);

            // --- ресурс ---
            var resource = CloneAsset<ResourceDefinition>(
                Farming + (spec.Live ? "/Resource_antler.asset" : "/Resource_goldbloom.asset"),
                Farming + "/Resource_" + spec.ResourceId + ".asset");

            using (var so = Edit(resource))
            {
                so.FindProperty("_id").stringValue = spec.ResourceId;
                so.FindProperty("_displayName").stringValue = spec.ResourceName;
                so.FindProperty("_tier").intValue = spec.Tier;
                so.FindProperty("_sellPrice").intValue = TierEconomy.SellPrice(spec.Tier);
                so.FindProperty("_icon").objectReferenceValue = IconSprite(spec.ResourceId);
            }

            // --- растимое ---
            var growable = CloneAsset<GrowableDefinition>(
                Farming + (spec.Live ? "/Growable_deer.asset" : "/Growable_goldbloom.asset"),
                Farming + "/Growable_" + spec.PlotId + ".asset");

            float grow = TierEconomy.OnlineGrowSeconds(spec.Tier);
            using (var so = Edit(growable))
            {
                so.FindProperty("_id").stringValue = spec.PlotId;
                so.FindProperty("_displayName").stringValue = spec.PlotName;
                so.FindProperty("_defaultStageDuration").floatValue = grow;
                so.FindProperty("_yieldResource").objectReferenceValue = resource;

                // Вершина линии: перерождаться некуда, пока не появится следующая ступень.
                // Заполнит «Лестницу слияния» отдельным инструментом — она видит всю линию.
                so.FindProperty("_mergeNext").objectReferenceValue = null;

                var stages = so.FindProperty("_stages");
                for (int i = 0; i < stages.arraySize; i++)
                {
                    var stage = stages.GetArrayElementAtIndex(i);
                    stage.FindPropertyRelative("_visual").objectReferenceValue = prefab;
                    if (stage.FindPropertyRelative("_duration").floatValue > 0f)
                        stage.FindPropertyRelative("_duration").floatValue = grow;
                }
            }

            // --- товар ---
            var item = CloneAsset<ShopItemDefinition>(
                Farming + (spec.Live ? "/Shop_deer.asset" : "/Shop_goldbloom.asset"),
                Farming + "/Shop_" + spec.PlotId + ".asset");

            var prev = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(
                Farming + "/Resource_" + spec.PrevResource + ".asset");
            if (prev == null) throw new System.Exception("нет ресурса предыдущей ступени " + spec.PrevResource);

            int gold = TierEconomy.PlotPrice(spec.Tier, spec.Live ? LiveGoldBase : CropGoldBase);
            int hours = Mathf.RoundToInt(grow / 3600f);

            using (var so = Edit(item))
            {
                so.FindProperty("_id").stringValue = spec.PlotId;
                so.FindProperty("_displayName").stringValue = spec.PlotName;
                so.FindProperty("_description").stringValue =
                    "Ступень " + spec.Tier + ". " + (spec.Live ? "Даёт " : "Растёт ") +
                    (spec.Live ? spec.ResourceName.ToLowerInvariant() + " раз в " + hours + " ч"
                               : hours + " ч") +
                    ", продаётся по " + TierEconomy.SellPrice(spec.Tier) + " зол.";
                so.FindProperty("_icon").objectReferenceValue = IconSprite(spec.ResourceId);
                so.FindProperty("_growable").objectReferenceValue = growable;

                var price = so.FindProperty("_price");
                price.FindPropertyRelative("Gold").intValue = gold;

                var resources = price.FindPropertyRelative("Resources");
                resources.arraySize = 1;
                var entry = resources.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("Resource").objectReferenceValue = prev;
                entry.FindPropertyRelative("Amount").intValue = TierEconomy.LadderMaterials(spec.Tier);
            }

            // --- исходник иконки ---
            string sourcePath = Path.Combine(sources, "icon_" + spec.ResourceId + ".png");
            if (!File.Exists(sourcePath))
            {
                if (!OnlineDecorIcons.RenderPrefab(prefab, sourcePath, 128, out string why))
                    throw new System.Exception("иконка не снялась: " + why);
            }

            if (IconSprite(spec.ResourceId) == null) waitingIcons.Add(spec.ResourceId);

            report.Append("  — t").Append(spec.Tier).Append(' ').Append(spec.PlotName)
                  .Append(" → ").Append(spec.ResourceName)
                  .Append(", продажа ").Append(TierEconomy.SellPrice(spec.Tier))
                  .Append(", грядка ").Append(gold).Append(" зол. + ")
                  .Append(TierEconomy.LadderMaterials(spec.Tier)).Append(" × ")
                  .Append(spec.PrevResource).Append('\n');
        }

        // ---- префабы ----

        /// <summary>
        /// Зверь как есть: модели CubePets уже раскрашены своей текстурой, и красить их
        /// однотонной заливкой значило бы стереть единственное, чем зверь узнаётся.
        /// </summary>
        private static GameObject AnimalPrefab(Spec spec)
        {
            string path = "Assets/Game/Prefabs/Animals/P_" + spec.PlotId + ".prefab";
            return BakePrefab(Pets + "/" + spec.Model + ".fbx", path, "P_" + spec.PlotId, null, spec.Size);
        }

        /// <summary>
        /// Растение красится в цвет ступени тем же шейдером, что и деревья: у посевов моделей
        /// мало, и линию наверх держит цвет — иначе четыре верхние ступени были бы одной морковкой.
        /// </summary>
        private static GameObject PlantPrefab(Spec spec)
        {
            string path = "Assets/Game/Prefabs/Plants/P_" + spec.PlotId + ".prefab";
            var leaf = VegetationMaterial("M_" + Cap(spec.PlotId), spec.Tint);

            return BakePrefab(Nature + "/" + spec.Model + ".fbx", path, "P_" + spec.PlotId, renderer =>
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = leaf;
                renderer.sharedMaterials = mats;
            }, spec.Size);
        }

        /// <summary>
        /// Собрать префаб из модели, при нужде перекрасив, и <b>привести к своему размеру</b>.
        /// <para>
        /// Нормализация обязательна: модели набора идут каждая в своём масштабе, и без неё
        /// белый медведь двенадцатой ступени вышел мельче лисы шестой (замер: 0.48 м против
        /// 0.74 м), а слон — мельче оленя. Размер на ферме читается как ценность, и обратный
        /// порядок игрок прочтёт как поломку, ещё не открыв цену.
        /// </para>
        /// <para>
        /// Префаб пересобирается на каждом прогоне, а не пропускается готовым: правка размера
        /// в спеке иначе не доехала бы до фермы, и инструмент перестал бы быть источником правды.
        /// </para>
        /// </summary>
        private static GameObject BakePrefab(string modelPath, string path, string name,
                                             System.Action<Renderer> recolor, float targetSize)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new System.Exception("нет модели " + modelPath);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = name;

            if (recolor != null)
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    recolor(renderer);

            Normalize(instance, targetSize);

            var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return saved;
        }

        /// <summary>Подогнать наибольший габарит объекта под <paramref name="target"/> метров.</summary>
        private static void Normalize(GameObject instance, float target)
        {
            if (target <= 0f) return;

            bool any = false;
            var bounds = new Bounds();

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!any) return;

            float biggest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (biggest <= 0.0001f) return;

            instance.transform.localScale *= target / biggest;
        }

        private static Material VegetationMaterial(string name, Color32 color)
        {
            string path = "Assets/Game/Materials/Vegetation/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Farm/Vegetation"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.06f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_WindMaskHeight", 0.9f);
            mat.SetFloat("_WindSpeed", 0.8f);
            mat.SetFloat("_WindStrength", 0.07f);
            mat.SetFloat("_WindTurbulence", 0.14f);
            mat.SetFloat("_WindPhaseScale", 1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---- каталог, реестр, мелочи ----

        private static void RegisterInShopCatalog(StringBuilder report)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(Farming + "/ShopCatalog.asset");
            if (catalog == null) { report.Append("  ! ShopCatalog.asset не найден\n"); return; }

            var so = new SerializedObject(catalog);
            var items = so.FindProperty("_items");

            var present = new HashSet<Object>();
            for (int i = 0; i < items.arraySize; i++)
                present.Add(items.GetArrayElementAtIndex(i).objectReferenceValue);

            int added = 0;
            foreach (var spec in Specs)
            {
                var item = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(
                    Farming + "/Shop_" + spec.PlotId + ".asset");
                if (item == null || present.Contains(item)) continue;

                items.arraySize++;
                items.GetArrayElementAtIndex(items.arraySize - 1).objectReferenceValue = item;
                added++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            if (added > 0) report.Append("  в каталог магазина добавлено товаров: ").Append(added).Append('\n');
        }

        private static T CloneAsset<T>(string templatePath, string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var template = AssetDatabase.LoadAssetAtPath<T>(templatePath);
            if (template == null) throw new System.Exception("нет шаблона " + templatePath);

            // Клон живого ассета верхней ступени, а не CreateInstance: флаги линии
            // (отрастание, стадия отрастания, урожай за уровень) приезжают вместе с ним
            // и не могут разойтись с остальной линией.
            var clone = Object.Instantiate(template);
            clone.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(clone, path);
            return clone;
        }

        private sealed class Scope : System.IDisposable
        {
            private readonly SerializedObject _so;
            public Scope(SerializedObject so) { _so = so; }
            public SerializedProperty FindProperty(string name) => _so.FindProperty(name);
            public void Dispose()
            {
                _so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(_so.targetObject);
            }
        }

        private static Scope Edit(Object asset) => new Scope(new SerializedObject(asset));

        private static Sprite IconSprite(string resourceId)
        {
            string path = "Assets/Game/UI/Icons/icon_" + resourceId + ".png";

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            // Свежая фишка импортируется текстурой, а не спрайтом, — дожимаем импорт сами.
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string Cap(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}

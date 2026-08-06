using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Ступени 9–12: продолжение линий дерева и руды теми же формулами, что и низ лестницы.
    /// <para>
    /// Инструмент, а не разовый скрипт, по правилу «контент не должен трогать системы»:
    /// вся арифметика берётся из <see cref="TierEconomy"/>, флаги — клонированием ассетов
    /// восьмой ступени своей линии, поэтому смена кривой или флага в шаблоне подхватится
    /// перезапуском, а не ручной правкой восьми файлов. Повторный запуск дозаполняет
    /// недостающее и не трогает готовое.
    /// </para>
    /// <para>
    /// Порядок целиком: это → <c>python Tools/icons.py</c> → это ещё раз (иконки
    /// назначатся) → Farm → Онлайн → Экспортировать каталог для сервера.
    /// </para>
    /// </summary>
    public static class OnlineTierContent
    {
        private const string Farming = "Assets/Game/Farming";
        private const string Models = "Assets/ExternalAssets/Kenney/NatureKit/Models";
        private const string SourceFolder = "Tools/icon-source";

        /// <summary>База цены грядки в золоте — та же, что у нижних рунг линии (HOWTO, «Лестница»).</summary>
        private const int TreeGoldBase = 25;
        private const int VeinGoldBase = 20;

        private sealed class Spec
        {
            public int Tier;
            public bool Tree;
            public string Id;            // id ресурса; growable/shop получают префикс линии
            public string Name;
            public string Model;
            public Color32 Leaf;         // у руды — цвет камня
            public Color32 Bark;
            public float Smooth;         // только руда
            public string PrevResource;  // id ресурса предыдущей ступени своей линии
        }

        private static readonly Spec[] Specs =
        {
            // Дерево. Цвета продолжают язык линии: восьмёрка кончилась «древом духов»
            // (светлая крона), дальше — небесный ряд: луна, солнце, гроза, мир.
            new Spec { Tier = 9,  Tree = true, Id = "moonwood",  Name = "Лунное древо",
                       Model = "tree_tall", Leaf = new Color32(205, 220, 240, 255),
                       Bark = new Color32(95, 105, 130, 255), PrevResource = "spiritwood" },
            new Spec { Tier = 10, Tree = true, Id = "sunwood",   Name = "Солнечное древо",
                       Model = "tree_plateau", Leaf = new Color32(245, 205, 95, 255),
                       Bark = new Color32(150, 100, 55, 255), PrevResource = "moonwood" },
            new Spec { Tier = 11, Tree = true, Id = "stormwood", Name = "Грозовое древо",
                       Model = "tree_thin", Leaf = new Color32(135, 115, 185, 255),
                       Bark = new Color32(70, 70, 88, 255), PrevResource = "sunwood" },
            new Spec { Tier = 12, Tree = true, Id = "worldtree", Name = "Древо мира",
                       Model = "tree_detailed", Leaf = new Color32(225, 245, 225, 255),
                       Bark = new Color32(185, 165, 125, 255), PrevResource = "stormwood" },

            // Руда. Жила — камень, целиком окрашенный в цвет металла (образец — P_gold).
            new Spec { Tier = 9,  Tree = false, Id = "orichalcum", Name = "Орихалк",
                       Model = "rock_largeE", Leaf = new Color32(70, 190, 160, 255), Smooth = 0.62f,
                       PrevResource = "starmetal" },
            new Spec { Tier = 10, Tree = false, Id = "moonstone", Name = "Лунный камень",
                       Model = "rock_tallB", Leaf = new Color32(208, 222, 240, 255), Smooth = 0.78f,
                       PrevResource = "orichalcum" },
            new Spec { Tier = 11, Tree = false, Id = "dragonite", Name = "Драконит",
                       Model = "rock_largeD", Leaf = new Color32(185, 66, 52, 255), Smooth = 0.55f,
                       PrevResource = "moonstone" },
            new Spec { Tier = 12, Tree = false, Id = "starheart", Name = "Сердце звезды",
                       Model = "rock_largeF", Leaf = new Color32(198, 150, 250, 255), Smooth = 0.85f,
                       PrevResource = "dragonite" },
        };

        [MenuItem("Farm/Онлайн/Досоздать ступени 9–12")]
        public static void Build()
        {
            var report = new StringBuilder("[Онлайн] Ступени 9–12:\n");
            var broken = new List<string>();
            var waitingIcons = new List<string>();

            string sources = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceFolder));
            Directory.CreateDirectory(sources);
            EnsureFolder("Assets/Game/Prefabs/Trees");

            foreach (var spec in Specs)
            {
                try
                {
                    BuildOne(spec, sources, report, waitingIcons);
                }
                catch (System.Exception e)
                {
                    broken.Add(spec.Id + ": " + e.Message);
                    Debug.LogException(e);
                }
            }

            // Каталог магазина и реестр контента — один раз в конце: они общие.
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
            string line = spec.Tree ? "tree_" : "vein_";
            string growableId = line + spec.Id;

            // --- материалы и префаб ---
            var prefab = spec.Tree ? TreePrefab(spec) : VeinPrefab(spec);

            // --- ресурс ---
            var resource = CloneAsset<ResourceDefinition>(
                Farming + (spec.Tree ? "/Resource_spiritwood.asset" : "/Resource_starmetal.asset"),
                Farming + "/Resource_" + spec.Id + ".asset");

            using (var so = Edit(resource))
            {
                so.FindProperty("_id").stringValue = spec.Id;
                so.FindProperty("_displayName").stringValue = spec.Name;
                so.FindProperty("_tier").intValue = spec.Tier;
                so.FindProperty("_sellPrice").intValue = TierEconomy.SellPrice(spec.Tier);
                so.FindProperty("_icon").objectReferenceValue = IconSprite(spec.Id);
            }

            // --- растимое ---
            var growable = CloneAsset<GrowableDefinition>(
                Farming + (spec.Tree ? "/Growable_tree_spiritwood.asset" : "/Growable_vein_starmetal.asset"),
                Farming + "/Growable_" + growableId + ".asset");

            float grow = TierEconomy.OnlineGrowSeconds(spec.Tier);
            using (var so = Edit(growable))
            {
                so.FindProperty("_id").stringValue = growableId;
                so.FindProperty("_displayName").stringValue = spec.Name;
                so.FindProperty("_defaultStageDuration").floatValue = grow;
                so.FindProperty("_yieldResource").objectReferenceValue = resource;

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
                Farming + (spec.Tree ? "/Shop_tree_spiritwood.asset" : "/Shop_vein_starmetal.asset"),
                Farming + "/Shop_" + growableId + ".asset");

            var prev = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(
                Farming + "/Resource_" + spec.PrevResource + ".asset");
            if (prev == null) throw new System.Exception("нет ресурса предыдущей ступени " + spec.PrevResource);

            int hours = Mathf.RoundToInt(grow / 3600f);
            using (var so = Edit(item))
            {
                so.FindProperty("_id").stringValue = growableId;
                so.FindProperty("_displayName").stringValue = spec.Name;
                so.FindProperty("_description").stringValue =
                    "Ступень " + spec.Tier + ". Растёт " + hours + " ч, продаётся по "
                    + TierEconomy.SellPrice(spec.Tier) + " зол.";
                so.FindProperty("_icon").objectReferenceValue = IconSprite(spec.Id);
                so.FindProperty("_growable").objectReferenceValue = growable;

                var price = so.FindProperty("_price");
                price.FindPropertyRelative("Gold").intValue =
                    TierEconomy.PlotPrice(spec.Tier, spec.Tree ? TreeGoldBase : VeinGoldBase);

                var resources = price.FindPropertyRelative("Resources");
                resources.arraySize = 1;
                var entry = resources.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("Resource").objectReferenceValue = prev;
                entry.FindPropertyRelative("Amount").intValue = TierEconomy.LadderMaterials(spec.Tier);
            }

            // --- исходник иконки ---
            string sourcePath = Path.Combine(sources, "icon_" + spec.Id + ".png");
            if (!File.Exists(sourcePath))
            {
                if (!OnlineDecorIcons.RenderPrefab(prefab, sourcePath, 128, out string why))
                    throw new System.Exception("иконка не снялась: " + why);
            }

            if (IconSprite(spec.Id) == null) waitingIcons.Add(spec.Id);

            report.Append("  — t").Append(spec.Tier).Append(' ').Append(spec.Name)
                  .Append(" (").Append(growableId).Append("), продажа ")
                  .Append(TierEconomy.SellPrice(spec.Tier)).Append(", грядка ")
                  .Append(TierEconomy.PlotPrice(spec.Tier, spec.Tree ? TreeGoldBase : VeinGoldBase))
                  .Append(" зол. + ").Append(TierEconomy.LadderMaterials(spec.Tier)).Append(" × ")
                  .Append(spec.PrevResource).Append('\n');
        }

        // ---- префабы ----

        private static GameObject TreePrefab(Spec spec)
        {
            string path = "Assets/Game/Prefabs/Trees/P_" + spec.Id + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var leaf = VegetationMaterial("M_" + Cap(spec.Id) + "_Leaf", spec.Leaf,
                                          maskHeight: 2f, speed: 0.65f, strength: 0.1f, turbulence: 0.16f);
            var bark = VegetationMaterial("M_" + Cap(spec.Id) + "_Bark", spec.Bark,
                                          maskHeight: 1.2f, speed: 0.7f, strength: 0.09f, turbulence: 0.12f);

            return BakePrefab(spec, path, renderer =>
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    // Слот листвы узнаём по имени исходного материала Kenney: «leafs*».
                    // Всё остальное у дерева — ствол.
                    string name = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                    mats[i] = name.Contains("leaf") ? leaf : bark;
                }
                renderer.sharedMaterials = mats;
            });
        }

        private static GameObject VeinPrefab(Spec spec)
        {
            string path = "Assets/Game/Prefabs/Veins/P_" + spec.Id + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var ore = OreMaterial("M_" + spec.Id, spec.Leaf, spec.Smooth);

            return BakePrefab(spec, path, renderer =>
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = ore;
                renderer.sharedMaterials = mats;
            });
        }

        private static GameObject BakePrefab(Spec spec, string path, System.Action<Renderer> recolor)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models + "/" + spec.Model + ".fbx");
            if (model == null) throw new System.Exception("нет модели " + spec.Model);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "P_" + spec.Id;

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                recolor(renderer);

            var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return saved;
        }

        // ---- материалы ----

        private static Material VegetationMaterial(string name, Color32 color,
                                                   float maskHeight, float speed, float strength, float turbulence)
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
            mat.SetFloat("_WindMaskHeight", maskHeight);
            mat.SetFloat("_WindSpeed", speed);
            mat.SetFloat("_WindStrength", strength);
            mat.SetFloat("_WindTurbulence", turbulence);
            mat.SetFloat("_WindPhaseScale", 1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material OreMaterial(string name, Color32 color, float smooth)
        {
            string path = "Assets/Game/Materials/Ore/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", 0.35f);   // как у остальных руд: металл, но не зеркало
            mat.SetFloat("_Smoothness", smooth);
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
                string line = spec.Tree ? "tree_" : "vein_";
                var item = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(
                    Farming + "/Shop_" + line + spec.Id + ".asset");
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

            // Клон шаблона, а не CreateInstance: все флаги линии (отрастание, порча, урожай
            // за уровень) приезжают из живого ассета восьмой ступени и не могут разойтись.
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

            // Свежая фишка импортируется текстурой, а не спрайтом, — дожимаем импорт сами,
            // как это делает конвейер иконок декора.
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
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        private static string Cap(string id) => char.ToUpperInvariant(id[0]) + id.Substring(1);
    }
}

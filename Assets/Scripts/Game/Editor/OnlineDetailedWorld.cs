using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Переезд мира с кубиков Kenney на детальные модели Quaternius (решение владельца
    /// 06.08.2026 по живому сравнению). Формы берутся из китов, цвета — из НАШИХ материалов:
    /// палитра и ветер остаются теми же, меняется только геометрия.
    /// <para>
    /// Материалы китов пустые (белый Lit без цвета) — это не дефект, а свойство: раскраска
    /// всё равно обязана идти из палитры проекта, иначе вернётся разнобой, который уже
    /// сводили. Слоты узнаются по имени материала модели (Leaves_*, Bark_*, Blossom_*...).
    /// </para>
    /// <para>
    /// Лес при переезде прореживается: детальное дерево в 15 раз дороже кубика по вершинам,
    /// и полная тысяча уронила бы веб-сборку на слабых машинах. Прореживание детерминировано
    /// хэшем позиции — повторный запуск не перетасует лес.
    /// </para>
    /// </summary>
    public static class OnlineDetailedWorld
    {
        private const string Kit = "Assets/ExternalAssets/Quaternius/StylizedNatureMegaKit/";
        private const string Veg = "Assets/Game/Materials/Vegetation/";
        private const string PrefabFolder = "Assets/Game/Prefabs/World";

        /// <summary>Какую долю леса оставить. 0.55 даёт ~470 из 860 — глазом лес остаётся лесом.</summary>
        private const float ForestKeep = 0.55f;

        [MenuItem("Farm/Онлайн/Детальный мир (Quaternius)")]
        public static void Build()
        {
            var report = new StringBuilder("[Онлайн] Детальный мир:\n");

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets/Game/Prefabs", "World");

            var forest = ForestPrefabs(report);
            var foliage = FoliagePrefabs(report);

            RetreePlots(report);
            ReRockVeins(report);

            // Сцены — в конце, когда все префабы готовы. Обе: лес и трава стоят и в меню.
            foreach (string scenePath in new[] { "Assets/Scenes/FarmerDemo.unity", "Assets/Scenes/MainMenu.unity" })
                RebuildScene(scenePath, forest, foliage, report);

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        // ---- лес ----

        private static List<GameObject> ForestPrefabs(StringBuilder report)
        {
            var mats = new Dictionary<string, Material>
            {
                { "leaves", Load(Veg + "M_Forest_Leaf.mat") },
                { "bark", Load(Veg + "M_Forest_Bark.mat") },
            };
            var dark = new Dictionary<string, Material>
            {
                { "leaves", Load(Veg + "M_Forest_Leaf_Dark.mat") },
                { "bark", Load(Veg + "M_Forest_Bark_Dark.mat") },
            };

            var list = new List<GameObject>();

            // Нормировка к росту старого леса обязательна: родной рост кита — 1.8 метра,
            // и без неё лес выходит по плечо забору (замечено глазами на первом прогоне).
            for (int i = 1; i <= 5; i++)
                list.Add(Bake("P_Forest_Common_" + i, Kit + "CommonTree_" + i + ".fbx",
                              i % 2 == 0 ? dark : mats, report, targetHeight: 3.4f));

            // Сосны — тёмной листвой и выше лиственных: хвоя и должна быть глуше и стройнее.
            for (int i = 1; i <= 3; i++)
                list.Add(Bake("P_Forest_Pine_" + i, Kit + "Pine_" + i + ".fbx", dark, report,
                              targetHeight: 4.3f, rebake: true));

            list.RemoveAll(p => p == null);
            return list;
        }

        // ---- трава, цветы, грибы, галька ----

        private sealed class FoliageSpec
        {
            public GameObject Prefab;
            public float Weight;
            public float ScaleMin, ScaleMax;
        }

        private static List<FoliageSpec> FoliagePrefabs(StringBuilder report)
        {
            var grass = new Dictionary<string, Material> { { "", Load(Veg + "M_Grass.mat") } };
            var fern = new Dictionary<string, Material> { { "", Load(Veg + "M_Bush.mat") } };

            Dictionary<string, Material> Flower(string cap) => new Dictionary<string, Material>
            {
                { "blossom", Load(Veg + cap) },
                { "petal", Load(Veg + cap) },
                { "flower", Load(Veg + cap) },
                { "", Load(Veg + "M_Grass.mat") },   // стебли и листья
            };

            var mushroom = new Dictionary<string, Material>
            {
                { "cap", Load(Veg + "M_Mushroom_Cap.mat") },
                { "top", Load(Veg + "M_Mushroom_Cap.mat") },
                { "", Load(Veg + "M_Mushroom_Stem.mat") },
            };

            var pebble = new Dictionary<string, Material> { { "", Load("Assets/Game/Materials/World/M_Rock.mat") } };

            var specs = new List<FoliageSpec>
            {
                Spec("P_Foliage_Grass_Short", Kit + "Grass_Wispy_Short.fbx", grass, 4f, 0.30f, report),
                Spec("P_Foliage_Grass_Tall", Kit + "Grass_Wispy_Tall.fbx", grass, 3f, 0.40f, report),
                Spec("P_Foliage_Clover", Kit + "Clover_1.fbx", grass, 2f, 0.28f, report),
                Spec("P_Foliage_Fern", Kit + "Fern_1.fbx", fern, 1.2f, 0.5f, report),
                Spec("P_Foliage_Flower_Red", Kit + "Flower_3_Single.fbx", Flower("M_Flower_Red.mat"), 0.4f, 0.4f, report),
                Spec("P_Foliage_Flower_Purple", Kit + "Flower_4_Single.fbx", Flower("M_Flower_Purple.mat"), 0.4f, 0.4f, report),
                Spec("P_Foliage_Flower_Yellow", Kit + "Flower_3_Group.fbx", Flower("M_Flower_Yellow.mat"), 0.4f, 0.42f, report),
                Spec("P_Foliage_Mushroom", Kit + "Mushroom_Common.fbx", mushroom, 0.12f, 0.3f, report),
                Spec("P_Foliage_Pebble", Kit + "Pebble_Round_2.fbx", pebble, 0.3f, 0.35f, report),
            };

            specs.RemoveAll(s => s == null || s.Prefab == null);
            return specs;
        }

        private static FoliageSpec Spec(string name, string model, Dictionary<string, Material> mats,
                                        float weight, float targetHeight, StringBuilder report)
        {
            var prefab = Bake(name, model, mats, report, targetHeight);
            return prefab == null ? null : new FoliageSpec
            {
                Prefab = prefab,
                Weight = weight,
                ScaleMin = 0.8f,
                ScaleMax = 1.3f,
            };
        }

        // ---- деревья-грядки ----

        private static void RetreePlots(StringBuilder report)
        {
            // Порядок фиксированный: у каждой породы своя форма кроны из пяти, по кругу.
            string[] ids =
            {
                "tree_wood", "tree_oak", "tree_ironwood", "tree_maple", "tree_yew", "tree_ebony",
                "tree_crimsonwood", "tree_spiritwood", "tree_moonwood", "tree_sunwood",
                "tree_stormwood", "tree_worldtree",
            };

            for (int i = 0; i < ids.Length; i++)
            {
                var growable = AssetDatabase.LoadAssetAtPath<GrowableDefinition>(
                    "Assets/Game/Farming/Growable_" + ids[i] + ".asset");
                if (growable == null) { report.Append("  ! нет ").Append(ids[i]).Append('\n'); continue; }

                // Цвета берём из НЫНЕШНЕГО спелого визуала: он уже одет в палитру линии,
                // и переезд формы не должен трогать язык цвета.
                var so = new SerializedObject(growable);
                var stages = so.FindProperty("_stages");
                var last = stages.GetArrayElementAtIndex(stages.arraySize - 1);
                var oldVisual = last.FindPropertyRelative("_visual").objectReferenceValue as GameObject;
                if (oldVisual == null) { report.Append("  ! у ").Append(ids[i]).Append(" нет визуала\n"); continue; }

                var mats = TreeMaterialsOf(oldVisual);
                string model = Kit + "CommonTree_" + (i % 5 + 1) + ".fbx";
                var prefab = Bake("P_Plot_" + ids[i], model, mats, report, targetHeight: 2.6f);
                if (prefab == null) continue;

                for (int s = 0; s < stages.arraySize; s++)
                    stages.GetArrayElementAtIndex(s).FindPropertyRelative("_visual").objectReferenceValue = prefab;

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(growable);
            }

            report.Append("  деревья-грядки переведены на детальные кроны\n");
        }

        /// <summary>Листва и кора старого дерева — по именам материалов, как их красил ремап.</summary>
        private static Dictionary<string, Material> TreeMaterialsOf(GameObject visual)
        {
            Material leaf = null, bark = null;

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    string name = mat.name.ToLowerInvariant();
                    if (leaf == null && name.Contains("leaf")) leaf = mat;
                    else if (bark == null && (name.Contains("bark") || name.Contains("wood"))) bark = mat;
                }

            return new Dictionary<string, Material>
            {
                { "leaves", leaf != null ? leaf : Load(Veg + "M_Forest_Leaf.mat") },
                { "", bark != null ? bark : Load(Veg + "M_Forest_Bark.mat") },
            };
        }

        // ---- жилы ----

        private static void ReRockVeins(StringBuilder report)
        {
            string[] ids =
            {
                "vein_copper", "vein_iron", "vein_silver", "vein_gold", "vein_platinum",
                "vein_mithril", "vein_adamant", "vein_starmetal", "vein_orichalcum",
                "vein_moonstone", "vein_dragonite", "vein_starheart",
            };

            for (int i = 0; i < ids.Length; i++)
            {
                var growable = AssetDatabase.LoadAssetAtPath<GrowableDefinition>(
                    "Assets/Game/Farming/Growable_" + ids[i] + ".asset");
                if (growable == null) continue;

                var so = new SerializedObject(growable);
                var stages = so.FindProperty("_stages");
                var oldVisual = stages.GetArrayElementAtIndex(stages.arraySize - 1)
                    .FindPropertyRelative("_visual").objectReferenceValue as GameObject;
                if (oldVisual == null) continue;

                // Металл — из старого камня: язык «жила = камень в цвете металла» не меняется.
                Material ore = null;
                foreach (var renderer in oldVisual.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var mat in renderer.sharedMaterials)
                        if (mat != null) { ore = mat; break; }
                    if (ore != null) break;
                }
                if (ore == null) continue;

                var mats = new Dictionary<string, Material> { { "", ore } };
                string model = Kit + "Rock_Medium_" + (i % 3 + 1) + ".fbx";
                var prefab = Bake("P_Vein_" + ids[i], model, mats, report, targetHeight: 0.9f);
                if (prefab == null) continue;

                for (int s = 0; s < stages.arraySize; s++)
                    stages.GetArrayElementAtIndex(s).FindPropertyRelative("_visual").objectReferenceValue = prefab;

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(growable);
            }

            report.Append("  жилы переведены на детальные скалы\n");
        }

        // ---- сцены ----

        private static void RebuildScene(string scenePath, List<GameObject> forest,
                                         List<FoliageSpec> foliage, StringBuilder report)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            int replaced = 0, thinned = 0;

            var root = GameObject.Find("Forest");
            if (root != null)
            {
                // Снизу вверх: удаление детей по ходу обхода сверху съедало бы индексы.
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var old = root.transform.GetChild(i);

                    // Уже переехавшее дерево не трогаем — инструмент можно запускать повторно.
                    if (old.name.StartsWith("P_Forest_")) continue;

                    Vector3 at = old.position;
                    int hash = Mathf.Abs(at.x.GetHashCode() * 31 + at.z.GetHashCode());

                    if ((hash % 100) / 100f > ForestKeep)
                    {
                        Object.DestroyImmediate(old.gameObject);
                        thinned++;
                        continue;
                    }

                    var pick = forest[hash % forest.Count];
                    var fresh = (GameObject)PrefabUtility.InstantiatePrefab(pick, root.transform);
                    fresh.transform.position = at;
                    fresh.transform.rotation = Quaternion.Euler(0f, hash % 360, 0f);
                    fresh.transform.localScale = Vector3.one * (0.9f + (hash % 37) / 37f * 0.5f);

                    Object.DestroyImmediate(old.gameObject);
                    replaced++;
                }
            }

            int scenery = RetreeScenery(forest);

            // Засев — новыми видами. SerializedObject, потому что _entries закрыт, и это
            // осознанно: рассевом владеет сцена, а не код.
            int reseeded = 0;
            foreach (var scatter in Object.FindObjectsByType<Farm.Juice.FoliageScatter>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(scatter);
                var entries = so.FindProperty("_entries");
                entries.arraySize = foliage.Count;

                for (int i = 0; i < foliage.Count; i++)
                {
                    var entry = entries.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("Prefab").objectReferenceValue = foliage[i].Prefab;
                    entry.FindPropertyRelative("Weight").floatValue = foliage[i].Weight;
                    entry.FindPropertyRelative("ScaleMin").floatValue = foliage[i].ScaleMin;
                    entry.FindPropertyRelative("ScaleMax").floatValue = foliage[i].ScaleMax;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(scatter);
                reseeded++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            report.Append("  ").Append(scene.name).Append(": заменено ").Append(replaced)
                  .Append(", прорежено ").Append(thinned).Append(", холмы ").Append(scenery)
                  .Append(", засевов обновлено ").Append(reseeded).Append('\n');
        }

        // ---- общая пекарня ----

        /// <summary>
        /// Префаб из модели кита: форма чужая, материалы наши. Слот выбирается по подстроке
        /// имени исходного материала; пустая строка в словаре — «всё остальное».
        /// </summary>
        private static GameObject Bake(string name, string modelPath, Dictionary<string, Material> mats,
                                       StringBuilder report, float targetHeight = 0f, bool rebake = true)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !rebake) return existing;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                report.Append("  ! нет модели ").Append(modelPath).Append('\n');
                return null;
            }

            // Пустой корень, модель — ребёнком, нормировка — на РЕБЁНКЕ. Масштаб корня
            // инстанса переопределяют все, кто расставляет: лес в сцене, засев, стадии
            // роста, — и нормировка на корне молча стиралась бы их override-ом.
            var root = new GameObject(name);

            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string source = slots[i] != null ? slots[i].name.ToLowerInvariant() : "";
                    Material best = null;

                    foreach (var pair in mats)
                        if (pair.Key.Length > 0 && source.Contains(pair.Key)) { best = pair.Value; break; }

                    if (best == null) mats.TryGetValue("", out best);
                    if (best == null && mats.TryGetValue("leaves", out var leaf)) best = leaf;
                    if (best != null) slots[i] = best;
                }
                renderer.sharedMaterials = slots;
            }

            // Нормировка к целевой высоте: у кита свой масштаб, и «трава по пояс дому»
            // читалась бы как ошибка, а не как стиль.
            if (targetHeight > 0f)
            {
                var bounds = new Bounds(root.transform.position, Vector3.zero);
                foreach (var r in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                if (bounds.size.y > 0.01f)
                    body.transform.localScale = Vector3.one * (targetHeight / bounds.size.y);
            }

            // SaveAsPrefabAsset поверх существующего пути сохраняет guid — сцены, уже
            // ссылающиеся на префаб, подхватят пересборку сами.
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>Деревья на холмах: узел Scenery держит их вперемешку с камнями.</summary>
        private static int RetreeScenery(List<GameObject> forest)
        {
            var scenery = GameObject.Find("Scenery");
            if (scenery == null) return 0;

            int replaced = 0;
            for (int i = scenery.transform.childCount - 1; i >= 0; i--)
            {
                var old = scenery.transform.GetChild(i);
                if (old.name.StartsWith("P_Forest_")) continue;
                if (!old.name.ToLowerInvariant().StartsWith("tree")) continue;

                Vector3 at = old.position;
                int hash = Mathf.Abs(at.x.GetHashCode() * 31 + at.z.GetHashCode());

                // Без прореживания: девять десятков акцентов на холмах погоды не делают,
                // а редеющий именно на горизонте лес заметнее всего.
                var fresh = (GameObject)PrefabUtility.InstantiatePrefab(forest[hash % forest.Count], scenery.transform);
                fresh.transform.position = at;
                fresh.transform.rotation = Quaternion.Euler(0f, hash % 360, 0f);
                fresh.transform.localScale = old.localScale.y > 1.5f
                    ? Vector3.one * 1.4f
                    : Vector3.one * (0.9f + (hash % 37) / 37f * 0.5f);

                Object.DestroyImmediate(old.gameObject);
                replaced++;
            }

            return replaced;
        }

        private static Material Load(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Собрать вкладку «Декор» из готовых замыслов: на каждый ImprovementDefinition —
    /// покупаемый Prop-товар, ссылающийся на замысел (так покупка попадает в сохранение
    /// под именем Improvement_&lt;id&gt;, см. Shop.Deliver).
    /// <para>
    /// Инструмент повторяемый: существующие товары обновляет, новые создаёт, в каталог
    /// добавляет только отсутствующее. Имена и цены — таблица ниже; замысел без строки
    /// в таблице получает имя из id и среднюю цену, а дизайнер потом поправит ассет руками —
    /// правило «числа живут в ассетах» распространяется и на декор.
    /// </para>
    /// </summary>
    public static class OnlineDecorCatalog
    {
        private struct Entry
        {
            public string Name;
            public int Gold;
            public Entry(string name, int gold) { Name = name; Gold = gold; }
        }

        /// <summary>Витрина: человеческое имя и цена в золоте. Дешёвое — мелочь, дорогое — характер.</summary>
        private static readonly Dictionary<string, Entry> Known = new Dictionary<string, Entry>
        {
            { "bench",          new Entry("Скамейка", 220) },
            { "flowerbed",      new Entry("Клумба", 90) },
            { "barn_dog",       new Entry("Дворовый пёс", 600) },
            { "barn_lamp",      new Entry("Фонарь у хлева", 160) },
            { "mine_lamp",      new Entry("Шахтёрский фонарь", 160) },
            { "barn_barrel",    new Entry("Бочка", 70) },
            { "barn_crates",    new Entry("Ящики", 70) },
            { "barn_plant",     new Entry("Кадка с цветком", 110) },
            { "mine_planks",    new Entry("Крепёжные доски", 80) },
            { "mine_rocks",     new Entry("Валуны", 90) },
            { "shed_fence",     new Entry("Плетень", 100) },
            { "shed_hay",       new Entry("Стог сена", 120) },
            { "apiary_flowers", new Entry("Цветы у пасеки", 130) },
            { "shed_lamp",      new Entry("Фонарь у сарая", 160) },
            { "signpost",       new Entry("Указатель", 140) },
            { "well_barrel",    new Entry("Бочка у колодца", 70) },
            { "well_flowers",   new Entry("Цветы у колодца", 130) },
            { "yardfire",       new Entry("Костровище", 350) },
        };

        private const string AssetFolder = "Assets/Game/Farming";
        private const int DefaultGold = 120;

        [MenuItem("Farm/Онлайн/Собрать каталог декора")]
        public static void Build()
        {
            var report = new StringBuilder("[Онлайн] Каталог декора:\n");
            var created = new List<ShopItemDefinition>();
            int updated = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:ImprovementDefinition"))
            {
                var improvement = AssetDatabase.LoadAssetAtPath<ImprovementDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (improvement == null || improvement.Prefab == null) continue;

                string path = AssetFolder + "/Shop_Decor_" + improvement.Id + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(path);
                bool fresh = item == null;

                if (fresh)
                {
                    item = ScriptableObject.CreateInstance<ShopItemDefinition>();
                    AssetDatabase.CreateAsset(item, path);
                }

                Known.TryGetValue(improvement.Id, out var entry);
                string displayName = string.IsNullOrEmpty(entry.Name) ? Humanize(improvement.Id) : entry.Name;
                int gold = entry.Gold > 0 ? entry.Gold : DefaultGold;

                var so = new SerializedObject(item);
                so.FindProperty("_id").stringValue = "decor_" + improvement.Id;
                so.FindProperty("_displayName").stringValue = displayName;
                so.FindProperty("_description").stringValue = "украшение для фермы; ставится где нашлось место, переставляется как угодно";
                so.FindProperty("_category").enumValueIndex = (int)ShopCategory.Decor;
                so.FindProperty("_kind").enumValueIndex = (int)ShopItemKind.Prop;
                so.FindProperty("_improvement").objectReferenceValue = improvement;
                so.FindProperty("_price").FindPropertyRelative("Gold").intValue = gold;
                so.FindProperty("_maxOwned").intValue = 0;   // декора много не бывает
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);

                if (fresh) created.Add(item); else updated++;
                report.Append("  — ").Append(displayName).Append(" (").Append(gold).Append(" зол.)")
                      .Append(fresh ? " создан" : " обновлён").Append('\n');
            }

            AppendToCatalog(created, report);
            AssetDatabase.SaveAssets();

            report.Append("Готово: новых ").Append(created.Count).Append(", обновлено ").Append(updated).Append('.');
            Debug.Log(report.ToString());
        }

        /// <summary>Дописать новые товары в ShopCatalog: не в каталоге — значит не существует.</summary>
        private static void AppendToCatalog(List<ShopItemDefinition> created, StringBuilder report)
        {
            if (created.Count == 0) return;

            var guids = AssetDatabase.FindAssets("t:ShopCatalog");
            if (guids.Length == 0)
            {
                report.Append("  ! ShopCatalog не найден — товары созданы, но в магазине их не будет\n");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
            var so = new SerializedObject(catalog);
            var items = so.FindProperty("_items");

            foreach (var item in created)
            {
                items.arraySize++;
                items.GetArrayElementAtIndex(items.arraySize - 1).objectReferenceValue = item;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            report.Append("  в каталог дописано: ").Append(created.Count).Append('\n');
        }

        private static string Humanize(string id) => id.Replace('_', ' ');
    }
}

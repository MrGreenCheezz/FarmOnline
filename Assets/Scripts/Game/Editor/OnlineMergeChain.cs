using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Заполнить <c>GrowableDefinition._mergeNext</c> — лестницу перехода ступеней слиянием
    /// (слияние 2.0, решение владельца 06.08.2026): две грядки 20 уровня перерождаются
    /// в следующую ступень СВОЕЙ линии. Линии: дерево, руда, посевы, живность.
    /// <para>
    /// Категория здесь недостаточна: деревья и посевы делят <see cref="ResourceCategory.Crop"/>,
    /// поэтому линия деревьев и жил распознаётся по имени ассета (tree_*, vein_*). Следующая
    /// ступень — единственное растимое той же линии со ступенью ресурса +1; вершина линии и
    /// неоднозначный кандидат остаются пустыми (вершина — по замыслу, неоднозначность — руками).
    /// </para>
    /// <para>Инструмент повторяемый: перезаписывает поле у всех растимых при каждом запуске.</para>
    /// </summary>
    public static class OnlineMergeChain
    {
        [MenuItem("Farm/Онлайн/Заполнить лестницу слияния")]
        public static void Build()
        {
            var all = AssetDatabase.FindAssets("t:GrowableDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<GrowableDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null && d.YieldResource != null)
                .ToList();

            var byLine = new Dictionary<string, List<GrowableDefinition>>();
            foreach (var def in all)
            {
                string line = LineOf(def);
                if (!byLine.TryGetValue(line, out var list)) byLine[line] = list = new List<GrowableDefinition>();
                list.Add(def);
            }

            var report = new StringBuilder("[Онлайн] Лестница слияния:\n");
            int set = 0, tops = 0, ambiguous = 0;

            foreach (var def in all)
            {
                var peers = byLine[LineOf(def)];
                int nextTier = def.YieldResource.Tier + 1;
                var candidates = peers.Where(p => p.YieldResource.Tier == nextTier).ToList();

                GrowableDefinition next = candidates.Count == 1 ? candidates[0] : null;
                if (candidates.Count == 0) tops++;
                if (candidates.Count > 1)
                {
                    ambiguous++;
                    report.Append("  ? ").Append(def.name).Append(": кандидатов ").Append(candidates.Count)
                          .Append(" (").Append(string.Join(", ", candidates.Select(c => c.name))).Append(") — руками\n");
                }

                var so = new SerializedObject(def);
                so.FindProperty("_mergeNext").objectReferenceValue = next;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                if (next != null)
                {
                    set++;
                    report.Append("  ").Append(def.name).Append(" -> ").Append(next.name).Append('\n');
                }
            }

            AssetDatabase.SaveAssets();
            report.Append("Назначено ").Append(set).Append(", вершин ").Append(tops)
                  .Append(", неоднозначных ").Append(ambiguous).Append(" из ").Append(all.Count);
            Debug.Log(report.ToString());
        }

        private static string LineOf(GrowableDefinition def)
        {
            if (def.Category == ResourceCategory.Ore) return "руда";
            if (def.Category == ResourceCategory.Livestock) return "живность";
            return def.name.Contains("tree_") ? "дерево" : "посевы";
        }
    }
}

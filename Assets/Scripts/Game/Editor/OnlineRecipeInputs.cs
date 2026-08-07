using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Перевести рецепты мастерских с одиночного входа на список строк сырья
    /// (составные рецепты, 07.08.2026 — docs/PLAN-RESOURCES.md).
    /// <para>
    /// Наследие читается и без этого инструмента: <c>WorkshopRecipe</c> отдаёт старые поля
    /// как одну строку, поэтому ассеты работали с первой же минуты перехода. Инструмент
    /// нужен, чтобы наследие не жило вечно: пока сырьё лежит в двух местах, следующая правка
    /// рецепта руками в инспекторе рискует разойтись с тем, что читает код.
    /// </para>
    /// <para>Повторяемый: у переведённых рецептов наследия уже нет, и они пропускаются.</para>
    /// </summary>
    public static class OnlineRecipeInputs
    {
        [MenuItem("Farm/Онлайн/Перевести рецепты на составные")]
        public static void Convert()
        {
            var definitions = AssetDatabase.FindAssets("t:BuildingDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<BuildingDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null && d.Recipes != null && d.Recipes.Count > 0)
                .ToList();

            var report = new StringBuilder("[Онлайн] Рецепты на списки сырья:\n");
            int moved = 0;

            foreach (var definition in definitions)
            {
                bool dirty = false;

                foreach (var recipe in definition.Recipes)
                {
                    if (recipe == null || !recipe.CaptureLegacy(out var resource, out int amount)) continue;

                    recipe.AdoptLegacy();
                    dirty = true;
                    moved++;

                    report.Append("  ").Append(definition.Id).Append(": ")
                          .Append(amount).Append(' ').Append(resource.DisplayName)
                          .Append(" → строка списка\n");
                }

                if (!dirty) continue;

                EditorUtility.SetDirty(definition);
            }

            if (moved > 0) AssetDatabase.SaveAssets();

            report.Append("Переведено строк: ").Append(moved)
                  .Append(" (постройек с рецептами: ").Append(definitions.Count).Append(')');
            Debug.Log(report.ToString());
        }
    }
}

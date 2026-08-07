using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Экспорт каталога для сервера: цены, тайминги и урожаи в Tools/catalog.json.
    /// <para>
    /// Сервер не умеет читать ассеты Unity, а проверять экономику снимков обязан по тем же
    /// числам, что и клиент, — иначе два источника истины разойдутся при первой правке
    /// баланса. Отсюда правило: перегнал тайминги или цены — перегони и каталог, это одна
    /// команда меню. Дата в файле выдаст забытый экспорт.
    /// </para>
    /// </summary>
    public static class OnlineCatalogExport
    {
        [Serializable]
        private sealed class ResourceRow
        {
            public string id;
            public int tier;
            public int sellPrice;
        }

        [Serializable]
        private sealed class GrowableRow
        {
            public string id;
            public string resourceId;
            public int tier;
            public double growSeconds;
            public int baseYield;
        }

        /// <summary>Одна строка сырья рецепта в каталоге.</summary>
        [Serializable]
        private sealed class WorkshopInputRow
        {
            public string id;
            public int amount;
        }

        [Serializable]
        private sealed class WorkshopRow
        {
            public string buildingId;

            /// <summary>Всё сырьё партии. Составной рецепт — несколько строк.</summary>
            public WorkshopInputRow[] inputs;

            // Первая строка отдельными полями — ради серверов на старом коде: они читают
            // одиночный вход и без списка посчитали бы маржу по нулевой цене сырья, то есть
            // завысили бы потолок дохода. Новый сервер предпочитает inputs, старый — эти два.
            public string inputId;
            public int inputAmount;

            public string outputId;
            public int outputAmount;
            public double seconds;

            /// <summary>Скорость мастерской на последнем уровне — потолок для эвристики.</summary>
            public float maxOutput;

            /// <summary>С какого уровня постройки рецепт открыт: сервер кредитует потолок
            /// дохода только открытыми рецептами, а не лучшим на любом уровне.</summary>
            public int unlockLevel;
        }

        [Serializable]
        private sealed class Catalog
        {
            public string generatedAtUtc;
            public ResourceRow[] resources;
            public GrowableRow[] growables;
            public WorkshopRow[] workshops;
        }

        [MenuItem("Farm/Онлайн/Экспортировать каталог для сервера")]
        public static void Export()
        {
            var resources = new List<ResourceRow>();
            foreach (var guid in AssetDatabase.FindAssets("t:ResourceDefinition"))
            {
                var res = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (res == null) continue;
                resources.Add(new ResourceRow { id = res.Id, tier = res.Tier, sellPrice = res.SellPrice });
            }

            var growables = new List<GrowableRow>();
            foreach (var guid in AssetDatabase.FindAssets("t:GrowableDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<GrowableDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;
                growables.Add(new GrowableRow
                {
                    id = def.Id,
                    resourceId = def.YieldResource != null ? def.YieldResource.Id : "",
                    tier = def.YieldResource != null ? def.YieldResource.Tier : 1,
                    growSeconds = def.TotalGrowTime,
                    baseYield = def.YieldFor(1),
                });
            }

            // Рецепты мастерских: по ним сервер поднимает потолок дохода фермы со станками.
            // Без этого ускоренный мастеровым оборот сыпал бы ложными suspicious (этап 2).
            var workshops = new List<WorkshopRow>();
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.Recipes == null || def.Recipes.Count == 0) continue;

                float maxOutput = 1f;
                for (int level = 1; level <= 32; level++)
                {
                    float output = def.OutputAt(level);
                    if (output <= 0f) break;
                    if (output > maxOutput) maxOutput = output;
                }

                foreach (var recipe in def.Recipes)
                {
                    if (recipe == null || !recipe.IsValid) continue;

                    int lines = recipe.InputCount;
                    var inputs = new WorkshopInputRow[lines];
                    for (int line = 0; line < lines; line++)
                        inputs[line] = new WorkshopInputRow
                        {
                            id = recipe.InputResourceAt(line).Id,
                            amount = recipe.InputAmountAt(line),
                        };

                    workshops.Add(new WorkshopRow
                    {
                        buildingId = def.Id,
                        inputs = inputs,
                        inputId = inputs[0].id,
                        inputAmount = inputs[0].amount,
                        outputId = recipe.Output.Id,
                        outputAmount = recipe.OutputAmount,
                        seconds = recipe.Seconds,
                        maxOutput = maxOutput,
                        unlockLevel = recipe.UnlockLevel,
                    });
                }
            }

            var catalog = new Catalog
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                resources = resources.ToArray(),
                growables = growables.ToArray(),
                workshops = workshops.ToArray(),
            };

            string path = Path.Combine(Application.dataPath, "..", "Tools", "catalog.json");
            File.WriteAllText(path, JsonUtility.ToJson(catalog, true));

            Debug.Log("[Онлайн] Каталог экспортирован: " + resources.Count + " ресурсов, "
                      + growables.Count + " растимых, " + workshops.Count + " рецептов → "
                      + Path.GetFullPath(path));
        }
    }
}

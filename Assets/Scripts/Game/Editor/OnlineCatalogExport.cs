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

        [Serializable]
        private sealed class Catalog
        {
            public string generatedAtUtc;
            public ResourceRow[] resources;
            public GrowableRow[] growables;
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

            var catalog = new Catalog
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                resources = resources.ToArray(),
                growables = growables.ToArray(),
            };

            string path = Path.Combine(Application.dataPath, "..", "Tools", "catalog.json");
            File.WriteAllText(path, JsonUtility.ToJson(catalog, true));

            Debug.Log("[Онлайн] Каталог экспортирован: " + resources.Count + " ресурсов, "
                      + growables.Count + " растимых → " + Path.GetFullPath(path));
        }
    }
}

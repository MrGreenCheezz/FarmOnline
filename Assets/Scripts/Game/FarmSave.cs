using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Farm.Farming;
using Farm.Characters;

namespace Farm.Game
{
    /// <summary>
    /// Снять партию в снимок и разложить снимок обратно по сцене.
    /// <para>
    /// Живёт в отдельной сборке, потому что видит обе половины игры сразу — ферму и жителей, —
    /// а между собой они по-прежнему не знают друг друга больше, чем знали. Сохранение читает
    /// состояние через публичные <c>CaptureState</c>/<c>RestoreState</c> самих компонентов:
    /// каждый отвечает за своё, а не отдаёт внутренности наружу.
    /// </para>
    /// </summary>
    public static class FarmSave
    {
        private const string FileName = "farm-save.json";

        /// <summary>Ключ в PlayerPrefs — им пользуется веб-сборка.</summary>
        private const string PrefKey = "farm.save";

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// В браузере храним в PlayerPrefs, а не в файле.
        /// <para>
        /// Файловая система WebGL — это виртуальный слой поверх IndexedDB, и записанное в него
        /// попадает в базу только на синхронизации, момент которой игре не принадлежит. Вкладка,
        /// закрытая между записью и синхронизацией, унесла бы партию с собой. У PlayerPrefs
        /// сохранение явное — <c>Save()</c>, — и потому надёжное.
        /// </para>
        /// </summary>
        private const bool UsePrefs = true;
#else
        private const bool UsePrefs = false;
#endif

        private static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>Есть ли что продолжать.</summary>
        public static bool Exists =>
            UsePrefs ? !string.IsNullOrEmpty(PlayerPrefs.GetString(PrefKey, "")) : File.Exists(Path);

        // ---- хранилище ----

        /// <summary>
        /// Проставить штампы времени и превратить снимок в JSON. Отдельно от записи,
        /// потому что тот же текст без изменений уезжает на сервер: два разных JSON
        /// одного снимка рано или поздно разошлись бы.
        /// </summary>
        public static string Serialize(FarmSaveData data)
        {
            if (data == null) return null;

            data.SavedAtUtc = DateTime.UtcNow.ToString("O");
            // Часы игры к этому моменту — unix-секунды (ServerClock из Farm.Net);
            // по этому штампу оффлайн-режим считает, сколько ферма прожила закрытой.
            data.SavedAtUnix = FarmingRuntime.Now;
            return JsonUtility.ToJson(data, true);
        }

        public static bool Write(FarmSaveData data) => WriteJson(Serialize(data));

        /// <summary>Записать уже сериализованный снимок в локальное хранилище.</summary>
        public static bool WriteJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                if (UsePrefs)
                {
                    PlayerPrefs.SetString(PrefKey, json);
                    PlayerPrefs.Save();
                }
                else
                {
                    File.WriteAllText(Path, json);
                }

                return true;
            }
            catch (Exception e)
            {
                // Не бросаем дальше: неудачное сохранение не должно ронять игру посреди дня.
                Debug.LogError("[Save] Не удалось записать сохранение: " + e.Message);
                return false;
            }
        }

        public static FarmSaveData Read()
        {
            if (!Exists) return null;

            try
            {
                string json = UsePrefs ? PlayerPrefs.GetString(PrefKey, "") : File.ReadAllText(Path);

                var data = JsonUtility.FromJson<FarmSaveData>(json);
                if (data == null) Debug.LogWarning("[Save] Сохранение пусто или испорчено");
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError("[Save] Не удалось прочитать сохранение: " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            try
            {
                if (UsePrefs)
                {
                    PlayerPrefs.DeleteKey(PrefKey);
                    PlayerPrefs.Save();
                }
                else if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            catch (Exception e) { Debug.LogError("[Save] Не удалось удалить сохранение: " + e.Message); }
        }

        // ---- снять ----

        public static FarmSaveData Capture()
        {
            var data = new FarmSaveData();

            var wallet = Wallet.Instance;
            data.Gold = wallet != null ? wallet.Gold : 0;

            var storage = FarmingRuntime.Sink as Inventory;
            data.Storage = storage != null ? storage.CaptureState() : new InventorySnapshot();

            var clock = DayNightCycle.Instance;
            if (clock != null) { data.Time01 = clock.Time01; data.Day = clock.Day; }

            data.TotalHarvested = FarmProgress.TotalHarvested;
            data.TotalMerges = FarmProgress.TotalMerges;
            data.FarmLevel = FarmLevels.Current;
            data.Footpaths = Footpaths.CaptureWorn();

            CapturePlots(data);
            var buildingIndex = CaptureBuildings(data);
            CaptureImprovements(data);
            CaptureShop(data);
            CaptureFarmer(data, buildingIndex);

            return data;
        }

        private static void CapturePlots(FarmSaveData data)
        {
            var plots = new List<PlotSave>(GrowableRegistry.Count);

            var all = GrowableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var plot = all[i];
                if (plot == null || plot.Definition == null) continue;

                // Пустые не сохраняем: восстанавливать нечего, а грядка «ни с чем» на загрузке
                // выглядела бы как потерянная покупка.
                if (plot.Phase == GrowthPhase.Empty || plot.Phase == GrowthPhase.Withered) continue;

                plot.CaptureState(out double elapsed, out double ripe, out float ownSpeed);

                plots.Add(new PlotSave
                {
                    GrowableId = plot.Definition.Id,
                    Level = plot.Level,
                    Ready = plot.IsReady,
                    Uid = plot.Uid,
                    ElapsedGrowth = elapsed,
                    RipeSeconds = ripe,
                    OwnGrowthSpeed = ownSpeed,
                    Position = plot.transform.position,
                    Yaw = plot.transform.eulerAngles.y
                });
            }

            data.Plots = plots.ToArray();
        }

        /// <summary>Возвращает соответствие «постройка → её индекс в сейве» — по нему пишутся дворики.</summary>
        private static Dictionary<Building, int> CaptureBuildings(FarmSaveData data)
        {
            var saved = new List<BuildingSave>();
            var index = new Dictionary<Building, int>();

            var all = BuildingRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var building = all[i];
                if (building == null || building.Definition == null) continue;

                index[building] = saved.Count;
                saved.Add(new BuildingSave
                {
                    BuildingId = building.Definition.Id,
                    Level = building.Level,
                    Position = building.transform.position,
                    Yaw = building.transform.eulerAngles.y
                });
            }

            data.Buildings = saved.ToArray();
            return index;
        }

        /// <summary>
        /// Построенное жителем узнаётся по имени объекта: <c>Improvement_&lt;id&gt;</c>. Метка
        /// в имени, а не компонент, — замысел это чистая вещь без поведения, и вешать на неё
        /// компонент только ради сейва значило бы платить объектом за строку.
        /// </summary>
        private static void CaptureImprovements(FarmSaveData data)
        {
            const string Prefix = "Improvement_";
            var saved = new List<ImprovementSave>();

            var all = MovableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var movable = all[i];
                if (movable == null || !movable.name.StartsWith(Prefix, StringComparison.Ordinal)) continue;

                saved.Add(new ImprovementSave
                {
                    ImprovementId = movable.name.Substring(Prefix.Length),
                    Position = movable.transform.position,
                    Yaw = movable.transform.eulerAngles.y
                });
            }

            data.Improvements = saved.ToArray();
        }

        private static void CaptureShop(FarmSaveData data)
        {
            var shop = Shop.Instance;
            if (shop == null) return;

            var saved = new List<ShopOwnedSave>();
            foreach (var pair in shop.CaptureOwned())
            {
                if (pair.Key == null || pair.Value <= 0) continue;
                saved.Add(new ShopOwnedSave { ItemId = pair.Key.Id, Count = pair.Value });
            }

            data.ShopOwned = saved.ToArray();
        }

        private static void CaptureFarmer(FarmSaveData data, Dictionary<Building, int> buildingIndex)
        {
            var farmer = FarmerRegistry.Primary;
            if (farmer == null) return;

            // Снимок умеет делать конкретная реализация: интерфейсу он не нужен — контейнеров
            // много, а сохраняется ими только тот, что лежит в сцене.
            var pack = farmer.Inventory as Inventory;

            var save = new FarmerSave
            {
                Position = farmer.transform.position,
                Yaw = farmer.transform.eulerAngles.y,
                Pack = pack != null ? pack.CaptureState() : new InventorySnapshot(),
                HiredUntilUnix = farmer.HiredUntil
            };

            var needs = farmer.GetComponent<CharacterNeeds>();
            if (needs != null)
                needs.CaptureState(out save.Satiety01, out save.Hydration01, out save.Energy01);

            var skills = farmer.Skills;
            if (skills != null) skills.CaptureState(out save.SkillLevels, out save.SkillXp);

            var traits = farmer.Traits;
            if (traits != null) save.Traits = traits.CaptureState();

            data.Farmer = save;

            var built = new List<BuiltSave>();
            foreach (var pair in farmer.CaptureBuilt())
            {
                if (pair.Key == null || pair.Value <= 0) continue;
                built.Add(new BuiltSave { ImprovementId = pair.Key.Id, Count = pair.Value });
            }
            data.Built = built.ToArray();

            var yards = new List<YardSave>();
            foreach (var yard in farmer.CaptureBuiltYards())
            {
                if (yard.Key == null || !buildingIndex.TryGetValue(yard.Key, out int index)) continue;

                foreach (var entry in yard.Value)
                {
                    if (entry.Key == null || entry.Value <= 0) continue;
                    yards.Add(new YardSave
                    {
                        BuildingIndex = index,
                        ImprovementId = entry.Key.Id,
                        Count = entry.Value
                    });
                }
            }
            data.Yards = yards.ToArray();
        }

        // ---- разложить ----

        /// <summary>
        /// Разложить снимок по сцене. Порядок не косметический:
        /// постройки встают <b>раньше</b> грядок, потому что грядка при восстановлении сразу
        /// спрашивает ауры о своей скорости роста, — иначе первая же загрузка отдала бы
        /// каждой грядке скорость «без построек».
        /// </summary>
        /// <param name="offlineSeconds">
        /// Сколько настенных секунд ферма прожила закрытой — рост догонит это время.
        /// Кто считает дельту, тот и отвечает за её честность: онлайн — сервер
        /// (saved_at и serverNow оба его), без сети — локальный UTC против SavedAtUnix.
        /// </param>
        public static void Apply(FarmSaveData data, double offlineSeconds = 0.0)
        {
            if (data == null) return;

            var registry = ContentRegistry.Instance;
            if (registry == null) { Debug.LogError("[Save] Нет реестра контента — загрузка отменена"); return; }

            // Окно восстановления: пока оно открыто, ферма недостоверна — старое снесено
            // отложенным Destroy и ещё числится в реестрах, новое не создано. Оформители
            // (заросли и прочие, кто читает содержимое фермы) ждут закрытия окна.
            FarmingRuntime.BeginRestore();
            try
            {
                ApplyState(data, registry, offlineSeconds);
            }
            finally
            {
                // Окно закрывается при любом исходе: упавшая загрузка не должна оставить
                // ферму навсегда «в процессе восстановления» — это тихо выключило бы
                // всё оформление до перезапуска.
                FarmingRuntime.EndRestore();
            }
        }

        /// <summary>Собственно раскладка. Порядок строк здесь — не косметика, см. комментарии.</summary>
        private static void ApplyState(FarmSaveData data, ContentRegistry registry, double offlineSeconds)
        {
            ClearScene();

            // Уровень — самым первым из всего состояния, и уж точно раньше построек и грядок:
            // он двигает край земли, а по краю меряются забор, рельеф и место, куда магазин
            // кладёт покупку. Восстанови его после расстановки — и сохранённая ферма успела бы
            // разложиться по старой, тесной земле.
            // Ноль здесь означает сейв, снятый до появления лестницы: у него первый уровень.
            FarmLevels.RestoreState(data.FarmLevel > 0 ? data.FarmLevel : 1);

            var wallet = Wallet.Instance;
            if (wallet != null) wallet.RestoreState(data.Gold);

            if (FarmingRuntime.Sink is Inventory storage)
            {
                storage.Clear();
                storage.RestoreState(data.Storage, registry.Resource);
            }

            var clock = DayNightCycle.Instance;
            if (clock != null) clock.RestoreState(data.Time01, data.Day);

            FarmProgress.RestoreState(data.TotalHarvested, data.TotalMerges);

            var buildings = ApplyBuildings(data, registry);
            FarmBuffs.Refresh();   // ауры готовы — теперь грядкам есть что спросить

            ApplyPlots(data, registry, offlineSeconds);
            ApplyImprovements(data, registry);
            ApplyShop(data, registry);
            ApplyFarmer(data, registry, buildings);

            Footpaths.RestoreWorn(data.Footpaths);
            FarmBuffs.Refresh();

            Debug.Log("[Save] Загружено: " + data.Describe());
        }

        /// <summary>
        /// Снести то, что сцена расставила сама. Стартовые грядки демо-сцены иначе сложились бы
        /// с сохранёнными, и каждая загрузка удваивала бы ферму.
        /// </summary>
        private static void ClearScene()
        {
            var plots = new List<Growable>(GrowableRegistry.All);
            foreach (var plot in plots)
                if (plot != null) UnityEngine.Object.Destroy(plot.gameObject);

            var buildings = new List<Building>(BuildingRegistry.All);
            foreach (var building in buildings)
                if (building != null) UnityEngine.Object.Destroy(building.gameObject);

            var movables = new List<Movable>(MovableRegistry.All);
            foreach (var movable in movables)
                if (movable != null && movable.name.StartsWith("Improvement_", StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(movable.gameObject);
        }

        private static List<Building> ApplyBuildings(FarmSaveData data, ContentRegistry registry)
        {
            var placed = new List<Building>(data.Buildings.Length);
            var shop = Shop.Instance;

            foreach (var save in data.Buildings)
            {
                var definition = registry.Building(save.BuildingId);
                if (definition == null || definition.Prefab == null)
                {
                    Debug.LogWarning("[Save] Постройка '" + save.BuildingId + "' не найдена — пропущена");
                    placed.Add(null);   // индексы двориков обязаны сойтись
                    continue;
                }

                var instance = UnityEngine.Object.Instantiate(
                    definition.Prefab, save.Position, Quaternion.Euler(0f, save.Yaw, 0f));
                instance.name = "Building_" + definition.Id;

                var building = instance.GetComponent<Building>();
                if (building == null) building = instance.AddComponent<Building>();
                building.Configure(definition, save.Level);

                if (definition.Service == BuildingService.Workshop && instance.GetComponent<Workshop>() == null)
                    instance.AddComponent<Workshop>();

                if (instance.GetComponent<Movable>() == null) instance.AddComponent<Movable>();

                if (shop != null) shop.RegisterPlaced(instance.transform);
                placed.Add(building);
            }

            return placed;
        }

        private static void ApplyPlots(FarmSaveData data, ContentRegistry registry, double offlineSeconds)
        {
            var parent = GameObject.Find("Plots");
            var shop = Shop.Instance;

            foreach (var save in data.Plots)
            {
                var definition = registry.Growable(save.GrowableId);
                if (definition == null)
                {
                    Debug.LogWarning("[Save] Растимое '" + save.GrowableId + "' не найдено — пропущено");
                    continue;
                }

                var go = new GameObject("Plot_" + definition.Id);
                go.transform.SetPositionAndRotation(save.Position, Quaternion.Euler(0f, save.Yaw, 0f));
                if (parent != null) go.transform.SetParent(parent.transform, true);

                var growable = go.AddComponent<Growable>();
                go.AddComponent<GrowableVisuals>();

                if (definition.Category == ResourceCategory.Livestock)
                    go.AddComponent<GrazingAnimal>();

                growable.RestoreState(definition, save.Level, save.Ready,
                                      save.ElapsedGrowth, save.RipeSeconds, save.OwnGrowthSpeed,
                                      offlineSeconds, save.Uid);

                if (shop != null) shop.RegisterPlaced(go.transform);
            }
        }

        private static void ApplyImprovements(FarmSaveData data, ContentRegistry registry)
        {
            var shop = Shop.Instance;

            foreach (var save in data.Improvements)
            {
                var definition = registry.Improvement(save.ImprovementId);
                if (definition == null || definition.Prefab == null)
                {
                    Debug.LogWarning("[Save] Замысел '" + save.ImprovementId + "' не найден — пропущен");
                    continue;
                }

                var built = UnityEngine.Object.Instantiate(
                    definition.Prefab, save.Position, Quaternion.Euler(0f, save.Yaw, 0f));
                built.name = "Improvement_" + definition.Id;

                if (built.GetComponent<Movable>() == null) built.AddComponent<Movable>();

                // И магазину — как постройкам с грядками: иначе купленное поедет прямо
                // в клумбу. Дублирует проверку по MovableRegistry, но стоит дёшево и
                // страхует на случай, если замысел останется без Movable.
                if (shop != null) shop.RegisterPlaced(built.transform);
            }
        }

        private static void ApplyShop(FarmSaveData data, ContentRegistry registry)
        {
            var shop = Shop.Instance;
            if (shop == null) return;

            foreach (var save in data.ShopOwned)
            {
                var item = registry.ShopItem(save.ItemId);
                if (item != null) shop.RestoreOwned(item, save.Count);
            }
        }

        private static void ApplyFarmer(FarmSaveData data, ContentRegistry registry, List<Building> buildings)
        {
            var farmer = FarmerRegistry.Primary;
            if (farmer == null || data.Farmer == null) return;

            var save = data.Farmer;

            var traits = farmer.Traits;
            if (traits != null) traits.RestoreState(save.Traits);

            var skills = farmer.Skills;
            if (skills != null) skills.RestoreState(save.SkillLevels, save.SkillXp);

            var needs = farmer.GetComponent<CharacterNeeds>();
            if (needs != null) needs.RestoreState(save.Satiety01, save.Hydration01, save.Energy01);

            if (farmer.Inventory is Inventory pack)
            {
                pack.Clear();
                pack.RestoreState(save.Pack, registry.Resource);
            }

            // Часы найма — те же unix-часы, что растят грядки: наём честно тикает и без нас.
            farmer.RestoreHired(save.HiredUntilUnix);

            foreach (var built in data.Built)
            {
                var definition = registry.Improvement(built.ImprovementId);
                if (definition != null) farmer.RestoreBuilt(definition, built.Count);
            }

            foreach (var yard in data.Yards)
            {
                if (yard.BuildingIndex < 0 || yard.BuildingIndex >= buildings.Count) continue;

                var anchor = buildings[yard.BuildingIndex];
                var definition = registry.Improvement(yard.ImprovementId);
                if (anchor != null && definition != null)
                    farmer.RestoreBuiltYard(anchor, definition, yard.Count);
            }

            // Последним: ставит на место и возвращает автомат в покой, когда всё вокруг уже собрано.
            farmer.RestorePlacement(save.Position, save.Yaw);
        }
    }
}

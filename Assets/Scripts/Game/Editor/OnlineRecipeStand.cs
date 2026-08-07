using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Стенд производственных цепочек: ставит станки, кладёт сырьё и сам смотрит, дошло ли
    /// дело до готового продукта.
    /// <para>
    /// Существует потому, что честная проверка стоит часов: ветряк и кухню надо купить,
    /// пшеницу с тыквой вырастить в реальном времени (5 минут и полтора часа), кухню поднять
    /// до третьего уровня. Цепочка при этом крутится за минуту — проверять нечего, ждать
    /// нечего, а вот дойти до неё дорого. Стенд убирает именно дорогу, ничего не подменяя:
    /// работают настоящие постройки, настоящий склад и настоящий планировщик партий.
    /// </para>
    /// <para>
    /// Отчёт печатается сам: ход партий ловится подпиской на <c>EditorApplication.update</c>,
    /// а не «посмотри в консоль сам» — проверка, требующая толкования, проверкой не является.
    /// </para>
    /// </summary>
    public static class OnlineRecipeStand
    {
        /// <summary>Сколько секунд ждать готовую цепочку, прежде чем признать её застрявшей.</summary>
        private const double Patience = 90.0;

        /// <summary>Что кладём на склад. С запасом: партий на пути к пирогу несколько.</summary>
        private static readonly (string Id, int Amount)[] Supplies =
        {
            ("Wheat", 40), ("eggs", 20), ("pumpkin", 20), ("honey", 10),
        };

        /// <summary>Чего ждём и в каком порядке — цепочка мука → хлеб → пирог.</summary>
        private static readonly string[] Wanted = { "flour", "bread", "pie" };

        private static double _startedAt;
        private static readonly HashSet<string> _seen = new HashSet<string>();

        [MenuItem("Farm/Отладка/Рецепты: прогнать цепочку")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Стенд рецептов",
                    "Нужен режим Play: партии ведёт планировщик, а он живёт только в игре.\n\n" +
                    "Включи Play (лучше на сцене FarmingTest — она без сети и сохранения) " +
                    "и запусти пункт снова.", "Понял");
                return;
            }

            // Склад заводим сами, если его нет: FarmingTest — голая сцена с парой грядок,
            // а гонять стенд на живой партии ради одного WorldStorage значит рисковать сейвом.
            var storage = FarmingRuntime.Sink as IInventory;
            if (storage == null)
            {
                var host = new GameObject("Склад (стенд)");
                host.AddComponent<WorldStorage>();

                storage = FarmingRuntime.Sink as IInventory;
                if (storage == null)
                {
                    Debug.LogError("[Стенд] Склад не завёлся — класть сырьё некуда.");
                    return;
                }

                Debug.Log("[Стенд] Склада в сцене не было — поставил свой.");
            }

            // Предупреждение не формальность: в FarmerDemo с загруженной партией стенд
            // положит сырьё и поставит постройки в ЖИВОЕ сохранение, и автосейв увезёт это
            // на сервер. Молча менять чужую ферму отладочный инструмент не вправе.
            if (!EditorUtility.DisplayDialog("Стенд рецептов",
                    "Стенд положит сырьё на склад и поставит ветряк с кухней.\n\n" +
                    "Если это партия с сохранением (FarmerDemo онлайн), изменения уедут " +
                    "в сейв. На FarmingTest — ничего не сохраняется.\n\nПродолжить?",
                    "Ставить", "Отмена"))
                return;

            var windmill = Definition("windmill");
            var kitchen = Definition("kitchen");
            if (windmill == null || kitchen == null)
            {
                Debug.LogError("[Стенд] Не найдены определения построек windmill/kitchen.");
                return;
            }

            // Ветряк сразу последнего уровня: его Output — сила ауры, и она же делит время
            // партии (BatchSeconds), поэтому на первом уровне (0.2) помол идёт впятеро
            // дольше — 30 секунд вместо шести. В игре это честно («слабый ветер мелет
            // медленно»), а проверке ни к чему.
            // Кухня третьего: пирог открывается там, и цепочку интереснее видеть до вершины.
            Place(windmill, windmill.MaxLevel, new Vector3(-3f, 0f, 0f));
            Place(kitchen, 3, new Vector3(3f, 0f, 0f));

            var report = new StringBuilder("[Стенд] Сырьё:");
            foreach (var (id, amount) in Supplies)
            {
                var resource = Resource(id);
                if (resource == null)
                {
                    Debug.LogWarning("[Стенд] Нет ресурса " + id + " — цепочка может не пойти.");
                    continue;
                }

                int added = storage.TryAdd(resource, amount);
                report.Append(' ').Append(resource.DisplayName).Append('×').Append(added);
            }

            Debug.Log(report.ToString());

            _seen.Clear();
            _startedAt = FarmingRuntime.Now;

            EditorApplication.update -= Watch;
            EditorApplication.update += Watch;

            Debug.Log("[Стенд] Пошло. Жду до " + Patience + " с — отчёт напечатается сам. " +
                      "Не хочется ждать — «Рецепты: промотать минуту».");
        }

        [MenuItem("Farm/Отладка/Рецепты: промотать минуту")]
        public static void Skip()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Стенд] Промотка работает только в Play.");
                return;
            }

            Farm.Net.ServerClock.DebugAdvance(60.0);
            Debug.Log("[Стенд] Часы фермы сдвинуты на минуту вперёд.");
        }

        /// <summary>Что сейчас на складе и что крутится в станках — снимок по требованию.</summary>
        [MenuItem("Farm/Отладка/Рецепты: показать состояние")]
        public static void Show() => Debug.Log(State());

        /// <summary>
        /// Выхаживание грядок «задним числом»: та же проверка честным путём стоит девяти
        /// циклов полива с ведром из колодца — часы реального времени ради одного числа.
        /// </summary>
        [MenuItem("Farm/Отладка/Сорт: выходить грядки и дорастить")]
        public static void Pamper()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Стенд] Только в Play: ухоженность живёт на грядках сцены.");
                return;
            }

            var plots = GrowableRegistry.All;
            int touched = 0;

            for (int i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                if (plot == null || plot.Phase == GrowthPhase.Empty) continue;

                // Полита и накормлена: ухоженность считается поливом у растений и кормом
                // у скотины, и ставить надо оба флага, иначе половина фермы останется серой.
                plot.RestoreCare(true, plot.Fertilized, Growable.PrimeGradeStreak, plot.CycleId, true);
                touched++;
            }

            // Час вперёд — чтобы было что собирать: без спелости сорт негде увидеть.
            Farm.Net.ServerClock.DebugAdvance(3600.0);

            Debug.Log("[Стенд] Выхожено грядок: " + touched + " (★★★, следующий сбор — призовой). " +
                      "Часы сдвинуты на час: собери спелое кликом и загляни на склад.");
        }

        private static void Watch()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= Watch;
                return;
            }

            var storage = FarmingRuntime.Sink as IInventory;
            if (storage == null) return;

            foreach (var id in Wanted)
            {
                var resource = Resource(id);
                if (resource == null || storage.GetAmount(resource) <= 0) continue;
                if (!_seen.Add(id)) continue;

                Debug.Log("[Стенд] ✓ пошёл " + resource.DisplayName +
                          " (" + (FarmingRuntime.Now - _startedAt).ToString("0") + " с)");
            }

            bool done = _seen.Count >= Wanted.Length;
            if (!done && FarmingRuntime.Now - _startedAt < Patience) return;

            EditorApplication.update -= Watch;

            if (done)
            {
                Debug.Log("[Стенд] ЦЕПОЧКА ПРОШЛА: мука → хлеб → пирог.\n" + State());
                return;
            }

            // Провал печатается вместе с состоянием: «не получилось» без причины отправляет
            // искать наугад, а станки как раз умеют назвать недостающее.
            Debug.LogWarning("[Стенд] ЦЕПОЧКА НЕ ДОШЛА за " + Patience + " с. Дошло: " +
                             (_seen.Count == 0 ? "ничего" : string.Join(", ", _seen)) + "\n" + State());
        }

        private static string State()
        {
            var text = new StringBuilder("[Стенд] Склад:");
            var storage = FarmingRuntime.Sink as IInventory;

            if (storage != null)
                foreach (var id in new[] { "Wheat", "eggs", "pumpkin", "honey", "flour", "bread", "pie" })
                {
                    var resource = Resource(id);
                    if (resource == null) continue;

                    text.Append(' ').Append(resource.DisplayName).Append('=').Append(storage.GetAmount(resource));

                    // Сорта — отдельно: общее число не покажет, дошёл ли уход до товара,
                    // а это ровно то, что проверяется.
                    foreach (var grade in ResourceGrades.All)
                    {
                        if (grade == ResourceGrade.Common) continue;

                        int amount = storage.GetAmount(resource, grade);
                        if (amount > 0) text.Append('(').Append(ResourceGrades.Mark(grade)).Append(amount).Append(')');
                    }
                }

            text.Append("\n[Стенд] Станки:");
            var buildings = BuildingRegistry.All;
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                var workshop = building != null ? building.GetComponent<Workshop>() : null;
                if (workshop == null) continue;

                text.Append("\n  ").Append(building.Definition != null ? building.Definition.DisplayName : "?")
                    .Append(" L").Append(building.Level).Append(": ");

                if (workshop.IsWorking)
                {
                    text.Append(workshop.Running)
                        .Append(" — ").Append((workshop.Progress01 * 100f).ToString("0")).Append('%');
                }
                else
                {
                    var missing = workshop.MissingInput();
                    text.Append(missing != null ? "стоит, нужно: " + missing.DisplayName : "стоит без сырья");
                }
            }

            return text.ToString();
        }

        /// <summary>Поставить постройку нужного уровня — или поднять уже стоящую.</summary>
        private static void Place(BuildingDefinition definition, int level, Vector3 offset)
        {
            var all = BuildingRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || all[i].Definition != definition) continue;

                // Уже стоит: не плодим второй экземпляр, только доводим уровень до нужного.
                if (all[i].Level < level) all[i].Configure(definition, level);
                Debug.Log("[Стенд] " + definition.DisplayName + " уже на ферме, уровень " + all[i].Level);
                return;
            }

            if (definition.Prefab == null)
            {
                Debug.LogError("[Стенд] У " + definition.Id + " нет префаба — ставить нечего.");
                return;
            }

            var spot = FarmBounds.ClampToFarm(offset);
            spot.y = FarmingRuntime.Ground.SampleHeight(spot);

            var instance = Object.Instantiate(definition.Prefab, spot, Quaternion.identity);
            instance.name = "Building_" + definition.Id;

            var building = instance.GetComponent<Building>() ?? instance.AddComponent<Building>();
            building.Configure(definition, level);

            Debug.Log("[Стенд] Поставлен " + definition.DisplayName + " уровня " + level);
        }

        private static BuildingDefinition Definition(string id) =>
            AssetDatabase.FindAssets("t:BuildingDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<BuildingDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(d => d != null && d.Id == id);

        private static ResourceDefinition Resource(string id) =>
            AssetDatabase.FindAssets("t:ResourceDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ResourceDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(r => r != null && r.Id == id);
    }
}

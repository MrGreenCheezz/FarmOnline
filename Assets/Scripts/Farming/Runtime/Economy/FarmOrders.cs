using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Заказ доски в сохранении: доска застыла при генерации (стабильность против живого
    /// пула), и застывшее обязано переживать перезаход. Ресурсы — по id, снисходительно.
    /// </summary>
    [Serializable]
    public sealed class OrderSave
    {
        public string Id;
        public string Customer;
        public int Gold;
        public double Expires;
        public string[] Resources;
        public int[] Amounts;
    }

    /// <summary>Одна строка заказа: чего и сколько просят.</summary>
    public readonly struct OrderLine
    {
        public readonly ResourceDefinition Resource;
        public readonly int Amount;

        public OrderLine(ResourceDefinition resource, int amount)
        {
            Resource = resource;
            Amount = amount;
        }

        public bool IsValid => Resource != null && Amount > 0;
    }

    /// <summary>
    /// Заказ горожанина: список товаров и награда за всё сразу.
    /// <para>
    /// Заказы генерируются из времени и того, что игрок умеет производить, а с 07.08.2026
    /// застывшая доска ещё и ХРАНИТСЯ в сейве: живой пул менялся от каждой пересадки и
    /// молча перекатывал несданные заказы. Сервер генератор по-прежнему не помнит.
    /// </para>
    /// </summary>
    public sealed class FarmOrder
    {
        public string Id { get; }
        public string Customer { get; }
        public IReadOnlyList<OrderLine> Lines { get; }
        public int Gold { get; }

        /// <summary>Когда заказ пропадёт с доски (unix-секунды игровых часов).</summary>
        public double ExpiresAt { get; }

        public FarmOrder(string id, string customer, IReadOnlyList<OrderLine> lines, int gold, double expiresAt)
        {
            Id = id;
            Customer = customer;
            Lines = lines;
            Gold = gold;
            ExpiresAt = expiresAt;
        }

        /// <summary>Всё ли лежит на складе.</summary>
        public bool CanFill(IInventory storage)
        {
            if (storage == null) return false;

            foreach (var line in Lines)
                if (!line.IsValid || storage.GetAmount(line.Resource) < line.Amount) return false;

            return true;
        }

        /// <summary>Сколько единиц уже есть из запрошенных — для полосы прогресса.</summary>
        public int Have(IInventory storage, int lineIndex)
        {
            if (storage == null || lineIndex < 0 || lineIndex >= Lines.Count) return 0;

            var line = Lines[lineIndex];
            return line.IsValid ? Mathf.Min(storage.GetAmount(line.Resource), line.Amount) : 0;
        }
    }

    /// <summary>
    /// Доска заказов: несколько горожан просят товары, платят золотом сверх лавочной цены.
    /// <para>
    /// Зачем это игре: продажа в лавку — фон, который не требует решений. Заказ даёт цель на
    /// вечер («нужно ещё 12 пшеницы») и повод растить то, что сам бы не стал. Наценка — плата
    /// за неудобство: заказ просит конкретное и в конкретном количестве.
    /// </para>
    /// <para>
    /// Генерация детерминированная: номер окна времени + номер места на доске дают зерно.
    /// Сохраняются и выполненные, и сама застывшая доска (см. Cache): доска не имеет права
    /// перекатываться от пересадок, а длинные заказы — доживать только в памяти процесса.
    /// </para>
    /// </summary>
    public static class FarmOrders
    {
        /// <summary>Сколько заказов висит на доске одновременно.</summary>
        // Слоты растут с уровнем игрока: больше заказов — больше и подкормки, и опыта,
        // так что это ровно та награда за уровень, которая кормит следующий уровень.
        // Нанятый возчик держит ещё один: его ценность — пропускная способность заказов
        // (демаркация ролей, CLAUDE.md), а не золото из воздуха.
        // Пороги 20 и 35 — продолжение лестницы (этап 8): уровни 13–60 не давали ничего,
        // а полоса опыта продолжала наполняться — теперь длинной прогрессии есть за что расти.
        public static int SlotCount =>
            3 + (FarmExperience.Level >= 6 ? 1 : 0) + (FarmExperience.Level >= 12 ? 1 : 0)
              + (FarmExperience.Level >= 20 ? 1 : 0) + (FarmExperience.Level >= 35 ? 1 : 0)
              + (CarrierActive ? 1 : 0);

        /// <summary>
        /// Метка нанятого возчика — по образцу метки мастерового у станка: продлевается
        /// каждый тик его агента и протухает сама, когда наём кончился или возчик пропал.
        /// Сборка заказов жителей не знает, ей хватает факта «возчик в деле».
        /// </summary>
        private static double _carrierUntil;

        public static void StampCarrier(double holdSeconds = 3.0) =>
            _carrierUntil = FarmingRuntime.Now + holdSeconds;

        public static bool CarrierActive => FarmingRuntime.Now < _carrierUntil;

        /// <summary>Заказ сдан и оплачен. Слушает возчик: повод отвезти короб к рынку.</summary>
        public static event Action<FarmOrder> FilledOrder;

        /// <summary>
        /// Сколько живёт одно окно заказов. Шесть часов — чтобы доска обновлялась к каждому
        /// заходу в игру, но не успевала протухнуть за время сбора многочасовой культуры.
        /// </summary>
        public const double WindowSeconds = 6.0 * 3600.0;

        /// <summary>
        /// Во сколько раз заказ платит больше простой продажи. Меньше — и заказ бессмыслен,
        /// сильно больше — и продажа в лавку перестаёт существовать как способ жить.
        /// </summary>
        private const float RewardFactor = 1.75f;

        /// <summary>Имена заказчиков. Лица деревни: заказ от «горожанина №2» не запоминается.</summary>
        private static readonly string[] Customers =
        {
            "мельник Прохор", "травница Аглая", "кузнец Богдан", "пекарь Марта",
            "трактирщик Савва", "рыбак Тихон", "ткачиха Устинья", "плотник Никифор",
        };

        /// <summary>Сданные заказы этого окна — по ним доска знает, что уже закрыто.</summary>
        private static readonly HashSet<string> Filled = new HashSet<string>();

        /// <summary>Доска изменилась: сдали заказ или сменилось окно времени.</summary>
        public static event Action Changed;

        /// <summary>Номер текущего окна времени. Он же — половина зерна генерации.</summary>
        public static long Window => (long)(FarmingRuntime.Now / WindowSeconds);

        /// <summary>Сколько секунд осталось до смены доски.</summary>
        public static double SecondsLeft => (Window + 1) * WindowSeconds - FarmingRuntime.Now;

        public static bool IsFilled(string orderId) => Filled.Contains(orderId);

        /// <summary>
        /// Доска этого окна, ЗАСТЫВШАЯ при генерации. Раньше доска пересобиралась из живого
        /// пула на каждый вызов — пересадка или покупка грядки молча перекатывала несданные
        /// заказы (бесплатный перевыбор), а сбор последней пшеницы уносил заказ на неё
        /// (аудит 06.08.2026). Цена стабильности: две вкладки без общего сейва могут
        /// разойтись при доживающих длинных заказах — сейв их синхронизирует.
        /// </summary>
        private static readonly List<FarmOrder> Cache = new List<FarmOrder>();
        private static long _cacheWindow = long.MinValue;

        /// <summary>Окно, под которое собран кэш, — для сохранения.</summary>
        public static long CacheWindow => _cacheWindow;

        /// <summary>Собрать доску на сейчас: первые <see cref="SlotCount"/> заказов кэша.</summary>
        public static List<FarmOrder> Board()
        {
            RefreshCache();

            int show = Mathf.Min(SlotCount, Cache.Count);
            var board = new List<FarmOrder>(show);
            for (int i = 0; i < show; i++) board.Add(Cache[i]);
            return board;
        }

        private static void RefreshCache()
        {
            long window = Window;
            double now = FarmingRuntime.Now;
            bool changed = false;

            if (_cacheWindow != window)
            {
                // Смена окна: сданные уходят вместе с окном, длинные несданные (их срок
                // дышит временем роста) доживают своё на прежних местах.
                changed |= Cache.RemoveAll(o => o == null || o.ExpiresAt <= now || Filled.Contains(o.Id)) > 0;
                _cacheWindow = window;
                changed = true;
            }
            else
            {
                // И посреди окна: длинный заказ, чей срок истёк между границами, не имеет
                // права висеть сдаваемым — TryFill его срок не проверяет. Сданные висят
                // со штампом «СДАНО» до смены окна, их не трогаем.
                changed |= Cache.RemoveAll(o => o == null ||
                    (o.ExpiresAt <= now && !Filled.Contains(o.Id))) > 0;
            }

            if (Cache.Count < SlotCount)
            {
                var pool = AffordablePool();
                if (pool.Count > 0)
                {
                    // Докидка по СВОБОДНЫМ номерам мест: слепое slot = Cache.Count после
                    // перезахода строило бы Id, который уже висит на доске или уже сдан
                    // (дубли — блокер судей этапа 6). Слот возчика при протухании найма
                    // заказ не выкидывает — тот прячется за SlotCount до следующего найма.
                    var taken = new HashSet<string>();
                    foreach (var order in Cache) taken.Add(order.Id);

                    for (int slot = 0; Cache.Count < SlotCount && slot < SlotCount * 4; slot++)
                    {
                        string id = window + ":" + slot;
                        if (taken.Contains(id) || Filled.Contains(id)) continue;

                        var order = Build(window, slot, pool);
                        if (order == null) break;
                        Cache.Add(order);
                        taken.Add(order.Id);
                        changed = true;
                    }
                }
            }

            // Raise только на фактическом изменении: Board() зовут из перерисовки доски,
            // и безусловный Raise зациклил бы Changed → Rebuild → Board.
            if (changed) Raise();
        }

        /// <summary>Снимок доски для сохранения — застывшие заказы должны переживать перезаход.</summary>
        public static OrderSave[] CaptureBoard()
        {
            RefreshCache();

            var saved = new OrderSave[Cache.Count];
            for (int i = 0; i < Cache.Count; i++)
            {
                var order = Cache[i];
                var save = new OrderSave
                {
                    Id = order.Id,
                    Customer = order.Customer,
                    Gold = order.Gold,
                    Expires = order.ExpiresAt,
                    Resources = new string[order.Lines.Count],
                    Amounts = new int[order.Lines.Count],
                };
                for (int j = 0; j < order.Lines.Count; j++)
                {
                    save.Resources[j] = order.Lines[j].Resource != null ? order.Lines[j].Resource.Id : "";
                    save.Amounts[j] = order.Lines[j].Amount;
                }
                saved[i] = save;
            }
            return saved;
        }

        /// <summary>
        /// Вернуть доску из сохранения. Заказ со ссылкой на выпавший из каталога ресурс
        /// молча не воскресает — его место займёт свежий (докидка в RefreshCache), с логом:
        /// откат контента не должен ронять всю доску.
        /// </summary>
        public static void RestoreBoard(long window, OrderSave[] saved, Func<string, ResourceDefinition> resolve)
        {
            Cache.Clear();
            _cacheWindow = window;
            if (saved == null || resolve == null) return;

            double now = FarmingRuntime.Now;
            foreach (var save in saved)
            {
                if (save == null || save.Resources == null || save.Amounts == null) continue;

                // Сданные НЕ выбрасываются: живьём они висят со штампом «СДАНО» до смены
                // окна, и перезаход обязан выглядеть так же — выброс сдвигал бы слоты и
                // рождал дубли Id при докидке (блокер судей этапа 6). Уходит только
                // протухшее: несданное — молча, сданное — вместе со своим окном.
                if (save.Expires <= now && !Filled.Contains(save.Id)) continue;

                var lines = new List<OrderLine>(save.Resources.Length);
                bool broken = false;
                for (int i = 0; i < save.Resources.Length && i < save.Amounts.Length; i++)
                {
                    var resource = resolve(save.Resources[i]);
                    if (resource == null) { broken = true; break; }
                    lines.Add(new OrderLine(resource, save.Amounts[i]));
                }

                if (broken || lines.Count == 0)
                {
                    Debug.LogWarning("[Заказы] Заказ '" + save.Id + "' ссылается на пропавший ресурс — заменён свежим");
                    continue;
                }

                Cache.Add(new FarmOrder(save.Id, save.Customer, lines, Mathf.Max(1, save.Gold), save.Expires));
            }
        }

        /// <summary>
        /// Сдать заказ: снять товары со склада, выдать золото. Отказ объясняется строкой —
        /// молчаливое «ничего не произошло» здесь читалось бы как поломка.
        /// <paramref name="fertilizerGranted"/> — сколько подкормки реально легло (при полном
        /// запасе — ноль, и об этом говорит показывающий сдачу).
        /// </summary>
        public static bool TryFill(FarmOrder order, out string refusal, out int fertilizerGranted)
        {
            refusal = null;
            fertilizerGranted = 0;

            if (order == null) { refusal = "заказа больше нет"; return false; }

            if (GuestMode.IsGuest)
            {
                refusal = "в гостях заказы не сдают — это чужой склад";
                return false;
            }

            if (Filled.Contains(order.Id)) { refusal = "этот заказ уже сдан"; return false; }

            var storage = FarmingRuntime.Sink as IInventory;
            if (storage == null) { refusal = "склад недоступен"; return false; }

            if (!order.CanFill(storage))
            {
                refusal = "на складе не всё, что просят";
                return false;
            }

            foreach (var line in order.Lines)
                storage.TryRemove(line.Resource, line.Amount);

            var wallet = Wallet.Instance;
            if (wallet != null) wallet.Add(order.Gold);

            // Подкормка платится за дело, а не за место: вода идёт из колодца сама по себе,
            // а это — награда тому, кто собрал заказ и донёс его до горожан. Сколько реально
            // легло — наружу: при полном запасе награда упирается в потолок, и говорить об
            // этом обязан тот, кто показывает сдачу, по факту, а не по догадке.
            fertilizerGranted = FarmFertilizer.Grant(FarmFertilizer.PerOrder);
            FarmExperience.Add(FarmExperience.PerOrder);

            Filled.Add(order.Id);
            FarmAchievements.NoteOrderFilled();

            // Исключение слушателя не должно ломать сдачу — награда уже выдана.
            var filled = FilledOrder;
            if (filled != null)
            {
                try { filled(order); }
                catch (Exception e) { Debug.LogException(e); }
            }

            Raise();
            return true;
        }

        /// <summary>Что сдано — для сохранения. Заказы прошлых окон отсеются сами при чтении.</summary>
        public static string[] CaptureFilled()
        {
            var list = new string[Filled.Count];
            Filled.CopyTo(list);
            return list;
        }

        /// <summary>
        /// Вернуть сданное из сохранения. Держим отметки текущего окна И всего, что упомянуто
        /// в сохранённой доске: длинные заказы-долгожители носят Id прошлых окон, и резать
        /// по одному лишь префиксу значило бы воскрешать их несданными на каждом перезаходе
        /// (повторная сдача за полную цену — блокер судей этапа 6). Остальное отбрасываем:
        /// копить чужие окна вечно — растить сейв на пустом месте.
        /// </summary>
        public static void RestoreState(string[] filled, OrderSave[] board = null)
        {
            Filled.Clear();
            if (filled == null) return;

            var boardIds = new HashSet<string>();
            if (board != null)
                foreach (var save in board)
                    if (save != null && !string.IsNullOrEmpty(save.Id)) boardIds.Add(save.Id);

            string prefix = Window.ToString() + ":";
            foreach (var id in filled)
                if (!string.IsNullOrEmpty(id) &&
                    (id.StartsWith(prefix, StringComparison.Ordinal) || boardIds.Contains(id)))
                    Filled.Add(id);

            Raise();
        }

        // ---- генерация ----

        /// <summary>
        /// Из чего вообще составлять заказ: то, что игрок умеет добывать. Иначе горожанин
        /// попросит звёздный металл у того, кто растит пшеницу, и доска станет издевательством.
        /// </summary>
        private static List<PoolItem> AffordablePool()
        {
            var pool = new List<PoolItem>();
            var seen = new HashSet<ResourceDefinition>();

            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
            {
                var definition = plots[i] != null ? plots[i].Definition : null;
                var resource = definition != null ? definition.YieldResource : null;
                if (resource == null || resource.SellPrice <= 0) continue;
                if (seen.Add(resource))
                    pool.Add(new PoolItem(resource, definition.TotalGrowTime));
            }

            // Продукты станков — тоже спрос: горожане просят и доски со слитками, если
            // на ферме стоит станок с открытым рецептом И сырьё рецепта реально доступно:
            // лежит на складе или растёт на грядке. Без второй половины гарантия «не
            // попросят недоступное» дырявилась бы апгрейдом станка за золото — рецепт
            // открыт уровнем ПОСТРОЙКИ, а грядка сырья заперта уровнем ИГРОКА (судья
            // этапа 6). Это единственный постоянный сток второго ряда переработки.
            // GrowSeconds — от источника сырья: срок заказа обязан дышать и у станков.
            var storage = FarmingRuntime.Sink as IInventory;
            var buildings = BuildingRegistry.All;
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                var recipes = building != null && building.Definition != null ? building.Definition.Recipes : null;
                if (recipes == null) continue;

                foreach (var recipe in recipes)
                {
                    if (recipe == null || !recipe.IsValid || building.Level < recipe.UnlockLevel) continue;
                    var output = recipe.Output;
                    if (output.SellPrice <= 0 || seen.Contains(output)) continue;

                    // Составной рецепт годится, только когда доступна КАЖДАЯ его строка:
                    // одной муки для хлеба мало, а заказ на недоступное — сломанное обещание.
                    double inputGrow = 0.0;
                    bool anyGrowing = false;
                    bool allAvailable = true;

                    int lines = recipe.InputCount;
                    for (int line = 0; line < lines && allAvailable; line++)
                    {
                        var need = recipe.InputResourceAt(line);
                        bool inStock = storage != null && storage.GetAmount(need) >= recipe.InputAmountAt(line);

                        bool growing = false;
                        for (int p = 0; p < plots.Count && !growing; p++)
                        {
                            var d = plots[p] != null ? plots[p].Definition : null;
                            if (d == null || d.YieldResource != need) continue;

                            growing = true;
                            anyGrowing = true;

                            // Срок дышит по самой медленной строке: заказ на хлеб не может
                            // жить короче, чем растёт пшеница для муки.
                            if (d.TotalGrowTime > inputGrow) inputGrow = d.TotalGrowTime;
                        }

                        if (!inStock && !growing) allAvailable = false;
                    }

                    if (!allAvailable) continue;

                    seen.Add(output);
                    pool.Add(new PoolItem(output, anyGrowing ? inputGrow : 0.0));
                }
            }

            // Порядок обхода реестров — не наше дело: они живые и меняются от пересадок.
            // Без сортировки одно и то же окно давало бы разные заказы после перезахода.
            pool.Sort((a, b) => string.CompareOrdinal(a.Resource.Id, b.Resource.Id));
            return pool;
        }

        /// <summary>Кандидат пула: ресурс и сколько секунд растёт его источник (0 — станок).</summary>
        private readonly struct PoolItem
        {
            public readonly ResourceDefinition Resource;
            public readonly double GrowSeconds;
            public PoolItem(ResourceDefinition resource, double growSeconds)
            {
                Resource = resource;
                GrowSeconds = growSeconds;
            }
        }

        private static FarmOrder Build(long window, int slot, List<PoolItem> pool)
        {
            // Своё зерно на каждое место доски: одно окно — одна и та же тройка заказов
            // у всех вкладок и после любого перезахода.
            var random = new System.Random(unchecked((int)(window * 7919) + slot * 104729));

            int lineCount = pool.Count >= 2 && random.Next(100) < 55 ? 2 : 1;
            var lines = new List<OrderLine>(lineCount);
            var used = new HashSet<ResourceDefinition>();
            double slowest = 0.0;

            for (int i = 0; i < lineCount; i++)
            {
                var item = pool[random.Next(pool.Count)];
                if (!used.Add(item.Resource)) continue;

                // Просят тем меньше, чем дороже ресурс: восемь досок — работа на вечер,
                // восемь слитков звёздного металла — на неделю.
                int baseAmount = Mathf.Clamp(Mathf.RoundToInt(40f / Mathf.Max(1, item.Resource.SellPrice)), 3, 25);
                int amount = Mathf.Max(2, baseAmount + random.Next(-2, 3));
                lines.Add(new OrderLine(item.Resource, amount));
                slowest = System.Math.Max(slowest, item.GrowSeconds);
            }

            if (lines.Count == 0) return null;

            int gold = 0;
            foreach (var line in lines)
                gold += Mathf.RoundToInt(line.Resource.SellPrice * line.Amount * RewardFactor);

            // Срок дышит вместе с ростом: заказ на 12-часовую культуру, живущий 6 часов,
            // был бы невыполним с нуля по построению — даём минимум два цикла роста.
            // Отсчёт от НАЧАЛА окна: генерация обязана оставаться детерминированной.
            double life = System.Math.Max(WindowSeconds, 2.0 * slowest);
            double expires = window * WindowSeconds + life;

            string id = window + ":" + slot;
            string customer = Customers[random.Next(Customers.Length)];
            return new FarmOrder(id, customer, lines, Mathf.Max(1, gold), expires);
        }

        private static void Raise()
        {
            var handler = Changed;
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload: без сброса
        // вторая партия унаследовала бы сданные заказы первой.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            Filled.Clear();
            Cache.Clear();
            _cacheWindow = long.MinValue;
            Changed = null;
            FilledOrder = null;
            _carrierUntil = 0.0;
        }
    }
}

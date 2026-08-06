using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
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
    /// Заказы не хранятся и не приходят с сервера — они <b>выводятся</b> из времени и из того,
    /// что игрок умеет растить. Поэтому две вкладки видят одну доску без всякой синхронизации,
    /// а сервер не обязан помнить генератор.
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
    /// Генерация детерминированная: номер окна времени + номер места на доске дают зерно, из
    /// которого вырастает один и тот же заказ у всех вкладок игрока. Хранить в сохранении нужно
    /// только <b>выполненные</b> — то, что уже сдано и оплачено.
    /// </para>
    /// </summary>
    public static class FarmOrders
    {
        /// <summary>Сколько заказов висит на доске одновременно.</summary>
        // Слоты растут с уровнем игрока: больше заказов — больше и подкормки, и опыта,
        // так что это ровно та награда за уровень, которая кормит следующий уровень.
        // Нанятый возчик держит ещё один: его ценность — пропускная способность заказов
        // (демаркация ролей, CLAUDE.md), а не золото из воздуха.
        public static int SlotCount =>
            3 + (FarmExperience.Level >= 6 ? 1 : 0) + (FarmExperience.Level >= 12 ? 1 : 0)
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
        /// Собрать доску на сейчас. Пересобирается каждый раз, а не кэшируется: окно времени
        /// меняется само по себе, и кэш пришлось бы сторожить таймером ради трёх строчек.
        /// </summary>
        public static List<FarmOrder> Board()
        {
            var board = new List<FarmOrder>(SlotCount);

            var pool = AffordablePool();
            if (pool.Count == 0) return board;

            long window = Window;
            double expires = (window + 1) * WindowSeconds;

            for (int slot = 0; slot < SlotCount; slot++)
            {
                var order = Build(window, slot, pool, expires);
                if (order != null) board.Add(order);
            }

            return board;
        }

        /// <summary>
        /// Сдать заказ: снять товары со склада, выдать золото. Отказ объясняется строкой —
        /// молчаливое «ничего не произошло» здесь читалось бы как поломка.
        /// </summary>
        public static bool TryFill(FarmOrder order, out string refusal)
        {
            refusal = null;

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
            // а это — награда тому, кто собрал заказ и донёс его до горожан.
            FarmFertilizer.Grant(FarmFertilizer.PerOrder);
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
        /// Вернуть сданное из сохранения. Идентификаторы чужих окон отбрасываем: доска давно
        /// другая, а копить их вечно — растить сейв на пустом месте.
        /// </summary>
        public static void RestoreState(string[] filled)
        {
            Filled.Clear();
            if (filled == null) return;

            string prefix = Window.ToString() + ":";
            foreach (var id in filled)
                if (!string.IsNullOrEmpty(id) && id.StartsWith(prefix, StringComparison.Ordinal))
                    Filled.Add(id);

            Raise();
        }

        // ---- генерация ----

        /// <summary>
        /// Из чего вообще составлять заказ: то, что игрок умеет добывать. Иначе горожанин
        /// попросит звёздный металл у того, кто растит пшеницу, и доска станет издевательством.
        /// </summary>
        private static List<ResourceDefinition> AffordablePool()
        {
            var pool = new List<ResourceDefinition>();
            var seen = new HashSet<ResourceDefinition>();

            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
            {
                var definition = plots[i] != null ? plots[i].Definition : null;
                var resource = definition != null ? definition.YieldResource : null;
                if (resource == null || resource.SellPrice <= 0) continue;
                if (seen.Add(resource)) pool.Add(resource);
            }

            // Порядок обхода реестра — не наше дело: он живой и меняется от пересадок.
            // Без сортировки одно и то же окно давало бы разные заказы после перезахода.
            pool.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return pool;
        }

        private static FarmOrder Build(long window, int slot, List<ResourceDefinition> pool, double expires)
        {
            // Своё зерно на каждое место доски: одно окно — одна и та же тройка заказов
            // у всех вкладок и после любого перезахода.
            var random = new System.Random(unchecked((int)(window * 7919) + slot * 104729));

            int lineCount = pool.Count >= 2 && random.Next(100) < 55 ? 2 : 1;
            var lines = new List<OrderLine>(lineCount);
            var used = new HashSet<ResourceDefinition>();

            for (int i = 0; i < lineCount; i++)
            {
                var resource = pool[random.Next(pool.Count)];
                if (!used.Add(resource)) continue;

                // Просят тем меньше, чем дороже ресурс: восемь досок — работа на вечер,
                // восемь слитков звёздного металла — на неделю.
                int baseAmount = Mathf.Clamp(Mathf.RoundToInt(40f / Mathf.Max(1, resource.SellPrice)), 3, 25);
                int amount = Mathf.Max(2, baseAmount + random.Next(-2, 3));
                lines.Add(new OrderLine(resource, amount));
            }

            if (lines.Count == 0) return null;

            int gold = 0;
            foreach (var line in lines)
                gold += Mathf.RoundToInt(line.Resource.SellPrice * line.Amount * RewardFactor);

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
            Changed = null;
            FilledOrder = null;
            _carrierUntil = 0.0;
        }
    }
}

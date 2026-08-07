using System;
using System.Collections.Generic;

namespace Farm.Farming
{
    /// <summary>Как инвентарь решает, что он полон.</summary>
    public enum InventoryCapacity
    {
        /// <summary>Без лимита. Склад мира начинает с этого.</summary>
        Unlimited = 0,
        /// <summary>Лимит по сумме единиц всех ресурсов. Грузоподъёмность персонажа.</summary>
        Units = 1,
        /// <summary>
        /// Лимит по числу ячеек. В ячейку влезает <see cref="IInventory.StackSize"/> единиц
        /// одного ресурса; что не влезло — занимает следующую. Ячейки как у сундука.
        /// <para>
        /// Ячейка меряет объём, а не разнообразие: сколько в игре видов ресурсов — неважно,
        /// новый ресурс ничего не отнимает у уже лежащих и не требует правки вместимости.
        /// </para>
        /// </summary>
        Slots = 2
    }

    /// <summary>
    /// Сорт единицы товара: что выросло на ухоженной земле, то и стоит дороже.
    /// <para>
    /// Сорт — поле записи склада, а не отдельный ассет: «отборная пшеница» отдельным
    /// ресурсом удвоила бы каталог с первым же сортом и утроила со вторым, а это ровно
    /// тот случай, когда контент начинает трогать системы.
    /// </para>
    /// <para>Значения сериализуются в сейв — добавлять только в конец.</para>
    /// </summary>
    public enum ResourceGrade
    {
        /// <summary>Обычный. Всё, что выросло без постоянного ухода.</summary>
        Common = 0,
        /// <summary>Отборный: земля ухожена несколько циклов подряд.</summary>
        Choice = 1,
        /// <summary>Призовой: уход не прерывался почти никогда.</summary>
        Prime = 2
    }

    /// <summary>Один ресурс, его сорт и сколько лежит. Неизменяемая — заменяй, а не правь.</summary>
    public readonly struct InventoryEntry
    {
        public readonly ResourceDefinition Resource;
        public readonly int Amount;

        /// <summary>Сорт этой стопки. Стопки разных сортов лежат порознь и не смешиваются.</summary>
        public readonly ResourceGrade Grade;

        public InventoryEntry(ResourceDefinition resource, int amount,
                              ResourceGrade grade = ResourceGrade.Common)
        {
            Resource = resource;
            Amount = amount;
            Grade = grade;
        }

        public InventoryEntry WithAmount(int amount) => new InventoryEntry(Resource, amount, Grade);

        public override string ToString() =>
            Amount + "x " + (Resource != null ? Resource.Id : "<none>") +
            (Grade == ResourceGrade.Common ? "" : " [" + ResourceGrades.Name(Grade) + "]");
    }

    /// <summary>Что сорт значит для игры: имя, значок и во сколько раз он дороже обычного.</summary>
    public static class ResourceGrades
    {
        /// <summary>
        /// Надбавка сорта к цене. Числа скромные нарочно: сорт — награда за постоянство ухода,
        /// а не второй множитель дохода поверх прибавки в штуках, которую уход и так даёт.
        /// </summary>
        public static float PriceFactor(ResourceGrade grade)
        {
            switch (grade)
            {
                case ResourceGrade.Choice: return 1.4f;
                case ResourceGrade.Prime: return 2f;
                default: return 1f;
            }
        }

        public static string Name(ResourceGrade grade)
        {
            switch (grade)
            {
                case ResourceGrade.Choice: return "отборный";
                case ResourceGrade.Prime: return "призовой";
                default: return "обычный";
            }
        }

        /// <summary>Значок для списков: звёзды читаются боковым зрением, слово — нет.</summary>
        public static string Mark(ResourceGrade grade)
        {
            switch (grade)
            {
                case ResourceGrade.Choice: return "★";
                case ResourceGrade.Prime: return "★★";
                default: return "";
            }
        }

        /// <summary>Все сорта от обычного к лучшему — чтобы порядок обхода жил в одном месте.</summary>
        public static readonly ResourceGrade[] All =
        {
            ResourceGrade.Common, ResourceGrade.Choice, ResourceGrade.Prime
        };
    }

    /// <summary>
    /// Контейнер ресурсов. Его реализует всё, что что-то хранит — сегодня рюкзак персонажа,
    /// позже амбары, силосы и сундуки — чтобы перенос, привязка UI и сохранения писались
    /// один раз против интерфейса, а не на каждый контейнер.
    /// <para>
    /// Расширяет <see cref="IResourceSink"/>, поэтому любой инвентарь можно передать прямо
    /// в <see cref="Growable.TryHarvest(out HarvestResult, IResourceSink)"/> как приёмник урожая.
    /// </para>
    /// </summary>
    public interface IInventory : IResourceSink
    {
        /// <summary>Как контейнер решает, что полон. UI нужно это, чтобы знать, что рисовать.</summary>
        InventoryCapacity CapacityMode { get; }

        /// <summary>Единицы или ячейки — смысл зависит от <see cref="CapacityMode"/>; при Unlimited бессмысленно.</summary>
        int Capacity { get; }

        /// <summary>
        /// Сколько единиц одного ресурса влезает в одну ячейку. Всегда не меньше 1;
        /// на лимит влияет только при <see cref="InventoryCapacity.Slots"/>.
        /// </summary>
        int StackSize { get; }

        /// <summary>Сумма всех количеств.</summary>
        int TotalUnits { get; }

        /// <summary>Сколько разных ресурсов лежит.</summary>
        int DistinctCount { get; }

        /// <summary>
        /// Сколько ячеек занято: сумма стеков по всем ресурсам. Это то же число, которым
        /// контейнер меряет свою полноту, — рисуй сетку по нему, а не по <see cref="DistinctCount"/>.
        /// </summary>
        int UsedSlots { get; }

        bool IsEmpty { get; }
        bool IsFull { get; }

        /// <summary>
        /// Сколько единиц ещё влезет — чему угодно. <see cref="int.MaxValue"/> при безлимите.
        /// <para>
        /// При <see cref="InventoryCapacity.Slots"/> это только целые свободные ячейки:
        /// недобитый верхний стек принимает лишь свой ресурс, и обещать его всем нельзя.
        /// Конкретному ресурсу может влезть больше — это решает <see cref="TryAdd"/>.
        /// </para>
        /// </summary>
        int FreeUnits { get; }

        /// <summary>Живой вид содержимого. Порядок стабилен — можно напрямую кормить список UI.</summary>
        IReadOnlyList<InventoryEntry> Entries { get; }

        /// <summary>Сколько лежит ВСЕГО, всех сортов. Ответ на «хватит ли на рецепт».</summary>
        int GetAmount(ResourceDefinition resource);

        /// <summary>Сколько лежит именно такого сорта.</summary>
        int GetAmount(ResourceDefinition resource, ResourceGrade grade);

        bool Contains(ResourceDefinition resource, int amount = 1);

        /// <summary>Добавить до <paramref name="amount"/> обычного сорта. Возвращает, сколько принято.</summary>
        int TryAdd(ResourceDefinition resource, int amount);

        /// <summary>Добавить до <paramref name="amount"/> нужного сорта.</summary>
        int TryAdd(ResourceDefinition resource, int amount, ResourceGrade grade);

        /// <summary>
        /// Забрать до <paramref name="amount"/>, начиная с обычного сорта.
        /// <para>
        /// Порядок именно такой во всём, что тратит склад молча — станки, кухня, заказы,
        /// постройки: сперва расходуется дешёвое. Иначе первое же случайное списание съело бы
        /// призовой урожай, ради которого игрок девять циклов носил воду.
        /// </para>
        /// </summary>
        int TryRemove(ResourceDefinition resource, int amount);

        /// <summary>Забрать до <paramref name="amount"/> именно такого сорта.</summary>
        int TryRemove(ResourceDefinition resource, int amount, ResourceGrade grade);

        /// <summary>Перелить всё в простой сток. Возвращает число перенесённых единиц.</summary>
        int TransferTo(IResourceSink target);

        /// <summary>Перелить в другой инвентарь с учётом его лимита. Возвращает число перенесённых единиц.</summary>
        int TransferTo(IInventory target);

        void Clear();

        /// <summary>Что-то изменилось. Дешёвый крючок для перерисовки UI.</summary>
        event Action<IInventory> Changed;

        /// <summary>Единицы пришли.</summary>
        event Action<IInventory, ResourceDefinition, int> Added;

        /// <summary>Единицы ушли.</summary>
        event Action<IInventory, ResourceDefinition, int> Removed;

        /// <summary>Единицы не влезли и были отброшены. Подключи это раньше, чем игроки смогут молча терять лут.</summary>
        event Action<IInventory, ResourceDefinition, int> Rejected;
    }
}

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

    /// <summary>Один ресурс и сколько его лежит. Неизменяемая — заменяй, а не правь.</summary>
    public readonly struct InventoryEntry
    {
        public readonly ResourceDefinition Resource;
        public readonly int Amount;

        public InventoryEntry(ResourceDefinition resource, int amount)
        {
            Resource = resource;
            Amount = amount;
        }

        public InventoryEntry WithAmount(int amount) => new InventoryEntry(Resource, amount);

        public override string ToString() =>
            Amount + "x " + (Resource != null ? Resource.Id : "<none>");
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

        int GetAmount(ResourceDefinition resource);
        bool Contains(ResourceDefinition resource, int amount = 1);

        /// <summary>Добавить до <paramref name="amount"/>. Возвращает, сколько реально принято.</summary>
        int TryAdd(ResourceDefinition resource, int amount);

        /// <summary>Забрать до <paramref name="amount"/>. Возвращает, сколько реально изъято.</summary>
        int TryRemove(ResourceDefinition resource, int amount);

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

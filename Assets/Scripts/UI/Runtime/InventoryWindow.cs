using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Склад как настоящая сетка ячеек: рисуется каждая, занятая или нет, — игрок видит,
    /// сколько места осталось, а не вычисляет это из числа.
    /// <para>
    /// Ячейка — это стек, а не вид ресурса: 250 пшеницы при стеке 99 займут три ячейки,
    /// ровно как их считает сам склад. Сетка, показывающая по ячейке на вид, врала бы про
    /// оставшееся место — а место здесь и есть единственное, ради чего окно открывают.
    /// </para>
    /// <para>
    /// Ячейки строятся один раз и лишь перезаполняются при изменениях — сетка стабильна,
    /// и пересборка элементов на каждую доставку аллоцировала бы впустую и заставляла список мерцать.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Inventory Window")]
    public sealed class InventoryWindow : MonoBehaviour
    {
        [Tooltip("Сколько ячеек рисовать, если у склада нет лимита по ячейкам.")]
        [SerializeField, Min(1)] private int _fallbackCells = 24;

        private VisualElement _overlay;
        private VisualElement _grid;
        private Label _count;

        private GameHud _hud;
        private IInventory _storage;

        private readonly List<VisualElement> _cells = new List<VisualElement>();

        /// <summary>Содержимое, разложенное по стекам: ровно то, что рисуется в ячейках.</summary>
        private readonly List<InventoryEntry> _stacks = new List<InventoryEntry>();

        private bool _dirty = true;

        public bool IsOpen => _overlay != null && _overlay.style.display != DisplayStyle.None;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) { enabled = false; return; }

            _hud = GetComponent<GameHud>();
            _overlay = root.Q<VisualElement>("inventory-overlay");
            _grid = root.Q<VisualElement>("inventory-grid");
            _count = root.Q<Label>("inventory-count");

            var close = root.Q<Button>("inventory-close");
            if (close != null) close.clicked += Close;

            if (_overlay != null) _overlay.RegisterCallback<ClickEvent>(OnOverlayClick);

            Bind();
            Close();
        }

        private void OnDisable()
        {
            if (_storage != null) _storage.Changed -= OnStorageChanged;
            _storage = null;
        }

        private void Update()
        {
            if (!IsOpen || !_dirty) return;
            Rebuild();
        }

        public void Open()
        {
            Bind();
            if (_overlay != null) _overlay.style.display = DisplayStyle.Flex;
            _dirty = true;
            Rebuild();
        }

        public void Close()
        {
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            _hud?.CloseSellMenu();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        private void OnOverlayClick(ClickEvent evt)
        {
            if (evt.target == _overlay) Close();
        }

        private void Bind()
        {
            if (_storage != null) return;
            _storage = FarmingRuntime.Sink as IInventory;
            if (_storage != null) _storage.Changed += OnStorageChanged;
        }

        private void OnStorageChanged(IInventory inventory) => _dirty = true;

        /// <summary>
        /// Разложить содержимое по стекам. Резать приходится здесь, а не в складе: складу
        /// хватает числа занятых ячеек, а сетке нужно, что именно лежит в каждой.
        /// </summary>
        private void BuildStacks()
        {
            _stacks.Clear();

            var entries = _storage?.Entries;
            if (entries == null) return;

            int stack = Mathf.Max(1, _storage.StackSize);
            for (int i = 0; i < entries.Count; i++)
            {
                int left = entries[i].Amount;
                while (left > 0)
                {
                    int part = Mathf.Min(stack, left);
                    _stacks.Add(new InventoryEntry(entries[i].Resource, part));
                    left -= part;
                }
            }
        }

        private int CellCount()
        {
            if (_storage != null && _storage.CapacityMode == InventoryCapacity.Slots)
                return Mathf.Max(_stacks.Count, _storage.Capacity);

            // Без лимита по ячейкам сетка всё равно нужна — берём столько, чтобы всё поместилось.
            return Mathf.Max(_fallbackCells, _stacks.Count);
        }

        private void Rebuild()
        {
            _dirty = false;
            if (_grid == null) return;

            BuildStacks();

            int cellCount = CellCount();
            int used = _stacks.Count;

            while (_cells.Count < cellCount) _cells.Add(CreateCell());

            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                bool exists = i < cellCount;
                cell.style.display = exists ? DisplayStyle.Flex : DisplayStyle.None;
                if (!exists) continue;

                bool filled = i < used;
                cell.EnableInClassList("cell--filled", filled);
                cell.EnableInClassList("cell--empty", !filled);

                var icon = cell.Q<VisualElement>(className: "cell__icon");
                var label = cell.Q<Label>(className: "cell__count");

                if (!filled)
                {
                    cell.userData = null;
                    cell.tooltip = null;
                    if (icon != null) icon.style.backgroundImage = new StyleBackground();
                    if (label != null) label.text = "";
                    continue;
                }

                var entry = _stacks[i];
                cell.userData = entry.Resource;
                // Цену спрашиваем у магазина, а не у ресурса: рынок даёт надбавку, и подсказка
                // не должна обещать меньше, чем реально заплатит кнопка продажи.
                // Всего — потому что ресурс мог растечься по нескольким ячейкам, а продажа
                // всё равно берёт со склада целиком.
                cell.tooltip = entry.Resource != null
                    ? entry.Resource.DisplayName + " — " + UnitPrice(entry.Resource) + " зол./шт" +
                      ", всего " + _storage.GetAmount(entry.Resource)
                    : null;

                if (icon != null)
                    icon.style.backgroundImage = entry.Resource != null && entry.Resource.Icon != null
                        ? new StyleBackground(entry.Resource.Icon)
                        : new StyleBackground();

                if (label != null) label.text = entry.Amount.ToString();
            }

            if (_count != null)
                _count.text = used + " / " + cellCount;
        }

        /// <summary>Сколько золота даёт единица прямо сейчас. До появления магазина — сырая цена ресурса.</summary>
        private static int UnitPrice(ResourceDefinition resource)
        {
            var shop = Shop.Instance;
            return shop != null ? shop.SellValue(resource, 1) : resource.SellPrice;
        }

        private VisualElement CreateCell()
        {
            var cell = new VisualElement();
            cell.AddToClassList("cell");

            var icon = new VisualElement();
            icon.AddToClassList("cell__icon");
            icon.pickingMode = PickingMode.Ignore;
            cell.Add(icon);

            var count = new Label();
            count.AddToClassList("cell__count");
            count.pickingMode = PickingMode.Ignore;
            cell.Add(count);

            cell.RegisterCallback<ClickEvent>(evt =>
            {
                var resource = cell.userData as ResourceDefinition;
                if (resource != null) _hud?.ShowSellMenu(resource, cell);
            });

            _grid.Add(cell);
            return cell;
        }
    }
}

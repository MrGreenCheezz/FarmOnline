using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Storage as a real grid of cells: every slot is drawn, occupied or not, so the player can see
    /// how much room is left rather than inferring it from a number.
    /// <para>
    /// Cells are built once and only refilled on change — the grid is stable, so rebuilding elements
    /// on every delivery would allocate for nothing and make the list flicker.
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

        private int CellCount()
        {
            if (_storage != null && _storage.CapacityMode == InventoryCapacity.Slots)
                return Mathf.Max(_storage.DistinctCount, _storage.Capacity);

            // Без лимита по ячейкам сетка всё равно нужна — берём столько, чтобы всё поместилось.
            int used = _storage != null ? _storage.DistinctCount : 0;
            return Mathf.Max(_fallbackCells, used);
        }

        private void Rebuild()
        {
            _dirty = false;
            if (_grid == null) return;

            int cellCount = CellCount();
            var entries = _storage?.Entries;
            int used = entries?.Count ?? 0;

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

                var entry = entries[i];
                cell.userData = entry.Resource;
                // Цену спрашиваем у магазина, а не у ресурса: рынок даёт надбавку, и подсказка
                // не должна обещать меньше, чем реально заплатит кнопка продажи.
                cell.tooltip = entry.Resource != null
                    ? entry.Resource.DisplayName + " — " + UnitPrice(entry.Resource) + " зол./шт"
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

        /// <summary>Gold one unit fetches right now. Falls back to the raw price before a shop exists.</summary>
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

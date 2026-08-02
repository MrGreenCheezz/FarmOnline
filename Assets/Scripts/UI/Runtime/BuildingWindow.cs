using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// The panel for one placed building: what it does now, what the next level costs, and the
    /// button that buys it. Opens when the player clicks a building, closes when they click away.
    /// <para>
    /// Asks <see cref="Building"/> whether an upgrade is possible instead of comparing prices
    /// itself — the building already owns that rule, and a second copy here is how the button ends
    /// up enabled for a purchase that then fails.
    /// </para>
    /// <para>
    /// Cost rows show have-vs-need per resource. A single "не хватает 14 — Железо" line under the
    /// button only names the first missing item, which reads as "get 14 iron" when the player is
    /// actually short on three things.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Building Window")]
    public sealed class BuildingWindow : MonoBehaviour
    {
        [Tooltip("Как часто перерисовывать панель, секунд. Полоска мастерской ползёт по этому же такту.")]
        [SerializeField, Min(0.02f)] private float _refreshInterval = 0.1f;

        private VisualElement _panel;
        private VisualElement _icon;
        private VisualElement _work;
        private VisualElement _workFill;
        private VisualElement _next;
        private VisualElement _costList;
        private Label _title;
        private Label _level;
        private Label _effect;
        private Label _workLabel;
        private Label _nextNote;
        private Label _message;
        private Button _upgrade;

        private Building _building;
        private Workshop _workshop;
        private float _timer;

        public bool IsOpen => _panel != null && _panel.style.display != DisplayStyle.None;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) { enabled = false; return; }

            _panel = root.Q<VisualElement>("building-panel");
            _icon = root.Q<VisualElement>("building-icon");
            _work = root.Q<VisualElement>("building-work");
            _workFill = root.Q<VisualElement>("building-work-fill");
            _next = root.Q<VisualElement>("building-next");
            _costList = root.Q<VisualElement>("building-cost");
            _title = root.Q<Label>("building-title");
            _level = root.Q<Label>("building-level");
            _effect = root.Q<Label>("building-effect");
            _workLabel = root.Q<Label>("building-work-label");
            _nextNote = root.Q<Label>("building-next-note");
            _message = root.Q<Label>("building-message");
            _upgrade = root.Q<Button>("building-upgrade");

            var close = root.Q<Button>("building-close");
            if (close != null) close.clicked += BuildingSelection.Clear;

            if (_upgrade != null) _upgrade.clicked += OnUpgradeClicked;

            BuildingSelection.Changed += OnSelectionChanged;
            OnSelectionChanged(BuildingSelection.Current);
        }

        private void OnDisable()
        {
            BuildingSelection.Changed -= OnSelectionChanged;
            _building = null;
            _workshop = null;
        }

        private void Update()
        {
            if (!IsOpen) return;

            // Постройку могли снести, пока панель открыта.
            if (_building == null) { Close(); return; }

            _timer += Time.unscaledDeltaTime;
            if (_timer < _refreshInterval) return;
            _timer = 0f;
            Refresh();
        }

        private void OnSelectionChanged(Building building)
        {
            _building = building;
            _workshop = building != null ? building.GetComponent<Workshop>() : null;

            if (building == null) { Close(); return; }

            if (_panel != null) _panel.style.display = DisplayStyle.Flex;
            Refresh();
        }

        private void Close()
        {
            if (_panel != null) _panel.style.display = DisplayStyle.None;
            _building = null;
            _workshop = null;
        }

        private void OnUpgradeClicked()
        {
            if (_building == null) return;

            if (!_building.CanUpgrade(out string reason))
            {
                ShowMessage(reason, true);
                return;
            }

            int from = _building.Level;
            if (_building.TryUpgrade()) ShowMessage("Уровень " + from + " → " + _building.Level, false);
            Refresh();
        }

        private void ShowMessage(string text, bool isError)
        {
            if (_message == null) return;
            _message.text = text ?? "";
            _message.EnableInClassList("window__message--hint", !isError);
        }

        // ---- отрисовка ----

        private void Refresh()
        {
            var definition = _building.Definition;
            if (definition == null) { Close(); return; }

            if (_title != null) _title.text = definition.DisplayName.ToUpperInvariant();
            if (_icon != null)
                _icon.style.backgroundImage = definition.Icon != null
                    ? new StyleBackground(definition.Icon)
                    : new StyleBackground();

            if (_level != null) _level.text = "Уровень " + _building.Level + " из " + definition.MaxLevel;

            var current = definition.GetLevel(_building.Level);
            if (_effect != null) _effect.text = current != null ? current.Note : definition.Description;

            RefreshWork();
            RefreshNextLevel(definition);
        }

        private void RefreshWork()
        {
            if (_work == null) return;

            if (_workshop == null)
            {
                _work.style.display = DisplayStyle.None;
                return;
            }

            _work.style.display = DisplayStyle.Flex;

            var running = _workshop.Running;
            if (_workLabel != null)
                _workLabel.text = running != null
                    ? running.ToString() + "   " + _workshop.BatchSeconds(running).ToString("0.0") + " с"
                    : "простаивает — нет сырья";

            if (_workFill != null)
                _workFill.style.width = new StyleLength(Length.Percent(_workshop.Progress01 * 100f));
        }

        private void RefreshNextLevel(BuildingDefinition definition)
        {
            if (_next == null) return;

            if (_building.IsMaxLevel)
            {
                _next.style.display = DisplayStyle.None;
                ShowMessage("максимальный уровень", false);
                return;
            }

            _next.style.display = DisplayStyle.Flex;

            int nextLevel = _building.Level + 1;
            var data = definition.GetLevel(nextLevel);
            if (_nextNote != null) _nextNote.text = data != null ? data.Note : "";

            BuildCostRows(definition.CostOf(nextLevel));

            bool can = _building.CanUpgrade(out string reason);
            if (_upgrade != null)
            {
                _upgrade.SetEnabled(can);
                _upgrade.tooltip = can ? null : reason;
            }
        }

        private void BuildCostRows(Price? price)
        {
            if (_costList == null) return;

            _costList.Clear();
            if (!price.HasValue) return;

            var cost = price.Value;
            var wallet = Wallet.Instance;
            var storage = FarmingRuntime.Sink as IInventory;

            if (cost.Gold > 0)
                _costList.Add(CostRow("Золото", wallet != null ? wallet.Gold : 0, cost.Gold));

            if (cost.Resources == null) return;
            foreach (var need in cost.Resources)
            {
                if (!need.IsValid) continue;
                int have = storage != null ? storage.GetAmount(need.Resource) : 0;
                _costList.Add(CostRow(need.Resource.DisplayName, have, need.Amount));
            }
        }

        private static VisualElement CostRow(string label, int have, int need)
        {
            var row = new VisualElement();
            row.AddToClassList("cost");

            var name = new Label(label);
            name.AddToClassList("cost__name");
            row.Add(name);

            var amount = new Label(have + " / " + need);
            amount.AddToClassList("cost__have");
            amount.EnableInClassList("cost__have--short", have < need);
            row.Add(amount);

            return row;
        }
    }
}

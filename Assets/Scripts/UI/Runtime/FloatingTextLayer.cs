using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Numbers that rise off the thing that produced them and fade.
    /// <para>
    /// This is the cheapest way to make a result feel earned: the player sees <i>where</i> the gain
    /// came from, not just that a counter somewhere changed. Merges get the loudest treatment
    /// because that is the decision the game is built on.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Floating Text")]
    public sealed class FloatingTextLayer : MonoBehaviour
    {
        private sealed class Item
        {
            public Label Label;
            public Vector3 World;
            public float Age;
            public float Life;
            public float Rise;
        }

        [SerializeField] private Camera _camera;

        [Tooltip("Сколько живёт всплывающее число, секунд.")]
        [SerializeField, Min(0.2f)] private float _life = 1.1f;

        [Tooltip("На сколько юнитов оно поднимается за это время.")]
        [SerializeField, Min(0f)] private float _rise = 1.1f;

        [Tooltip("Больше этого числа одновременно не показываем — иначе экран превращается в кашу.")]
        [SerializeField, Min(4)] private int _maxActive = 24;

        private VisualElement _layer;
        private readonly List<Item> _active = new List<Item>();
        private readonly Stack<Label> _pool = new Stack<Label>();
        private Shop _shop;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            _layer = root?.Q<VisualElement>("floating-text");
            if (_camera == null) _camera = Camera.main;
            if (_layer == null) { enabled = false; return; }

            FarmingEvents.Harvested += OnHarvested;
            FarmingEvents.Merged += OnMerged;
            FarmingEvents.Withered += OnWithered;

            _shop = Shop.Instance != null ? Shop.Instance : FindFirstObjectByType<Shop>();
            if (_shop != null) _shop.Sold += OnSold;
        }

        private void OnDisable()
        {
            FarmingEvents.Harvested -= OnHarvested;
            FarmingEvents.Merged -= OnMerged;
            FarmingEvents.Withered -= OnWithered;
            if (_shop != null) _shop.Sold -= OnSold;
            _shop = null;
        }

        // ---- события ----

        private void OnHarvested(Growable g, HarvestResult result)
        {
            if (g == null) return;
            string name = result.Resource != null ? result.Resource.DisplayName : "";
            Show("+" + result.Amount + " " + name, g.transform.position + Vector3.up * 0.6f, "float--harvest");
        }

        private void OnMerged(Growable survivor, Growable absorbed)
        {
            if (survivor == null) return;
            Show("Уровень " + survivor.Level + "!", survivor.transform.position + Vector3.up * 0.9f, "float--merge");
        }

        private void OnWithered(Growable g)
        {
            if (g == null) return;
            Show("испортилось", g.transform.position + Vector3.up * 0.5f, "float--bad");
        }

        private void OnSold(Shop shop, ResourceDefinition resource, int amount, int gold)
        {
            // Продажа идёт из меню, а не с точки на поле — показываем у камеры, по центру сверху.
            var world = _camera != null
                ? _camera.transform.position + _camera.transform.forward * 6f + Vector3.up * 1.5f
                : Vector3.up;
            Show("+" + gold + " зол.", world, "float--gold");
        }

        // ---- показ ----

        public void Show(string text, Vector3 world, string variantClass = null)
        {
            if (_layer == null || string.IsNullOrEmpty(text)) return;
            if (_active.Count >= _maxActive) Retire(0);

            var label = _pool.Count > 0 ? _pool.Pop() : NewLabel();
            label.text = text;
            label.style.display = DisplayStyle.Flex;
            label.style.opacity = 1f;

            // Сбрасываем варианты, иначе метка из пула унесёт цвет прошлого события.
            label.RemoveFromClassList("float--harvest");
            label.RemoveFromClassList("float--gold");
            label.RemoveFromClassList("float--merge");
            label.RemoveFromClassList("float--bad");
            if (!string.IsNullOrEmpty(variantClass)) label.AddToClassList(variantClass);

            _active.Add(new Item { Label = label, World = world, Age = 0f, Life = _life, Rise = _rise });
        }

        private Label NewLabel()
        {
            var label = new Label();
            label.AddToClassList("float");
            label.pickingMode = PickingMode.Ignore;
            _layer.Add(label);
            return label;
        }

        private void LateUpdate()
        {
            if (_camera == null || _layer == null) return;

            var panel = _layer.panel;
            if (panel == null) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var item = _active[i];
                item.Age += Time.deltaTime;

                float k = item.Age / item.Life;
                if (k >= 1f) { Retire(i); continue; }

                Vector3 world = item.World + Vector3.up * (item.Rise * Tween01(k));
                if (_camera.WorldToViewportPoint(world).z <= 0f) { item.Label.style.opacity = 0f; continue; }

                Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, _camera);
                item.Label.style.left = point.x - item.Label.resolvedStyle.width * 0.5f;
                item.Label.style.top = point.y - item.Label.resolvedStyle.height;

                // Держим полную непрозрачность первую половину жизни — иначе число не успевают прочесть.
                item.Label.style.opacity = k < 0.5f ? 1f : 1f - (k - 0.5f) * 2f;
            }
        }

        /// <summary>Fast at first, then drifting — matches how a "pop" reads.</summary>
        private static float Tween01(float k) => 1f - (1f - k) * (1f - k);

        private void Retire(int index)
        {
            var item = _active[index];
            item.Label.style.display = DisplayStyle.None;
            _pool.Push(item.Label);
            _active.RemoveAt(index);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Стройплощадка: купленная постройка или декор не появляется мгновенно, а строится
    /// реальные минуты — «театр труда» (решение владельца 06.08.2026, этап 3 колонии).
    /// <para>
    /// Стройка идёт <b>сама</b>, как станок работает без мастера: строитель-житель рядом
    /// лишь ускоряет её и даёт что смотреть. Без строителя ничего не виснет — правило
    /// «отказ системы обязан быть заметным» здесь превращается в «отказа не бывает».
    /// Грядки этим не трогаются: замедлять самый частый цикл игры театром нельзя.
    /// </para>
    /// <para>
    /// Работает на <see cref="GrowthScheduler"/>, а не в Update: будильник на момент
    /// готовности, пересчёт — только когда меняется скорость (пришёл или ушёл строитель).
    /// Update остаётся косметике: заглушка цели растёт из земли по прогрессу.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Construction Site")]
    public sealed class ConstructionSite : MonoBehaviour, IGrowthScheduled
    {
        /// <summary>Что строим: постройку или вещь-декор.</summary>
        public enum TargetKind { Building = 0, Prop = 1 }

        // Базовые сроки — прикидки до живого плейтеста. Постройка — событие редкое и
        // дорогое, ей положено «строиться»; декор — мелочь, ждать её долго обидно.
        public const double BuildingSeconds = 240.0;
        public const double PropSeconds = 90.0;

        /// <summary>Живой список площадок — для сейва и мозга строителя. Не копия!</summary>
        public static readonly List<ConstructionSite> All = new List<ConstructionSite>();

        private TargetKind _kind;
        private string _targetId;        // buildingId или id товара магазина
        private double _remaining;       // секунд при скорости 1
        private double _baseSeconds = 1.0;
        private double _lastSync;

        // Метка строителя рядом — тот же приём, что у станка: продлевается каждый тик,
        // протухает сама. Скорость площадки = 1 без строителя, его множитель — с ним.
        private float _tendSpeed = 1f;
        private double _tendUntil;

        private Transform _preview;      // растущая заглушка цели
        private int _handle = GrowthScheduler.InvalidHandle;

        public TargetKind Kind => _kind;
        public string TargetId => _targetId;

        /// <summary>Сколько секунд осталось при текущей скорости — для мозга и проверок.</summary>
        public double RemainingSeconds
        {
            get { Sync(); return _remaining / CurrentSpeed; }
        }

        /// <summary>Готовность 0..1 — растит заглушку.</summary>
        public float Progress01 =>
            _baseSeconds <= 0.0 ? 1f : Mathf.Clamp01(1f - (float)(_remaining / _baseSeconds));

        private float CurrentSpeed => FarmingRuntime.Now < _tendUntil ? _tendSpeed : 1f;

        /// <summary>Строитель у площадки: множитель скорости на ближайшие секунды.</summary>
        public void SetTendSpeed(float factor, double holdSeconds = 2.0)
        {
            Sync();   // сперва досчитать по старой скорости — задним числом не ускоряют
            _tendSpeed = Mathf.Max(1f, factor);
            _tendUntil = FarmingRuntime.Now + holdSeconds;
            Reschedule();
        }

        /// <summary>Начать стройку. Зовёт магазин при покупке и загрузка при чтении сейва.</summary>
        public void Begin(TargetKind kind, string targetId, double remainingSeconds, double baseSeconds)
        {
            _kind = kind;
            _targetId = targetId;
            _baseSeconds = Math.Max(1.0, baseSeconds);
            _remaining = Math.Max(0.0, remainingSeconds);
            _lastSync = FarmingRuntime.Now;

            name = "Construction_" + targetId;
            BuildPreview();
            Reschedule();
        }

        /// <summary>Для сейва: что и сколько осталось (секунд при скорости 1).</summary>
        public void CaptureState(out int kind, out string targetId, out double remaining)
        {
            Sync();
            kind = (int)_kind;
            targetId = _targetId;
            remaining = _remaining;
        }

        private void Sync()
        {
            double now = FarmingRuntime.Now;
            double elapsed = now - _lastSync;
            if (elapsed <= 0.0) return;

            _remaining = Math.Max(0.0, _remaining - elapsed * CurrentSpeed);
            _lastSync = now;
        }

        private void Reschedule()
        {
            var scheduler = GrowthScheduler.Existing;
            if (scheduler == null || _handle == GrowthScheduler.InvalidHandle) return;
            scheduler.Schedule(_handle, FarmingRuntime.Now + _remaining / CurrentSpeed);
        }

        void IGrowthScheduled.OnScheduledDue(double now)
        {
            Sync();
            if (_remaining > 0.05) { Reschedule(); return; }
            Finish();
        }

        /// <summary>
        /// Достроено: площадка заменяет себя настоящей вещью — тем же кодом-порядком, что
        /// магазин и загрузка, чтобы построенное ничем не отличалось от купленного раньше.
        /// </summary>
        private void Finish()
        {
            var registry = ContentRegistry.Instance;
            var shop = Shop.Instance;
            GameObject built = null;

            if (_kind == TargetKind.Building)
            {
                var definition = registry != null ? registry.Building(_targetId) : null;
                if (definition != null && definition.Prefab != null)
                {
                    built = Instantiate(definition.Prefab, transform.position, Quaternion.identity);
                    built.name = "Building_" + definition.Id;

                    var building = built.GetComponent<Building>();
                    if (building == null) building = built.AddComponent<Building>();
                    building.Configure(definition, 1);

                    if (definition.Service == BuildingService.Workshop && built.GetComponent<Workshop>() == null)
                        built.AddComponent<Workshop>();
                }
            }
            else
            {
                var item = registry != null ? registry.ShopItem(_targetId) : null;
                if (item != null && item.Prefab != null)
                {
                    built = Instantiate(item.Prefab, transform.position, transform.rotation);
                    built.name = item.Improvement != null ? "Improvement_" + item.Improvement.Id : item.Id;
                }
            }

            // Пропавший из ассетов товар — вслух: молча исчезнувшая стройка хуже ошибки.
            if (built == null)
            {
                Debug.LogWarning("[Стройка] Цель '" + _targetId + "' не нашлась в реестре — площадка снята", this);
            }
            else
            {
                if (built.GetComponent<Movable>() == null) built.AddComponent<Movable>();
                if (shop != null) shop.RegisterPlaced(built.transform);
                FarmingEvents.RaiseConstructed(built.transform);
            }

            Destroy(gameObject);
        }

        /// <summary>Заглушка: уменьшенная копия цели, растущая из земли по прогрессу.</summary>
        private void BuildPreview()
        {
            if (_preview != null) { Destroy(_preview.gameObject); _preview = null; }

            var registry = ContentRegistry.Instance;
            GameObject source = null;

            if (_kind == TargetKind.Building)
            {
                var definition = registry != null ? registry.Building(_targetId) : null;
                source = definition != null ? definition.Prefab : null;
            }
            else
            {
                var item = registry != null ? registry.ShopItem(_targetId) : null;
                source = item != null ? item.Prefab : null;
            }

            if (source == null) return;

            var preview = Instantiate(source, transform.position, transform.rotation, transform);
            preview.name = "preview";

            // Заглушка — картинка, не вещь: никакого поведения, никаких кликов.
            foreach (var component in preview.GetComponentsInChildren<Component>(true))
            {
                if (component is Transform || component is MeshFilter || component is MeshRenderer ||
                    component is SkinnedMeshRenderer) continue;
                Destroy(component);
            }

            _preview = preview.transform;
            ApplyPreviewScale();
        }

        private void ApplyPreviewScale()
        {
            if (_preview == null) return;
            float scale = Mathf.Lerp(0.25f, 1f, Progress01);
            _preview.localScale = new Vector3(scale, scale, scale);
        }

        private void Update() => ApplyPreviewScale();

        private void OnEnable()
        {
            All.Add(this);

            var scheduler = GrowthScheduler.Instance;
            if (scheduler == null) return;
            _handle = scheduler.Register(this);

            // До Begin будильник не заводится: AddComponent зовёт OnEnable раньше, чем
            // площадка узнаёт цель, и пустой будильник «достроил» бы её в ничто.
            if (!string.IsNullOrEmpty(_targetId)) Reschedule();
        }

        private void OnDisable()
        {
            All.Remove(this);

            var scheduler = GrowthScheduler.Existing;
            if (scheduler != null) scheduler.Unregister(_handle);
            _handle = GrowthScheduler.InvalidHandle;
        }
    }
}

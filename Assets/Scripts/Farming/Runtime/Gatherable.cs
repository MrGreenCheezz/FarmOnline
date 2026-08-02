using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Как игрок это берёт.</summary>
    public enum GatherMode
    {
        /// <summary>Единица за клик. Быстро, дёргано, хорошо для подвижного.</summary>
        Click = 0,
        /// <summary>Держать кнопку, заполняя шкалу. Медленнее за единицу, но узел платит больше.</summary>
        Hold = 1
    }

    /// <summary>
    /// То, что игрок собирает руками, — в противовес всему, что фермер делает сам.
    /// <para>
    /// Это противовес его автономии. Он ведёт ферму, пока тебя нет; собираемое отдаётся только
    /// тому, кто реально сидит в игре. Именно это даёт смысл ночной смене — ферма не
    /// останавливается из-за его сна, она переходит из рук в руки.
    /// </para>
    /// <para>
    /// Урожай идёт сразу на склад мира, минуя рюкзак фермера: игрок нигде не «стоит», нести
    /// домой нечего, а маршрут через спящего персонажа означал бы, что ночная добыча пролежит
    /// у него в карманах до утра.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Gatherable")]
    public sealed class Gatherable : MonoBehaviour
    {
        [Header("Что даёт")]
        [SerializeField] private ResourceDefinition _resource;

        [Tooltip("Сколько всего единиц в этом узле.")]
        [SerializeField, Min(1)] private int _amount = 3;

        [Header("Как берётся")]
        [SerializeField] private GatherMode _mode = GatherMode.Click;

        [Tooltip("Для Hold: сколько секунд удержания на одну единицу.")]
        [SerializeField, Min(0.05f)] private float _holdSeconds = 0.7f;

        [Tooltip("Высота «центра» при наведении курсора.")]
        [SerializeField, Min(0f)] private float _aimHeight = 0.35f;

        [Tooltip("Убирать объект, когда всё собрано.")]
        [SerializeField] private bool _removeWhenEmpty = true;

        private int _remaining;
        private float _progress;
        private bool _started;

        internal int RegistryIndex = -1;

        /// <summary>Единицы забрали. Второй аргумент — сколько.</summary>
        public event Action<Gatherable, int> Gathered;

        /// <summary>Ничего не осталось. Поднимается один раз, до удаления объекта.</summary>
        public event Action<Gatherable> Depleted;

        public ResourceDefinition Resource => _resource;
        public GatherMode Mode => _mode;
        public float AimHeight => _aimHeight;
        public int Remaining => _remaining;
        public bool IsEmpty => _remaining <= 0;

        /// <summary>Прогресс до следующей единицы при удержании, 0..1. В режиме клика — ноль.</summary>
        public float Progress01 => _mode == GatherMode.Hold ? Mathf.Clamp01(_progress / _holdSeconds) : 0f;

        private void Awake() => EnsureStarted();

        private void OnEnable()
        {
            EnsureStarted();
            GatherableRegistry.Register(this);
        }

        private void OnDisable() => GatherableRegistry.Unregister(this);

        private void EnsureStarted()
        {
            if (_started) return;
            _started = true;
            _remaining = Mathf.Max(1, _amount);
        }

        /// <summary>Настроить свежесозданный узел. Вызывать до того, как он показан.</summary>
        public void Configure(ResourceDefinition resource, int amount, GatherMode mode)
        {
            _resource = resource;
            _amount = Mathf.Max(1, amount);
            _mode = mode;
            _remaining = _amount;
            _progress = 0f;
            _started = true;
        }

        /// <summary>Взять одну единицу. Только для режима клика — false, когда ничего не осталось.</summary>
        public bool Collect()
        {
            if (IsEmpty) return false;
            Take(1);
            return true;
        }

        /// <summary>
        /// Продолжать удержание. Возвращает true в кадры, когда единица реально выпала.
        /// Прогресс хранится на узле, а не во вводе, — отпустить и вернуться не значит
        /// молча потерять уже сделанную работу.
        /// </summary>
        public bool Hold(float deltaTime)
        {
            if (IsEmpty || deltaTime <= 0f) return false;

            _progress += deltaTime;
            if (_progress < _holdSeconds) return false;

            int units = Mathf.FloorToInt(_progress / _holdSeconds);
            _progress -= units * _holdSeconds;

            Take(Mathf.Min(units, _remaining));
            return true;
        }

        private void Take(int units)
        {
            if (units <= 0) return;

            _remaining -= units;

            if (_resource != null)
                FarmingRuntime.Sink.Add(_resource, units);

            Raise(Gathered, units);

            if (_remaining > 0) return;

            var handler = Depleted;
            if (handler != null)
            {
                try { handler(this); }
                catch (Exception e) { Debug.LogException(e, this); }
            }

            if (_removeWhenEmpty) Destroy(gameObject);
        }

        private void Raise(Action<Gatherable, int> handler, int amount)
        {
            if (handler == null) return;
            try { handler(this, amount); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }

    /// <summary>Живой список всего, что игрок собирает руками. Тот же O(1) swap-removal, что и везде.</summary>
    public static class GatherableRegistry
    {
        private static readonly List<Gatherable> _all = new List<Gatherable>(32);

        public static IReadOnlyList<Gatherable> All => _all;
        public static int Count => _all.Count;

        internal static void Register(Gatherable g)
        {
            if (g == null || g.RegistryIndex >= 0) return;
            g.RegistryIndex = _all.Count;
            _all.Add(g);
        }

        internal static void Unregister(Gatherable g)
        {
            if (g == null) return;

            int i = g.RegistryIndex;
            if (i < 0 || i >= _all.Count || _all[i] != g) { g.RegistryIndex = -1; return; }

            int last = _all.Count - 1;
            _all[i] = _all[last];
            if (_all[i] != null) _all[i].RegistryIndex = i;
            _all.RemoveAt(last);
            g.RegistryIndex = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all.Clear();
    }
}

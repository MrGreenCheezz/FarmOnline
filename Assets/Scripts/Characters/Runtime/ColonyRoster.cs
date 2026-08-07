using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    /// <summary>
    /// Связка «определение жителя → агент сцены → койка». Живёт в сцене, а не в ассете,
    /// потому что агент и койка существуют только в сцене.
    /// <para>
    /// Работает в Awake, и это не мелочь: имя агента — идентичность сейва, и смениться
    /// оно обязано до того, как SaveRunner (order 100) начнёт раскладывать жителей по
    /// именам, а пересев черт — до того, как данные сейва станут авторитетными.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Colony Roster")]
    public sealed class ColonyRoster : MonoBehaviour
    {
        [Serializable]
        private sealed class Entry
        {
            [Tooltip("Кто живёт.")]
            public ResidentDefinition Definition;

            [Tooltip("Каким агентом сцены он живёт.")]
            public FarmerAgent Agent;

            [Tooltip("Где спит. Пусто — как настроено на самом агенте.")]
            public Transform Bed;

            [Tooltip("Куда носит урожай и при чём вертится. Пусто — как настроено на агенте.")]
            public Transform Home;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        /// <summary>Житель приехал в живой игре (не при загрузке) — интерфейсу есть что объявить.</summary>
        public static event Action<FarmerAgent> Arrived;

        /// <summary>
        /// Строка о следующем прибытии — для панели жителей; когда все дома — «все дома…»,
        /// null остаётся только у выключенного ростера (вне сцены фермы). Игрок обязан видеть,
        /// что́ приводит людей — и что больше не приведёт: рост без причины читается как
        /// случайность, а молча исчезнувшая строка — как поломка.
        /// </summary>
        public static string NextArrivalNote { get; private set; }

        private static ColonyRoster _instance;

        /// <summary>
        /// Зерно характера от имени игрока — для тех, кто приезжает ЖИВЬЁМ: их не было
        /// ни в сейве, ни в реестре при пересеве SaveRunner, и без этого поля Глашу,
        /// Тимофея, Луку и Захара весь мир получал бы с одним и тем же характером от
        /// имени объекта сцены (аудит). Ставит SaveRunner на старте; пусто — оффлайн,
        /// характер остаётся сценовым.
        /// </summary>
        public static string PlayerSeed;

        /// <summary>Известный ростеру уровень земли. Ноль — ещё не видел ни одного.</summary>
        private int _seenLevel;

        private void Awake()
        {
            _instance = this;
            FarmerAgent first = null;

            foreach (var entry in _entries)
            {
                if (entry == null || entry.Definition == null || entry.Agent == null)
                {
                    // Полупустая строка — ошибка расстановки, и молчать о ней нельзя.
                    Debug.LogWarning("[Roster] Пустая строка ростера — житель пропущен", this);
                    continue;
                }

                entry.Agent.ApplyDefinition(entry.Definition, entry.Bed, entry.Home);
                if (first == null) first = entry.Agent;
            }

            // Первая строка ростера — житель №1: за ним следит HUD, в него ложатся сейвы
            // эпохи одного фермера. Порядку регистрации это доверять нельзя — он от
            // порядка OnEnable, которого Unity не обещает.
            if (first != null) FarmerRegistry.Designate(first);
        }

        private void OnEnable()
        {
            FarmLevels.Changed += OnFarmLevelChanged;
            OnFarmLevelChanged(FarmLevels.Current);
        }

        private void OnDisable()
        {
            FarmLevels.Changed -= OnFarmLevelChanged;
            if (_instance == this) { _instance = null; NextArrivalNote = null; Arrived = null; }
        }

        /// <summary>
        /// Земля выросла (или партия загрузилась) — свериться, кто уже приехал. Приезд
        /// объявляется только при живом росте: загрузка включает состав молча, партия
        /// с этими людьми уже жила.
        /// </summary>
        private void OnFarmLevelChanged(int level)
        {
            // Окно восстановления гасит «живость»: RestoreState на загрузке поднимает Changed
            // с 1 до сохранённого уровня, и без стража каждая загрузка объявляла бы «к вам
            // приехал(а)…» про людей, которые давно живут на ферме.
            bool live = !Farming.FarmingRuntime.Restoring && _seenLevel > 0 && level > _seenLevel;

            foreach (var entry in _entries)
            {
                if (entry == null || entry.Definition == null || entry.Agent == null) continue;

                bool here = level >= entry.Definition.ArrivesAtFarmLevel;
                var go = entry.Agent.gameObject;
                if (go.activeSelf == here) continue;

                go.SetActive(here);

                if (here && live)
                {
                    // Характер новичка — от имени игрока: живой приезд значит, что в сейве
                    // его ещё не было. SetActive выше уже прогнал Awake — компоненты живы
                    // (та самая грабля «не трогать чужое до Awake» здесь не стреляет).
                    if (!string.IsNullOrEmpty(PlayerSeed))
                    {
                        var traits = entry.Agent.GetComponent<FarmerTraits>();
                        if (traits != null) traits.Reroll(PlayerSeed + "·" + entry.Agent.name);
                    }

                    Raise(entry.Agent);
                }
            }

            _seenLevel = level;
            RefreshArrivalNote(level);
        }

        private void RefreshArrivalNote(int level)
        {
            ResidentDefinition next = null;

            foreach (var entry in _entries)
            {
                if (entry == null || entry.Definition == null) continue;
                if (entry.Definition.ArrivesAtFarmLevel <= level) continue;

                if (next == null || entry.Definition.ArrivesAtFarmLevel < next.ArrivesAtFarmLevel)
                    next = entry.Definition;
            }

            // «Все дома» — не null: строка-объяснение роста колонии не имеет права молча
            // исчезнуть после последнего приезда (правило заметности; сам же комментарий
            // выше обещает, что игрок видит, что́ приводит людей — и что больше не приведёт).
            NextArrivalNote = next != null
                ? next.DisplayName + " приедет на " + next.ArrivesAtFarmLevel + "-й ступени земли"
                : "все дома — больше никто не приедет";
        }

        private static void Raise(FarmerAgent arrived)
        {
            var handler = Arrived;
            if (handler == null) return;
            try { handler(arrived); }
            catch (Exception e) { Debug.LogException(e, arrived); }
        }
    }
}

using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Один шаг цепочки роста. Последняя стадия определения — спелое состояние.</summary>
    [Serializable]
    public sealed class GrowthStageDef
    {
        [Tooltip("Подпись только для читаемости и отладки.")]
        [SerializeField] private string _name = "Stage";

        [Tooltip("Секунды на этой стадии до перехода к следующей.\n" +
                 "0 — взять Default Stage Duration из определения.\n" +
                 "У последней стадии игнорируется: она спелая и не истекает.")]
        [SerializeField, Min(0f)] private float _duration;

        [Tooltip("Необязательный меш этой стадии. Показывается через GrowableVisuals.")]
        [SerializeField] private GameObject _visual;

        public string Name => string.IsNullOrEmpty(_name) ? "Stage" : _name;
        public float RawDuration => _duration;
        public GameObject Visual => _visual;
    }

    /// <summary>
    /// Всё, что определяет одну сажаемую вещь: цепочка стадий, тайминги, выплата.
    /// Чистые данные — определение общее для всех грядок этой культуры и не хранит
    /// никакого рантайм-состояния.
    /// </summary>
    [CreateAssetMenu(menuName = "Farm/Growable", fileName = "Growable_")]
    public sealed class GrowableDefinition : ScriptableObject
    {
        [Header("Что это")]
        [Tooltip("Стабильный ключ для сохранений и поиска. Пусто — берётся имя ассета.")]
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private ResourceCategory _category = ResourceCategory.Crop;

        [Header("Рост")]
        [Tooltip("Используется стадиями, у которых собственная длительность 0.")]
        [SerializeField, Min(0.01f)] private float _defaultStageDuration = 10f;

        [Tooltip("По порядку от семечка к спелому. Последняя запись — собираемое состояние.")]
        [SerializeField] private GrowthStageDef[] _stages = Array.Empty<GrowthStageDef>();

        [Header("Урожай")]
        [SerializeField] private ResourceDefinition _yieldResource;

        [Tooltip("Урожай на уровне 1.")]
        [SerializeField, Min(1)] private int _baseYield = 1;

        [Header("Слияние")]
        [Tooltip("В какой вид перерождается грядка, когда сливаются две 20 уровня.\n" +
                 "Следующая ступень своей линии (дуб для саженца, железо для меди).\n" +
                 "Пусто — вершина линии: выше 20 уровня пути нет.")]
        [SerializeField] private GrowableDefinition _mergeNext;

        [Header("После сбора")]
        [Tooltip("Вкл: грядка перезапускается с Regrow Stage вместо опустошения.")]
        [SerializeField] private bool _regrows;

        [Tooltip("С какой стадии перезапускаться при отрастании (например 1 — пропустить семечко).")]
        [SerializeField, Min(0)] private int _regrowStage;

        [Tooltip("Вкл: когда грядка опустела (собрали одноразовую или всё испортилось), объект удаляется.\n" +
                 "Выкл: остаётся пустой грядкой на будущее.\n" +
                 "Без этого поле зарастает невидимыми мёртвыми объектами, которые всё ещё можно схватить.")]
        [SerializeField] private bool _removeWhenEmpty = true;

        // Накопленные секунды роста, с которых начинается каждая стадия; [0] всегда 0.
        [NonSerialized] private double[] _stageStart;

        public string Id => string.IsNullOrEmpty(_id) ? name : _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? Id : _displayName;
        public ResourceCategory Category => _category;
        public ResourceDefinition YieldResource => _yieldResource;
        public bool Regrows => _regrows;
        public bool RemoveWhenEmpty => _removeWhenEmpty;

        /// <summary>Следующая ступень линии для перехода слиянием; null — вершина.</summary>
        public GrowableDefinition MergeNext => _mergeNext;

        public int StageCount => _stages != null ? _stages.Length : 0;
        public int LastStageIndex => StageCount - 1;
        public int RegrowStage => StageCount == 0 ? 0 : Mathf.Clamp(_regrowStage, 0, LastStageIndex);

        public GrowthStageDef GetStage(int index)
        {
            if (_stages == null || index < 0 || index >= _stages.Length) return null;
            return _stages[index];
        }

        /// <summary>Секунды на стадии <paramref name="index"/>. Последняя стадия не истекает и отвечает 0.</summary>
        public float StageDuration(int index)
        {
            if (index < 0 || index >= StageCount) return 0f;
            if (index == LastStageIndex) return 0f;

            var stage = _stages[index];
            float d = stage != null ? stage.RawDuration : 0f;
            return d > 0f ? d : _defaultStageDuration;
        }

        /// <summary>Секунды роста от посадки до начала стадии <paramref name="index"/>.</summary>
        public double StageStartTime(int index)
        {
            EnsureCache();
            if (_stageStart.Length == 0) return 0.0;
            return _stageStart[Mathf.Clamp(index, 0, _stageStart.Length - 1)];
        }

        /// <summary>Секунды роста от посадки до спелости.</summary>
        public double TotalGrowTime => StageStartTime(LastStageIndex);

        /// <summary>На какой стадии окажется грядка через <paramref name="elapsedGrowth"/> секунд роста.</summary>
        public int StageAtElapsed(double elapsedGrowth)
        {
            EnsureCache();
            if (_stageStart.Length == 0) return 0;
            if (elapsedGrowth >= _stageStart[_stageStart.Length - 1]) return _stageStart.Length - 1;

            // Стадий совсем мало (3-6), поэтому простой проход тут быстрее бинарного поиска.
            for (int i = _stageStart.Length - 1; i > 0; i--)
                if (elapsedGrowth >= _stageStart[i]) return i;
            return 0;
        }

        /// <summary>
        /// Размер урожая для данного уровня слияния. Линейно уровню: уровень — это число
        /// слитых грядок (правило «суммой», решение владельца 06.08.2026), и слияние
        /// сохраняет суммарный доход, выигрывая место, а не печатает его. Экспонента ×2,
        /// жившая здесь раньше, на потолке 20 давала бы полмиллиона единиц за сбор.
        /// </summary>
        public int YieldFor(int level)
        {
            int lvl = Mathf.Max(1, level);
            long amount = (long)_baseYield * lvl;
            return (int)Math.Min(int.MaxValue, Math.Max(1, amount));
        }

        private void EnsureCache()
        {
            if (_stageStart != null && _stageStart.Length == StageCount) return;
            RebuildCache();
        }

        private void RebuildCache()
        {
            int n = StageCount;
            _stageStart = new double[n];
            double acc = 0.0;
            for (int i = 1; i < n; i++)
            {
                acc += StageDuration(i - 1);
                _stageStart[i] = acc;
            }
        }

        private void OnValidate()
        {
            _stageStart = null; // длительности могли поменяться в инспекторе
            if (StageCount > 0) _regrowStage = Mathf.Clamp(_regrowStage, 0, LastStageIndex);
        }
    }
}

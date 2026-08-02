using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>One step of a growth chain. The final stage in a definition is the ripe state.</summary>
    [Serializable]
    public sealed class GrowthStageDef
    {
        [Tooltip("Label for readability and debugging only.")]
        [SerializeField] private string _name = "Stage";

        [Tooltip("Seconds spent on this stage before moving to the next.\n" +
                 "Leave at 0 to use the definition's Default Stage Duration.\n" +
                 "Ignored on the last stage — that one is the ripe state and never expires.")]
        [SerializeField, Min(0f)] private float _duration;

        [Tooltip("Optional mesh shown while on this stage. Rendered by GrowableVisuals.")]
        [SerializeField] private GameObject _visual;

        public string Name => string.IsNullOrEmpty(_name) ? "Stage" : _name;
        public float RawDuration => _duration;
        public GameObject Visual => _visual;
    }

    /// <summary>
    /// Everything that makes one plantable thing tick: its stage chain, timings, and payout.
    /// Pure data — a definition is shared by every plot growing it, so it holds no runtime state.
    /// </summary>
    [CreateAssetMenu(menuName = "Farm/Growable", fileName = "Growable_")]
    public sealed class GrowableDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable key for saves and lookups. Falls back to the asset name when empty.")]
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private ResourceCategory _category = ResourceCategory.Crop;

        [Header("Growth")]
        [Tooltip("Used by any stage whose own Duration is 0.")]
        [SerializeField, Min(0.01f)] private float _defaultStageDuration = 10f;

        [Tooltip("Ordered from seed to ripe. The last entry is the harvestable state.")]
        [SerializeField] private GrowthStageDef[] _stages = Array.Empty<GrowthStageDef>();

        [Header("Harvest")]
        [SerializeField] private ResourceDefinition _yieldResource;

        [Tooltip("Yield at level 1.")]
        [SerializeField, Min(1)] private int _baseYield = 1;

        [Tooltip("Yield is multiplied by this per level above 1.\n" +
                 "2 mirrors the merge rule: two level-N merge into one level-N+1 that pays double.")]
        [SerializeField, Min(1f)] private float _yieldPerLevel = 2f;

        [Header("After harvest")]
        [Tooltip("On: the plot restarts itself from Regrow Stage instead of emptying.")]
        [SerializeField] private bool _regrows;

        [Tooltip("Stage the plot restarts from when regrowing (e.g. 1 to skip the seed stage).")]
        [SerializeField, Min(0)] private int _regrowStage;

        [Tooltip("On: когда грядка опустела (собрали одноразовую или всё испортилось), объект удаляется.\n" +
                 "Off: остаётся пустой грядкой на будущее.\n" +
                 "Без этого поле зарастает невидимыми мёртвыми объектами, которые всё ещё можно схватить.")]
        [SerializeField] private bool _removeWhenEmpty = true;

        [Header("Spoilage")]
        [Tooltip("Seconds a ripe plot may sit unharvested before it withers. 0 disables withering.")]
        [SerializeField, Min(0f)] private float _witherAfter;

        // Cumulative growth-seconds at which each stage begins; [0] is always 0.
        [NonSerialized] private double[] _stageStart;

        public string Id => string.IsNullOrEmpty(_id) ? name : _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? Id : _displayName;
        public ResourceCategory Category => _category;
        public ResourceDefinition YieldResource => _yieldResource;
        public bool Regrows => _regrows;
        public bool RemoveWhenEmpty => _removeWhenEmpty;
        public float WitherAfter => _witherAfter;

        public int StageCount => _stages != null ? _stages.Length : 0;
        public int LastStageIndex => StageCount - 1;
        public int RegrowStage => StageCount == 0 ? 0 : Mathf.Clamp(_regrowStage, 0, LastStageIndex);

        public GrowthStageDef GetStage(int index)
        {
            if (_stages == null || index < 0 || index >= _stages.Length) return null;
            return _stages[index];
        }

        /// <summary>Seconds spent on <paramref name="index"/>. The last stage never expires, so it reports 0.</summary>
        public float StageDuration(int index)
        {
            if (index < 0 || index >= StageCount) return 0f;
            if (index == LastStageIndex) return 0f;

            var stage = _stages[index];
            float d = stage != null ? stage.RawDuration : 0f;
            return d > 0f ? d : _defaultStageDuration;
        }

        /// <summary>Growth-seconds from planting until <paramref name="index"/> begins.</summary>
        public double StageStartTime(int index)
        {
            EnsureCache();
            if (_stageStart.Length == 0) return 0.0;
            return _stageStart[Mathf.Clamp(index, 0, _stageStart.Length - 1)];
        }

        /// <summary>Growth-seconds from planting until ripe.</summary>
        public double TotalGrowTime => StageStartTime(LastStageIndex);

        /// <summary>Which stage a plot is on after <paramref name="elapsedGrowth"/> growth-seconds.</summary>
        public int StageAtElapsed(double elapsedGrowth)
        {
            EnsureCache();
            if (_stageStart.Length == 0) return 0;
            if (elapsedGrowth >= _stageStart[_stageStart.Length - 1]) return _stageStart.Length - 1;

            // Stage counts are tiny (3-6), so a forward scan beats a binary search here.
            for (int i = _stageStart.Length - 1; i > 0; i--)
                if (elapsedGrowth >= _stageStart[i]) return i;
            return 0;
        }

        /// <summary>Harvest amount for a given merge level.</summary>
        public int YieldFor(int level)
        {
            int lvl = Mathf.Max(1, level);
            double amount = _baseYield * Math.Pow(_yieldPerLevel, lvl - 1);
            if (amount >= int.MaxValue) return int.MaxValue;
            return Mathf.Max(1, (int)Math.Round(amount));
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
            _stageStart = null; // durations may have changed in the inspector
            if (StageCount > 0) _regrowStage = Mathf.Clamp(_regrowStage, 0, LastStageIndex);
        }
    }
}

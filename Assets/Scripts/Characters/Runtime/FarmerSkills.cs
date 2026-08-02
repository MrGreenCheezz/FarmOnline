using System;
using UnityEngine;

namespace Farm.Characters
{
    /// <summary>What the farmer gets better at. Values are serialized — append, never renumber.</summary>
    public enum FarmerSkill
    {
        /// <summary>Сбор. Быстрее собирает и иногда снимает лишнее.</summary>
        Harvesting = 0,
        /// <summary>Ноги. Быстрее ходит и берёт больше грядок за один заход.</summary>
        Legs = 1,
        /// <summary>Спина. Носит больше за раз.</summary>
        Back = 2,
        /// <summary>Смекалка. Открывает самостоятельные дела — продажу, закупку.</summary>
        Wits = 3
    }

    /// <summary>
    /// The farmer's skills. They rise from doing the work, never from a menu.
    /// <para>
    /// This is the thing the player is meant to watch. A skill that goes up because you clicked
    /// "upgrade" is a shop; a skill that goes up because he has been hauling turnips all morning is
    /// a character. So nothing here is spendable — the only input is work already done.
    /// </para>
    /// <para>
    /// Levels do two different jobs on purpose. Small ones tune numbers he already lives by (speed,
    /// capacity); named thresholds hand him whole new duties. The second kind is what makes the
    /// progression readable: you do not notice +8% walking speed, you notice him walking to the
    /// market on his own for the first time.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Farmer Skills")]
    public sealed class FarmerSkills : MonoBehaviour
    {
        /// <summary>Смекалка, с которой он начинает сам продавать излишки.</summary>
        public const int SellingLevel = 2;

        /// <summary>Смекалка, с которой он сам докупает грядки на вырученное.</summary>
        public const int RestockLevel = 4;

        /// <summary>Спина, с которой он берёт тележку.</summary>
        public const int CartLevel = 3;

        private const int SkillCount = 4;

        [Header("Кривая роста")]
        [SerializeField, Min(1)] private int _maxLevel = 10;

        [Tooltip("Сколько опыта стоит второй уровень.")]
        [SerializeField, Min(1f)] private float _baseCost = 6f;

        [Tooltip("Во сколько раз дороже каждый следующий уровень.\n" +
                 "Держим пологим намеренно: первые уровни должны приходить за минуты наблюдения, " +
                 "иначе смотреть не за чем — рост, который не виден за сессию, не читается как рост.")]
        [SerializeField, Min(1.01f)] private float _costGrowth = 1.35f;

        [Header("Старт")]
        [Tooltip("С какого уровня начинает каждый навык. Отладочный рычаг: подняв его, " +
                 "можно сразу посмотреть на позднюю игру, не отматывая час.")]
        [SerializeField, Min(1)] private int _startingLevel = 1;

        private readonly int[] _levels = new int[SkillCount];
        private readonly float[] _xp = new float[SkillCount];
        private bool _ready;

        /// <summary>Skill went up. Third argument is the new level.</summary>
        public event Action<FarmerSkills, FarmerSkill, int> LevelledUp;

        public int MaxLevel => _maxLevel;

        // ---- чтение ----

        public int LevelOf(FarmerSkill skill)
        {
            EnsureReady();
            return _levels[(int)skill];
        }

        /// <summary>How far into the current level, 0..1. Reports 1 at max level.</summary>
        public float Progress01(FarmerSkill skill)
        {
            EnsureReady();

            int index = (int)skill;
            if (_levels[index] >= _maxLevel) return 1f;

            float need = CostOf(_levels[index] + 1);
            return need > 0f ? Mathf.Clamp01(_xp[index] / need) : 0f;
        }

        /// <summary>Experience the next level costs. Zero at max.</summary>
        public float CostOf(int level) =>
            level <= 1 || level > _maxLevel ? 0f : _baseCost * Mathf.Pow(_costGrowth, level - 2);

        // ---- запись ----

        /// <summary>Credit work done. Levels up as many times as the experience covers.</summary>
        public void Grant(FarmerSkill skill, float amount)
        {
            if (amount <= 0f) return;
            EnsureReady();

            int index = (int)skill;
            if (_levels[index] >= _maxLevel) return;

            _xp[index] += amount;

            while (_levels[index] < _maxLevel)
            {
                float need = CostOf(_levels[index] + 1);
                if (need <= 0f || _xp[index] < need) break;

                _xp[index] -= need;
                _levels[index]++;
                Raise(skill, _levels[index]);
            }

            if (_levels[index] >= _maxLevel) _xp[index] = 0f;
        }

        // ---- что это даёт ----

        /// <summary>Multiplier on harvesting speed.</summary>
        public float HarvestSpeed => 1f + (LevelOf(FarmerSkill.Harvesting) - 1) * 0.14f;

        /// <summary>Chance that one harvest yields an extra unit.</summary>
        public float BonusYieldChance => Mathf.Min(0.5f, (LevelOf(FarmerSkill.Harvesting) - 1) * 0.05f);

        /// <summary>Multiplier on walking speed.</summary>
        public float MoveSpeed => 1f + (LevelOf(FarmerSkill.Legs) - 1) * 0.09f;

        /// <summary>
        /// How many plots he is willing to visit before hauling home.
        /// <para>
        /// At level 1 he walks back after every single plot, which is the whole reason early game
        /// feels slow. Raising this is the most visible thing legs do.
        /// </para>
        /// </summary>
        public int RouteLength => 1 + (LevelOf(FarmerSkill.Legs) - 1) / 2;

        /// <summary>Extra units of backpack space on top of the base capacity.</summary>
        public int ExtraCapacity
        {
            get
            {
                int level = LevelOf(FarmerSkill.Back);
                int extra = (level - 1) * 2;
                if (level >= CartLevel) extra += 6;   // тележка
                return extra;
            }
        }

        public bool HasCart => LevelOf(FarmerSkill.Back) >= CartLevel;
        public bool CanSell => LevelOf(FarmerSkill.Wits) >= SellingLevel;
        public bool CanRestock => LevelOf(FarmerSkill.Wits) >= RestockLevel;

        /// <summary>Russian label, for UI and logs.</summary>
        public static string NameOf(FarmerSkill skill)
        {
            switch (skill)
            {
                case FarmerSkill.Harvesting: return "Сбор";
                case FarmerSkill.Legs: return "Ноги";
                case FarmerSkill.Back: return "Спина";
                case FarmerSkill.Wits: return "Смекалка";
                default: return skill.ToString();
            }
        }

        /// <summary>What the next level of this skill will change, in one line for the player.</summary>
        public string NextRewardOf(FarmerSkill skill)
        {
            int next = LevelOf(skill) + 1;
            if (next > _maxLevel) return "предел";

            switch (skill)
            {
                case FarmerSkill.Harvesting:
                    return "сбор быстрее на 14%";
                case FarmerSkill.Legs:
                    return (next - 1) % 2 == 0 ? "обходит на одну грядку больше" : "шаг быстрее на 9%";
                case FarmerSkill.Back:
                    return next == CartLevel ? "берёт тележку: +6 к переноске" : "+2 к переноске";
                case FarmerSkill.Wits:
                    if (next == SellingLevel) return "начнёт сам продавать излишки";
                    if (next == RestockLevel) return "начнёт сам докупать грядки";
                    return "ближе к новому умению";
                default:
                    return "";
            }
        }

        private void Awake() => EnsureReady();

        private void EnsureReady()
        {
            if (_ready) return;
            _ready = true;

            int start = Mathf.Clamp(_startingLevel, 1, _maxLevel);
            for (int i = 0; i < SkillCount; i++) _levels[i] = start;
        }

        private void Raise(FarmerSkill skill, int level)
        {
            var handler = LevelledUp;
            if (handler == null) return;
            try { handler(this, skill, level); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }
}

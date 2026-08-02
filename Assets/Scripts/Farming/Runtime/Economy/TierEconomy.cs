using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// One curve for the whole progression ladder.
    /// <para>
    /// This is the answer to "a tier system is a lot of work": the work is not building things, it
    /// is hand-typing prices, yields and timings for every rung. Here a tier is a single number and
    /// everything else is derived, so adding copper→iron→mithril costs one asset and one integer
    /// instead of a table nobody can keep balanced.
    /// </para>
    /// <para>
    /// The numbers stay written into the assets rather than computed at runtime — designers must be
    /// able to override any single value without fighting a formula.
    /// </para>
    /// </summary>
    public static class TierEconomy
    {
        /// <summary>Each tier is worth this much more than the one below.</summary>
        public const float ValueStep = 2.8f;

        /// <summary>And takes this much longer to produce. Deliberately gentler than the value step:
        /// higher tiers must be worth the wait, otherwise nobody climbs.</summary>
        public const float TimeStep = 1.65f;

        public static int SellPrice(int tier, int baseValue = 3) =>
            Mathf.Max(1, Mathf.RoundToInt(baseValue * Mathf.Pow(ValueStep, Mathf.Max(0, tier - 1))));

        public static float GrowSeconds(int tier, float baseSeconds = 8f) =>
            baseSeconds * Mathf.Pow(TimeStep, Mathf.Max(0, tier - 1));

        /// <summary>Gold price of a plot or vein that produces this tier.</summary>
        public static int PlotPrice(int tier, int basePrice = 20) =>
            Mathf.Max(1, Mathf.RoundToInt(basePrice * Mathf.Pow(ValueStep, Mathf.Max(0, tier - 1))));

        /// <summary>
        /// Gold part of a building upgrade. Level 1 is the initial build.
        /// Grows faster than resource cost so gold never stops mattering.
        /// </summary>
        public static int UpgradeGold(int level, int baseGold = 120) =>
            Mathf.Max(1, Mathf.RoundToInt(baseGold * Mathf.Pow(2.3f, Mathf.Max(0, level - 1))));

        /// <summary>How many units of a tier-N material an upgrade to <paramref name="level"/> costs.</summary>
        public static int UpgradeMaterials(int level, int baseAmount = 12) =>
            Mathf.Max(1, Mathf.RoundToInt(baseAmount * Mathf.Pow(1.55f, Mathf.Max(0, level - 1))));

        /// <summary>
        /// Which material tier an upgrade to <paramref name="level"/> demands.
        /// Level 1-2 use tier 1, 3-4 tier 2, and so on — a new tier every two levels keeps the
        /// player climbing the production ladder instead of grinding one resource forever.
        /// </summary>
        public static int RequiredTier(int level) => Mathf.Max(1, (level + 1) / 2);

        /// <summary>
        /// How much of a need one visit restores, as a fraction. Level 1 is a snack, the top level
        /// is a full meal — the point of upgrading is fewer interruptions, not a bigger number.
        /// </summary>
        public static float ServiceEfficiency(int level) => Mathf.Min(1f, 0.35f + (level - 1) * 0.22f);

        /// <summary>
        /// How many times faster a workshop runs at this level.
        /// <para>
        /// Uncapped where <see cref="ServiceEfficiency"/> stops at 1: a kitchen can only ever fill
        /// the bar once, so its ladder is short, but a sawmill has no ceiling to hit and its levels
        /// must keep paying. This is why workshops get six rungs and service buildings four.
        /// </para>
        /// </summary>
        public static float WorkshopSpeed(int level) => 1f + (Mathf.Max(1, level) - 1) * 0.55f;

        /// <summary>Fraction a market adds on top of every sale. Level 1 is +8%.</summary>
        public static float MarketBonus(int level) => Mathf.Max(0, level) * 0.08f;
    }
}

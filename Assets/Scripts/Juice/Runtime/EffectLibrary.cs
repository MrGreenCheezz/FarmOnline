using UnityEngine;

namespace Farm.Juice
{
    /// <summary>Which particle prefab belongs to which moment. One asset, easy to swap wholesale.</summary>
    [CreateAssetMenu(menuName = "Farm/Effect Library", fileName = "EffectLibrary")]
    public sealed class EffectLibrary : ScriptableObject
    {
        [Tooltip("Слияние — самый весомый жест в игре, эффект должен быть заметно ярче остальных.")]
        public GameObject MergeBurst;

        public GameObject Harvest;
        public GameObject Plant;
        public GameObject Ready;
        public GameObject Wither;
        public GameObject Place;
    }
}

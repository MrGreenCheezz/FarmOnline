using UnityEngine;

namespace Farm.Juice
{
    /// <summary>Какой префаб частиц какому моменту принадлежит. Один ассет — легко заменить всё разом.</summary>
    [CreateAssetMenu(menuName = "Farm/Effect Library", fileName = "EffectLibrary")]
    public sealed class EffectLibrary : ScriptableObject
    {
        [Tooltip("Слияние — самый весомый жест в игре, эффект должен быть заметно ярче остальных.")]
        public GameObject MergeBurst;

        public GameObject Harvest;
        public GameObject Plant;
        public GameObject Ready;
        public GameObject Place;
    }
}

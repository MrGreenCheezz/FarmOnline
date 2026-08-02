using UnityEngine;

namespace Farm.Juice
{
    /// <summary>
    /// Which clip plays for which action, in one asset. Variants per cue are picked at random so
    /// repeated actions do not turn into a machine-gun of the identical sample — the single most
    /// noticeable difference between "has sound" and "sounds good".
    /// </summary>
    [CreateAssetMenu(menuName = "Farm/Sound Bank", fileName = "SoundBank")]
    public sealed class SoundBank : ScriptableObject
    {
        [System.Serializable]
        public sealed class Cue
        {
            public AudioClip[] Clips;
            [Range(0f, 1f)] public float Volume = 1f;

            [Tooltip("Разброс высоты тона. Небольшой разброс убирает ощущение зацикленной записи.")]
            [Range(0f, 0.5f)] public float PitchJitter = 0.08f;

            public AudioClip Pick()
            {
                if (Clips == null || Clips.Length == 0) return null;
                return Clips[Random.Range(0, Clips.Length)];
            }
        }

        [Header("Ферма")]
        public Cue Plant;
        public Cue Ready;
        public Cue Harvest;
        public Cue Merge;
        public Cue Wither;

        [Header("Игрок")]
        public Cue PickUp;
        public Cue Drop;
        public Cue Deliver;

        [Header("Экономика")]
        public Cue Buy;
        public Cue Sell;
        public Cue Refused;

        [Header("Интерфейс")]
        public Cue UiOpen;
        public Cue UiClose;
        public Cue UiClick;
    }
}

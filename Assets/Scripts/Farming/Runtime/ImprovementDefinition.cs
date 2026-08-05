using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Возле чего житель ставит свой замысел.</summary>
    public enum ImprovementAnchor
    {
        /// <summary>У дома — того, куда он носит урожай.</summary>
        Home = 0,
        /// <summary>У любимого места — своего у каждого работника.</summary>
        FavouriteSpot = 1,
        /// <summary>
        /// У постройки определённого вида — у каждого её экземпляра свой дворик.
        /// Это и превращает случайные вещицы в осознанные ансамбли: стог имеет смысл
        /// у хлева, а не в чистом поле.
        /// </summary>
        Building = 2
    }

    /// <summary>
    /// Один замысел жителя: маленькое улучшение фермы, которое он однажды решит сделать сам —
    /// клумба, скамейка, костёр у крыльца.
    /// <para>
    /// Это ассет, а не код, по той же причине, что и весь контент фермы: новый замысел должен
    /// стоить один файл в редакторе. А лежит список замыслов на самом работнике — у будущих
    /// жителей будут свои: ферма обживается разными руками по-разному.
    /// </para>
    /// <para>
    /// Замысел — не покупка. Игрок ставит постройки со свойствами, житель — следы своей жизни:
    /// вещи без механики (кроме света от костра), из излишков склада, всегда переставляемые.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Farm/Improvement", fileName = "Improvement_")]
    public sealed class ImprovementDefinition : ScriptableObject
    {
        [SerializeField] private string _id;

        [Tooltip("Мысль, с которой он берётся за дело, — от первого лица, с маленькой буквы.")]
        [SerializeField] private string _thought = "сделаю-ка кое-что";

        [Tooltip("Что появляется на ферме. Movable добавится сам — построенное можно переставлять.")]
        [SerializeField] private GameObject _prefab;

        [Header("Цена и труд")]
        [Tooltip("Что уходит со склада. Пусто — бесплатно; сверх этого он всегда бережёт запас Keep Materials.")]
        [SerializeField] private ResourceCost _cost;

        [SerializeField, Min(1f)] private float _buildSeconds = 6f;

        [Header("Когда открывается")]
        [Tooltip("С какого дня фермы замысел приходит ему в голову.")]
        [SerializeField, Min(1)] private int _minDay = 1;

        [Tooltip("Сколько урожая ферма должна собрать. Замыслы — награда за прожитое, а не стартовый декор.")]
        [SerializeField, Min(0)] private int _minHarvested;

        [Header("Где и сколько")]
        [SerializeField] private ImprovementAnchor _anchor = ImprovementAnchor.Home;

        [Tooltip("Вид постройки-якоря. Только для Anchor = Building: у каждого её экземпляра " +
                 "будет свой дворик с собственным счётом построенного.")]
        [SerializeField] private BuildingDefinition _anchorBuilding;

        [Tooltip("На каком расстоянии от якоря искать место: от и до, метров.")]
        [SerializeField] private Vector2 _anchorRadius = new Vector2(1.5f, 3.5f);

        [Tooltip("Сколько таких он построит. У якоря-постройки — на каждый её экземпляр, " +
                 "иначе — на всю ферму.")]
        [SerializeField, Min(1)] private int _maxCount = 1;

        [Tooltip("Ступень ансамбля у одного якоря: сперва полностью строятся младшие ступени.\n" +
                 "Это и есть осознанность: сначала огородить хлев, потом сено, потом свет — " +
                 "а не фонарь посреди пустого двора.")]
        [SerializeField, Min(1)] private int _order = 1;

        [Tooltip("Насколько охотно берётся среди прочих доступных. 2 — вдвое чаще остальных.\n" +
                 "Это склонность, а не порядок: очерёдность открытия задают день и урожай.")]
        [SerializeField, Min(0.05f)] private float _weight = 1f;

        public string Id => string.IsNullOrEmpty(_id) ? name : _id;
        public string Thought => _thought;
        public GameObject Prefab => _prefab;
        public ResourceCost Cost => _cost;
        public float BuildSeconds => _buildSeconds;
        public ImprovementAnchor Anchor => _anchor;

        /// <summary>Вид постройки-якоря; null у домашних и любимых мест.</summary>
        public BuildingDefinition AnchorBuilding => _anchor == ImprovementAnchor.Building ? _anchorBuilding : null;

        public Vector2 AnchorRadius => _anchorRadius;
        public int MaxCount => _maxCount;
        public int Order => _order;
        public float Weight => _weight;

        /// <summary>Дозрела ли ферма до этого замысла.</summary>
        public bool IsUnlocked => FarmProgress.Day >= _minDay && FarmProgress.TotalHarvested >= _minHarvested;

        private void OnValidate()
        {
            if (_prefab == null)
                Debug.LogWarning("[Farm] У замысла '" + Id + "' нет префаба — строить будет нечего", this);

            if (_anchor == ImprovementAnchor.Building && _anchorBuilding == null)
                Debug.LogWarning("[Farm] У замысла '" + Id + "' якорь — постройка, но вид постройки не задан", this);
        }
    }
}

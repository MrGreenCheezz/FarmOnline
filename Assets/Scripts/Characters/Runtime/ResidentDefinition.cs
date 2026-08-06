using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    /// <summary>
    /// Житель как данные: имя-идентичность, рвения, личные замыслы.
    /// <para>
    /// Ассет, а не поля на объекте сцены, — по той же причине, что у культур и построек:
    /// жителя добавляют файлом, не правкой систем. Койки здесь нет и не будет: койка —
    /// это Transform сцены, ассету на сцену указывать нечем; связку «житель → койка»
    /// держит <see cref="ColonyRoster"/>.
    /// </para>
    /// <para>
    /// Рвения — оттенки в духе черт (<see cref="FarmerTraits"/>): они сдвигают, КОГДА
    /// намерение дозреет внутри своей полосы оценок, и не трогают сами полосы. Множитель,
    /// способный продавить быт выше еды, был бы поломкой распорядка, а не характером.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "Resident_", menuName = "Farm/Житель")]
    public sealed class ResidentDefinition : ScriptableObject
    {
        [Tooltip("Имя жителя. Оно же идентичность в сейве и зерно характера — двум жителям " +
                 "нельзя давать одно имя.")]
        [SerializeField] private string _displayName = "Фермер";

        [Tooltip("Рвение к порядку: во сколько раз быстрее обычного у него кончается " +
                 "терпение при беспорядке. Единица — обычный.")]
        [SerializeField, Range(0.5f, 2f)] private float _tidyZeal = 1f;

        [Tooltip("Тяга к обустройству: во сколько раз быстрее дозревает желание построить " +
                 "что-нибудь из замыслов. Единица — обычный.")]
        [SerializeField, Range(0.5f, 2f)] private float _projectZeal = 1f;

        [Tooltip("Личные замыслы: что он однажды построит сам. Пусто — не строит.")]
        [SerializeField] private ImprovementDefinition[] _improvements = Array.Empty<ImprovementDefinition>();

        public string DisplayName => _displayName;
        public float TidyZeal => _tidyZeal;
        public float ProjectZeal => _projectZeal;
        public ImprovementDefinition[] Improvements => _improvements;
    }
}

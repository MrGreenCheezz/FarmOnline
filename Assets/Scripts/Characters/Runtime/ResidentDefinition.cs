using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    /// <summary>
    /// Чем житель занят за жалование. Демаркация — в CLAUDE.md: жителям трансформация
    /// и быт, игроку сбор и слияние, друзьям уход и рынок.
    /// </summary>
    public enum ResidentRole
    {
        /// <summary>Подсобник: наём включает сбор рутины ≤3 ступени (Ф3).</summary>
        None = 0,

        /// <summary>Мастеровой: наём ставит его к станкам — партии идут быстрее.</summary>
        Craftsman = 1,

        /// <summary>Возчик: пока нанят, доска заказов шире на один слот; сданное возит к рынку.</summary>
        Carter = 2
    }

    /// <summary>
    /// Житель как данные: имя-идентичность, роль, рвения, личные замыслы.
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

        [Tooltip("Роль за жалование. Подсобник собирает рутину, мастеровой ведёт станки; " +
                 "неоплаченная роль живёт бытом.")]
        [SerializeField] private ResidentRole _role = ResidentRole.None;

        [Tooltip("Жалование роли, золота за сутки серверных часов. 0 — по общему тарифу (150).")]
        [SerializeField, Min(0)] private int _wagePerDay;

        [Tooltip("Рвение к порядку: во сколько раз быстрее обычного у него кончается " +
                 "терпение при беспорядке. Единица — обычный.")]
        [SerializeField, Range(0.5f, 2f)] private float _tidyZeal = 1f;

        [Tooltip("Тяга к обустройству: во сколько раз быстрее дозревает желание построить " +
                 "что-нибудь из замыслов. Единица — обычный.")]
        [SerializeField, Range(0.5f, 2f)] private float _projectZeal = 1f;

        [Tooltip("Личные замыслы: что он однажды построит сам. Пусто — не строит.")]
        [SerializeField] private ImprovementDefinition[] _improvements = Array.Empty<ImprovementDefinition>();

        public string DisplayName => _displayName;
        public ResidentRole Role => _role;
        public int WagePerDay => _wagePerDay;
        public float TidyZeal => _tidyZeal;
        public float ProjectZeal => _projectZeal;
        public ImprovementDefinition[] Improvements => _improvements;
    }
}

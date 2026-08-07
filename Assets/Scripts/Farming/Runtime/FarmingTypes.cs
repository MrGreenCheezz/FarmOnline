namespace Farm.Farming
{
    /// <summary>Семейство, к которому относится растимое. От него зависят правила слияния и приоритеты ИИ.</summary>
    public enum ResourceCategory
    {
        Crop = 0,
        Livestock = 1,
        Ore = 2
    }

    /// <summary>Фаза жизненного цикла одной грядки.</summary>
    public enum GrowthPhase
    {
        /// <summary>Ничего не посажено — место свободно.</summary>
        Empty = 0,
        /// <summary>Посажено и идёт по стадиям.</summary>
        Growing = 1,
        /// <summary>На последней стадии, можно собирать.</summary>
        Ready = 2,
        /// <summary>
        /// Историческое: порча исключена решением владельца 06.08.2026 — с реальными
        /// часами увядание стирало ферму за ночь. Значение держит совместимость,
        /// достичь его больше нечем.
        /// </summary>
        Withered = 3
    }

    /// <summary>Что дал один сбор. Возвращается из <see cref="Growable.TryHarvest"/>.</summary>
    public readonly struct HarvestResult
    {
        public readonly Growable Source;
        public readonly ResourceDefinition Resource;
        public readonly int Amount;
        /// <summary>Уровень слияния грядки в момент сбора — количество его уже учитывает.</summary>
        public readonly int Level;

        /// <summary>Сорт снятого: что выросло на ухоженной земле, то и лучше.</summary>
        public readonly ResourceGrade Grade;

        public HarvestResult(Growable source, ResourceDefinition resource, int amount, int level,
                             ResourceGrade grade = ResourceGrade.Common)
        {
            Source = source;
            Resource = resource;
            Amount = amount;
            Level = level;
            Grade = grade;
        }

        public override string ToString()
        {
            string id = Resource != null ? Resource.Id : "<none>";
            string mark = Grade == ResourceGrade.Common ? "" : " " + ResourceGrades.Mark(Grade);
            return Amount + "x " + id + " (lvl " + Level + ")" + mark;
        }
    }
}

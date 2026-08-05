using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game
{
    /// <summary>Одна грядка, загон или жила в сохранении.</summary>
    [Serializable]
    public sealed class PlotSave
    {
        public string GrowableId;
        public int Level = 1;
        public bool Ready;

        /// <summary>Стабильное имя грядки — по нему её находят события друзей. Пустое у старых сейвов.</summary>
        public string Uid;

        /// <summary>Наработанные секунды роста, а не таймстамп — см. <see cref="Growable.CaptureState"/>.</summary>
        public double ElapsedGrowth;

        /// <summary>Сколько стоит спелой. Держит порчу честной после загрузки.</summary>
        public double RipeSeconds;

        /// <summary>Собственный множитель скорости грядки, без общефермовых надбавок.</summary>
        public float OwnGrowthSpeed = 1f;

        public Vector3 Position;
        public float Yaw;
    }

    [Serializable]
    public sealed class BuildingSave
    {
        public string BuildingId;
        public int Level = 1;
        public Vector3 Position;
        public float Yaw;
    }

    [Serializable]
    public sealed class ImprovementSave
    {
        public string ImprovementId;
        public Vector3 Position;
        public float Yaw;
    }

    [Serializable]
    public sealed class ShopOwnedSave
    {
        public string ItemId;
        public int Count;
    }

    /// <summary>Общефермовый счёт построенного жителем — дом и любимое место.</summary>
    [Serializable]
    public sealed class BuiltSave
    {
        public string ImprovementId;
        public int Count;
    }

    /// <summary>Счёт по дворику: индекс постройки-якоря в массиве <see cref="FarmSaveData.Buildings"/>.</summary>
    [Serializable]
    public sealed class YardSave
    {
        public int BuildingIndex = -1;
        public string ImprovementId;
        public int Count;
    }

    [Serializable]
    public sealed class FarmerSave
    {
        public Vector3 Position;
        public float Yaw;

        // Нужды долями: сохранение переживёт правку максимумов в инспекторе.
        public float Satiety01 = 1f;
        public float Hydration01 = 1f;
        public float Energy01 = 1f;

        public int[] SkillLevels;
        public float[] SkillXp;
        public float[] Traits;

        /// <summary>Рюкзак: он мог нести урожай в момент сохранения.</summary>
        public InventorySnapshot Pack;

        /// <summary>До какого момента (unix-секунды серверных часов) фермер нанят. 0 — не нанят.</summary>
        public double HiredUntilUnix;
    }

    /// <summary>
    /// Вся партия одним снимком. Плоские массивы, а не словари, — <c>JsonUtility</c> словарей
    /// не умеет, а тащить внешний сериализатор ради сейва фермы значит платить крупной
    /// зависимостью за мелкую нужду.
    /// <para>
    /// Всё, что ссылается на контент, ссылается <b>строкой Id</b>: файл обязан пережить
    /// пересборку базы ассетов и переезд папок. Обратно в ассеты их превращает
    /// <see cref="ContentRegistry"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class FarmSaveData
    {
        /// <summary>
        /// Версия формата. Растёт, когда поля меняют смысл, — старое читается снисходительно.
        /// V2: появился <see cref="SavedAtUnix"/> — с него начинается оффлайн-рост.
        /// </summary>
        public int Version = 2;

        public string SavedAtUtc;

        /// <summary>
        /// Момент сохранения в unix-секундах по часам <see cref="FarmingRuntime.Now"/>.
        /// По разнице с «сейчас» ферма при загрузке узнаёт, сколько она прожила без нас.
        /// Онлайн эту разницу считает сервер по своим часам; это поле — для игры без сети.
        /// 0 — сейв формата V1, оффлайн-дельты не будет (партия просто продолжится).
        /// </summary>
        public double SavedAtUnix;

        public int Gold;
        public InventorySnapshot Storage;

        public float Time01 = 0.3f;
        public int Day = 1;
        public int TotalHarvested;
        public int TotalMerges;

        public PlotSave[] Plots = Array.Empty<PlotSave>();
        public BuildingSave[] Buildings = Array.Empty<BuildingSave>();
        public ImprovementSave[] Improvements = Array.Empty<ImprovementSave>();
        public ShopOwnedSave[] ShopOwned = Array.Empty<ShopOwnedSave>();
        public BuiltSave[] Built = Array.Empty<BuiltSave>();
        public YardSave[] Yards = Array.Empty<YardSave>();

        public FarmerSave Farmer;

        /// <summary>Натоптанные клетки парами (x, y, x, y…).</summary>
        public int[] Footpaths = Array.Empty<int>();

        /// <summary>Короткая строка для кнопки «Продолжить» в меню.</summary>
        public string Describe() =>
            "День " + Day + " · " + TotalHarvested + " урожая · " + Buildings.Length + " построек";
    }
}

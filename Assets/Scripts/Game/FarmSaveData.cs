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

        /// <summary>
        /// Полита ли в текущем цикле. Именно флаг: секунды, которые дал полив, уже вошли
        /// в <see cref="ElapsedGrowth"/>, и хранить тут нечего, кроме запрета полить дважды.
        /// Сервер по той же причине проверяет флаг, а не число.
        /// </summary>
        public bool Watered;

        /// <summary>Подкормлена ли в текущем цикле. Множитель урожая знает каталог, а не сейв.</summary>
        public bool Fertilized;

        /// <summary>Счёт ухоженных циклов подряд — из него растёт качество урожая.</summary>
        public int CareStreak;

        /// <summary>Номер цикла роста — по нему события друзей узнают «тот ли это посев».</summary>
        public int CycleId;

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

    /// <summary>
    /// Партия мастерской, не доведённая до конца. Наработанные секунды, а не таймстамп, —
    /// по той же причине, что у грядок: оффлайн-догон добавляется при чтении.
    /// Рецепт называется парой «вход-выход»: собственного id у рецепта нет, а пара
    /// уникальна внутри постройки.
    /// </summary>
    [Serializable]
    public sealed class WorkshopSave
    {
        public int BuildingIndex = -1;
        public string InputId;
        public string OutputId;
        public double ElapsedSeconds;
    }

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
        /// <summary>
        /// Имя жителя — идентичность между сейвом и сценой. Пустое у сейвов эпохи одного
        /// фермера: такой снимок читается как «житель №1», и его характер сохраняется —
        /// характер посеян от имени игрока, терять его при обновлении нельзя.
        /// </summary>
        public string Name;

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

        /// <summary>
        /// Личные замыслы и дворики жителя. Личные по замыслу («у будущих жителей будут
        /// свои», FarmerAgent), поэтому лежат в жителе, а не на ферме. Пустые у старых
        /// сейвов — их верхнеуровневые Built/Yards читаются как имущество жителя №1.
        /// </summary>
        public BuiltSave[] Built = Array.Empty<BuiltSave>();

        public YardSave[] Yards = Array.Empty<YardSave>();
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

        /// <summary>
        /// Уровень фермы, с единицы. От него зависит радиус земли, а от радиуса — где стоит
        /// забор и куда магазин кладёт покупку, поэтому он лежит рядом со счётчиками партии,
        /// а не среди построек: восстанавливать его нужно раньше всего расставляемого.
        /// <para>
        /// В сейвах до лестницы поля нет вовсе, и <c>JsonUtility</c> оставит здесь 0 —
        /// читающая сторона поднимает его до 1. Это ровно те 13 метров, с которыми старая
        /// партия и жила: молча съёжившаяся ферма была бы хуже любой ошибки.
        /// </para>
        /// </summary>
        public int FarmLevel = 1;

        /// <summary>
        /// Сколько вёдер в колодце и с какого момента копится следующее.
        /// <para>
        /// Хранится момент, а не «прошло секунд»: вода копится и в отсутствие игрока, и по
        /// той же причине, по которой рост считается от таймстампа посадки, — закрытая игра
        /// не тикает, а время идёт. У старых сейвов оба поля нулевые; ноль в
        /// <see cref="WaterFilledUnix"/> читается как «начать копить с этой минуты».
        /// </para>
        /// </summary>
        public int WaterCharges;

        public double WaterFilledUnix;

        /// <summary>Запас подкормки. Копится только за сданные заказы, поэтому момента не хранит.</summary>
        public int FertilizerCharges;

        /// <summary>
        /// Суммарный опыт игрока. Только сумма: уровень — производная (<see cref="FarmExperience"/>),
        /// и хранить его отдельно значило бы завести два числа, обязанные сходиться.
        /// </summary>
        public int TotalXp;

        /// <summary>
        /// Заказы, сданные в текущем окне времени. Сами заказы не хранятся — они выводятся из
        /// часов и того, что растёт на ферме (<see cref="FarmOrders"/>), поэтому помнить нужно
        /// ровно одно: за что уже заплачено. Записи чужих окон отсеются при чтении.
        /// </summary>
        public string[] FilledOrders = Array.Empty<string>();

        /// <summary>Полученные достижения — по идентификаторам, чтобы список можно было менять.</summary>
        public string[] Achievements = Array.Empty<string>();

        /// <summary>Сколько заказов сдано за партию: свой счётчик, его больше никто не ведёт.</summary>
        public int OrdersFilled;

        public PlotSave[] Plots = Array.Empty<PlotSave>();
        public BuildingSave[] Buildings = Array.Empty<BuildingSave>();
        public ImprovementSave[] Improvements = Array.Empty<ImprovementSave>();
        public ShopOwnedSave[] ShopOwned = Array.Empty<ShopOwnedSave>();

        /// <summary>Эпоха одного фермера: замыслы и дворики лежали на ферме. Читаются как
        /// имущество жителя №1; новые сейвы пишут их внутри <see cref="Residents"/>.</summary>
        public BuiltSave[] Built = Array.Empty<BuiltSave>();

        public YardSave[] Yards = Array.Empty<YardSave>();

        /// <summary>Эпоха одного фермера — читается как житель №1, когда <see cref="Residents"/> пуст.</summary>
        public FarmerSave Farmer;

        /// <summary>
        /// Жители колонии, по одному снимку на каждого. Массив, а не одно поле, — решение
        /// владельца 06.08.2026 о колонии; при единственном жителе он длиной один, и партия
        /// эпохи одного фермера остаётся собой после первой же записи.
        /// </summary>
        public FarmerSave[] Residents = Array.Empty<FarmerSave>();

        /// <summary>
        /// Недоделанные партии мастерских. С реальными часами без этого 8-часовая партия,
        /// начатая перед выходом, молча откатывалась — сырьё возвращалось в склад, который
        /// уже никуда не записывался.
        /// </summary>
        public WorkshopSave[] Workshops = Array.Empty<WorkshopSave>();

        /// <summary>Натоптанные клетки парами (x, y, x, y…).</summary>
        public int[] Footpaths = Array.Empty<int>();

        /// <summary>Короткая строка для кнопки «Продолжить» в меню.</summary>
        public string Describe() =>
            "День " + Day + " · " + TotalHarvested + " урожая · " + Buildings.Length + " построек";
    }
}

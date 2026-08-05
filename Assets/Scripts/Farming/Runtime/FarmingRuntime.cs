using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Источник времени для всей математики роста. Рост считается от таймстампов,
    /// а не из накопленных покадровых дельт, поэтому для оффлайн-прогресса достаточно
    /// подменить эти часы на сохраняемые.
    /// </summary>
    public interface IGameClock
    {
        /// <summary>Секунды. Обязаны монотонно не убывать.</summary>
        double Now { get; }
    }

    /// <summary>Часы по умолчанию: масштабируемое время Unity с запуска. Сбрасываются при перезапуске приложения.</summary>
    public sealed class UnityGameClock : IGameClock
    {
        public double Now => Time.timeAsDouble;
    }

    /// <summary>
    /// Реальные секунды с эпохи Unix. Назначь эти часы в <see cref="FarmingRuntime.Clock"/>,
    /// когда появятся сохранения и урожай продолжит расти при закрытой игре.
    /// </summary>
    public sealed class UnixGameClock : IGameClock
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public double Now => (DateTime.UtcNow - Epoch).TotalSeconds;
    }

    /// <summary>
    /// Куда падают собранные ресурсы. Система фермы зависит только от этого шва —
    /// настоящий склад подставляется, не трогая ни одну грядку.
    /// </summary>
    public interface IResourceSink
    {
        void Add(ResourceDefinition resource, int amount);
    }

    /// <summary>
    /// Откуда узнать высоту земли. Ещё один шов: рельеф рисует слой оформления, а класть по нему
    /// вещи нужно ядру, и ссылаться на оформление оно не может.
    /// </summary>
    public interface IGroundHeight
    {
        /// <summary>Высота земли под точкой, в мировых координатах.</summary>
        float SampleHeight(Vector3 worldPosition);
    }

    /// <summary>Земля-плоскость. Работает, пока рельефа в сцене нет.</summary>
    public sealed class FlatGround : IGroundHeight
    {
        public float SampleHeight(Vector3 worldPosition) => 0f;
    }

    /// <summary>
    /// Сток-пустышка для гостя: урожай помощи принадлежит хозяину и в мир гостя не
    /// попадает вовсе — он уезжает событием через <see cref="GuestMode.Helped"/>.
    /// </summary>
    public sealed class DiscardSink : IResourceSink
    {
        public static readonly DiscardSink Instance = new DiscardSink();
        public void Add(ResourceDefinition resource, int amount) { }
    }

    /// <summary>Сток-заглушка: логирует урожай и ведёт суммарный счёт по каждому ресурсу.</summary>
    public sealed class DebugResourceSink : IResourceSink
    {
        private readonly System.Collections.Generic.Dictionary<string, int> _totals =
            new System.Collections.Generic.Dictionary<string, int>();

        public System.Collections.Generic.IReadOnlyDictionary<string, int> Totals => _totals;

        public void Add(ResourceDefinition resource, int amount)
        {
            if (resource == null || amount <= 0) return;

            _totals.TryGetValue(resource.Id, out int current);
            _totals[resource.Id] = current + amount;

            if (FarmingRuntime.LogHarvests)
                Debug.Log("[Farming] +" + amount + " " + resource.Id + " (всего " + _totals[resource.Id] + ")");
        }
    }

    /// <summary>
    /// Единственное место подмены внешних зависимостей фермы. Выставляются один раз
    /// на старте — каждая грядка читает отсюда, а не держит собственную ссылку.
    /// </summary>
    public static class FarmingRuntime
    {
        private static IGameClock _clock;
        private static IResourceSink _sink;
        private static IGroundHeight _ground;

        public static IGameClock Clock
        {
            get => _clock ?? (_clock = new UnityGameClock());
            set => _clock = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static IResourceSink Sink
        {
            get => _sink ?? (_sink = new DebugResourceSink());
            set => _sink = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Рельеф земли. Пока никто не подставил свой — земля считается плоской, и всё работает
        /// ровно как раньше.
        /// </summary>
        public static IGroundHeight Ground
        {
            get => _ground ?? (_ground = new FlatGround());
            set => _ground = value ?? new FlatGround();
        }

        /// <summary>Выключить, когда появится настоящий интерфейс инвентаря.</summary>
        public static bool LogHarvests = true;

        public static double Now => Clock.Now;

        // Статики переживают перезапуск Play Mode при отключённом domain reload —
        // чистим явно, иначе второй запуск играет на мусоре первого.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _clock = null;
            _sink = null;
            _ground = null;
            LogHarvests = true;
        }
    }
}

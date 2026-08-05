using System;
using UnityEngine;
using Farm.Farming;

namespace Farm.Net
{
    /// <summary>
    /// Часы игры, идущие по серверному времени. <c>Now = поправка + Time.realtimeSinceStartupAsDouble</c>:
    /// тикает локальный монотонный счётчик, а поправка привязывает его к unix-секундам.
    /// До первой связи с сервером поправка берётся из локального UTC; каждый ответ сервера
    /// несёт <c>serverNow</c> и уточняет её через <see cref="ApplyServerTime"/>.
    /// <para>
    /// Монотонность — обещание <see cref="IGameClock"/>, и здесь она держится двумя замками:
    /// поправка назад не применяется вовсе, а <see cref="Now"/> вдобавок никогда не возвращает
    /// меньше уже выданного. Откат времени назад означал бы для грядок отрицательный рост.
    /// </para>
    /// </summary>
    public sealed class ServerClock : IGameClock
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Локальный UTC в unix-секундах — стартовая точка, пока сервер молчит.</summary>
        public static double UtcNowUnix => (DateTime.UtcNow - Epoch).TotalSeconds;

        /// <summary>Экземпляр, назначенный в <see cref="FarmingRuntime.Clock"/>. Null до <see cref="Install"/>.</summary>
        public static ServerClock Installed { get; private set; }

        /// <summary>Сдвиг локального счётчика к unix-секундам.</summary>
        private double _offset;

        /// <summary>Максимум, который уже выдавали наружу: страховка монотонности.</summary>
        private double _maxNow;

        public ServerClock()
        {
            _offset = UtcNowUnix - Time.realtimeSinceStartupAsDouble;
        }

        public double Now
        {
            get
            {
                double now = _offset + Time.realtimeSinceStartupAsDouble;
                if (now < _maxNow) return _maxNow;
                _maxNow = now;
                return now;
            }
        }

        /// <summary>
        /// Создать часы и назначить их ферме. BeforeSceneLoad — единственный правильный момент:
        /// это ПОСЛЕ <c>FarmingRuntime.ResetStatics</c> (SubsystemRegistration), который стёр бы
        /// назначенное раньше, и ДО <c>OnEnable</c> первых грядок, которые снимают стартовый
        /// таймстамп роста. Опоздай мы на кадр — грядки запомнили бы «время с запуска Unity»,
        /// и вся математика реальных часов поехала бы на десятки лет.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            Installed = new ServerClock();
            FarmingRuntime.Clock = Installed;
        }

        /// <summary>
        /// Уточнить поправку ответом сервера. Вперёд — применяется сразу; назад — игнорируется
        /// с предупреждением: монотонность дороже точности, а расхождение в секунды догонится
        /// следующим ответом само.
        /// </summary>
        public static void ApplyServerTime(double serverNow)
        {
            var clock = Installed;
            if (clock == null) return;

            double current = clock.Now;
            if (serverNow < current)
            {
                // Дрожь в доли секунды — обычная сетевая жизнь, о ней молчим. Предупреждение
                // оставлено настоящим расхождениям: перемотанные часы, чужой сервер.
                if (current - serverNow > 1.0)
                    Debug.LogWarning("[Net] серверное время позади наших часов на "
                        + (current - serverNow).ToString("F1") + " с — поправка не применена");
                return;
            }

            clock._offset = serverNow - Time.realtimeSinceStartupAsDouble;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Сдвинуть часы вперёд на <paramref name="seconds"/>. Только для отладки в редакторе:
        /// спелость восьмичасовой культуры иначе не проверить, не просидев рабочий день.
        /// </summary>
        public static void DebugAdvance(double seconds)
        {
            var clock = Installed;
            if (clock == null || seconds <= 0) return;
            clock._offset += seconds;
        }
#endif
    }
}

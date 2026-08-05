using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Запись об одной собранной в гостях грядке — уезжает событием хозяину.</summary>
    public readonly struct HelpReport
    {
        public readonly Vector3 Position;
        public readonly string GrowableId;
        public readonly int Level;
        public readonly string ResourceId;
        public readonly int Amount;

        public HelpReport(Vector3 position, string growableId, int level, string resourceId, int amount)
        {
            Position = position;
            GrowableId = growableId;
            Level = level;
            ResourceId = resourceId;
            Amount = amount;
        }
    }

    /// <summary>
    /// Режим «мы в гостях»: сцена собрана из чужого снимка, и почти всё в ней — чужое.
    /// <para>
    /// Живёт в Farm.Farming, а не рядом с сетью: для мира это просто вопрос «чья ферма»,
    /// и отвечать на него должны уметь и ввод, и сохранение, и HUD, не зная ничего про
    /// сервер. Кто и как нас сюда привёз — дело слоёв выше.
    /// </para>
    /// <para>
    /// Правила гостя одним абзацем: таскать, сливать, покупать, собирать ночное — нельзя
    /// (это руки хозяина); можно смотреть и <b>помогать</b> — собрать до
    /// <see cref="HelpLimit"/> спелых грядок за визит. Урожай помощи не попадает в мир
    /// гостя вовсе — только в <see cref="Helped"/>, откуда он уедет событием хозяину.
    /// </para>
    /// </summary>
    public static class GuestMode
    {
        /// <summary>Сколько грядок можно собрать за один визит. Классика жанра: помощь — жест, а не батрачество.</summary>
        public const int HelpLimit = 5;

        public static bool IsGuest { get; private set; }
        public static int OwnerId { get; private set; }
        public static string OwnerName { get; private set; } = "";
        public static int HelpUsed { get; private set; }

        public static bool CanHelp => IsGuest && HelpUsed < HelpLimit;

        /// <summary>Вход/выход из гостей или потрачена помощь — HUD перерисовывает плашку.</summary>
        public static event Action Changed;

        /// <summary>Гость собрал грядку хозяину. Слушает сетевой слой — он превратит это в события.</summary>
        public static event Action<HelpReport> Helped;

        /// <summary>Помощь кончилась, а игрок ещё кликает. Отказ обязан быть заметным — UI скажет вслух.</summary>
        public static event Action HelpRefused;

        public static void Enter(int ownerId, string ownerName)
        {
            IsGuest = true;
            OwnerId = ownerId;
            OwnerName = ownerName ?? "";
            HelpUsed = 0;
            Raise(Changed);
        }

        public static void Exit()
        {
            if (!IsGuest) return;
            IsGuest = false;
            OwnerId = 0;
            OwnerName = "";
            HelpUsed = 0;
            Raise(Changed);
        }

        /// <summary>Засчитать помощь и разнести весть. Зовётся ПОСЛЕ удачного сбора.</summary>
        public static void ReportHelp(in HelpReport report)
        {
            if (!IsGuest) return;

            HelpUsed++;
            Raise(Changed);

            var handler = Helped;
            if (handler == null) return;
            try { handler(report); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void RefuseHelp() => Raise(HelpRefused);

        private static void Raise(Action handler)
        {
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload —
        // чистим явно, иначе второй запуск начнётся «в гостях» у первого.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsGuest = false;
            OwnerId = 0;
            OwnerName = "";
            HelpUsed = 0;
            Changed = null;
            Helped = null;
            HelpRefused = null;
        }
    }
}

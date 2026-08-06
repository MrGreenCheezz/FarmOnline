using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Запись об одном поливе в гостях. Uid называет грядку, номер цикла — сам посев.</summary>
    public readonly struct CareReport
    {
        public readonly string Uid;
        public readonly int CycleId;

        public CareReport(string uid, int cycleId)
        {
            Uid = uid;
            CycleId = cycleId;
        }
    }

    /// <summary>Запись об одной собранной в гостях грядке — уезжает событием хозяину.</summary>
    public readonly struct HelpReport
    {
        /// <summary>Стабильное имя грядки — главный ключ поиска у хозяина.</summary>
        public readonly string Uid;

        public readonly Vector3 Position;
        public readonly string GrowableId;
        public readonly int Level;
        public readonly string ResourceId;
        public readonly int Amount;

        public HelpReport(string uid, Vector3 position, string growableId, int level, string resourceId, int amount)
        {
            Uid = uid;
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

        /// <summary>Сколько чужих грядок можно полить за визит. Свой лимит: сбор и полив — разные жесты.</summary>
        public const int CareLimit = 5;

        public static bool IsGuest { get; private set; }
        public static int OwnerId { get; private set; }
        public static string OwnerName { get; private set; } = "";
        public static int HelpUsed { get; private set; }
        public static int CareUsed { get; private set; }

        public static bool CanHelp => IsGuest && HelpUsed < HelpLimit;
        public static bool CanCare => IsGuest && CareUsed < CareLimit;

        /// <summary>Вход/выход из гостей или потрачена помощь — HUD перерисовывает плашку.</summary>
        public static event Action Changed;

        /// <summary>Гость собрал грядку хозяину. Слушает сетевой слой — он превратит это в события.</summary>
        public static event Action<HelpReport> Helped;

        /// <summary>Гость полил чужую грядку. Тоже уезжает событием — хозяин увидит и получит.</summary>
        public static event Action<CareReport> Cared;

        /// <summary>Помощь кончилась, а игрок ещё кликает. Отказ обязан быть заметным — UI скажет вслух.</summary>
        public static event Action HelpRefused;

        public static void Enter(int ownerId, string ownerName)
        {
            IsGuest = true;
            OwnerId = ownerId;
            OwnerName = ownerName ?? "";
            HelpUsed = 0;
            CareUsed = 0;
            Raise(Changed);
        }

        public static void Exit()
        {
            if (!IsGuest) return;
            IsGuest = false;
            OwnerId = 0;
            OwnerName = "";
            HelpUsed = 0;
            CareUsed = 0;
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

        /// <summary>Засчитать полив в гостях. Зовётся ПОСЛЕ того, как жест принят.</summary>
        public static void ReportCare(in CareReport report)
        {
            if (!IsGuest) return;

            CareUsed++;
            Raise(Changed);

            var handler = Cared;
            if (handler == null) return;
            try { handler(report); }
            catch (Exception e) { Debug.LogException(e); }
        }

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
            CareUsed = 0;
            Changed = null;
            Helped = null;
            Cared = null;
            HelpRefused = null;
        }
    }
}

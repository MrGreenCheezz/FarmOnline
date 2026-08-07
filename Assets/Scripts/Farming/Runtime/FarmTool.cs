using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Что сейчас в руке у игрока — чем именно он тронет грядку.</summary>
    public enum FarmToolKind
    {
        /// <summary>Рука: собрать спелое, открыть постройку. Жест по умолчанию.</summary>
        Hand = 0,

        /// <summary>Перенос: поднять грядку, отнести, слить с такой же.</summary>
        Move = 1,

        /// <summary>Ведро: полить растущее растение — рост идёт быстрее.</summary>
        Water = 2,

        /// <summary>Корм: накормить животное — урожай вдвое.</summary>
        Feed = 3,

        /// <summary>Удобрение: подкормить растение — урожай вдвое.</summary>
        Fertilizer = 4,
    }

    /// <summary>
    /// Инструмент в руке игрока. Статик той же породы, что <see cref="DragFocus"/> и
    /// <see cref="BuildingSelection"/>, и по той же причине: его ставит интерфейс (панель
    /// инструментов), а читает слой взаимодействия, и видеть друг друга этим сборкам
    /// не положено — обе смотрят только сюда, в Farm.Farming.
    /// <para>
    /// Зачем инструменты вообще (решение владельца 07.08.2026): один клик значил четыре
    /// вещи подряд — собрать, полить, подкормить, открыть панель, — и промах по спелой
    /// грядке молча тратил ведро на соседнюю растущую. Теперь каждый жест называет себя
    /// сам: пустая рука только собирает, а уход требует взять ведро или корм.
    /// </para>
    /// <para>
    /// Инструмент — НЕ режим-ловушка: он живёт до следующего выбора, но «рука» всегда
    /// в одном клике, и панель показывает выбранное. Правило 1 (сбор и слияние —
    /// только игроку) от этого не страдает: инструменты лишь разводят его собственные
    /// жесты, ни один из них не даёт права действовать никому другому.
    /// </para>
    /// </summary>
    public static class FarmTool
    {
        private static FarmToolKind _current = FarmToolKind.Hand;

        /// <summary>Что сейчас выбрано. По умолчанию — рука.</summary>
        public static FarmToolKind Current
        {
            get => _current;
            set
            {
                if (_current == value) return;
                _current = value;

                var handler = Changed;
                if (handler == null) return;
                try { handler(value); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>Инструмент сменился — панель перекрашивает выбранную кнопку.</summary>
        public static event Action<FarmToolKind> Changed;

        /// <summary>
        /// Иконку инструмента бросили на ферму — применить его туда, где сейчас указатель.
        /// <para>
        /// Канал нужен потому, что панель (Farm.UI) и слой ввода (Farm.Interaction) друг
        /// друга не видят и не должны: обе смотрят только сюда. Заодно точку экрана берёт
        /// тот, кто с указателем и работает, — панели незачем знать про Input System и
        /// пересчитывать координаты панели обратно в экранные.
        /// </para>
        /// </summary>
        public static event Action ApplyRequested;

        /// <summary>Позвать применение. Зовёт панель после сброса иконки на ферму.</summary>
        public static void RequestApply()
        {
            var handler = ApplyRequested;
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Взять руку обратно. Зовут после разового применения перетаскиванием.</summary>
        public static void Reset() => Current = FarmToolKind.Hand;

        /// <summary>Человеческое имя — для подсказок и отказов.</summary>
        public static string NameOf(FarmToolKind tool)
        {
            switch (tool)
            {
                case FarmToolKind.Move: return "перенос";
                case FarmToolKind.Water: return "ведро";
                case FarmToolKind.Feed: return "корм";
                case FarmToolKind.Fertilizer: return "удобрение";
                default: return "рука";
            }
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload —
        // без сброса вторая партия начнётся с ведром в руке от первой.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = FarmToolKind.Hand;
            Changed = null;
            ApplyRequested = null;
        }
    }
}

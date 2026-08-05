using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Что сейчас несут — игрок мышью или фермер руками. Один слот на всю ферму: двумя руками
    /// одну грядку никто не тащит.
    /// <para>
    /// Третий статик той же породы, что <see cref="BuildingSelection"/> и <see cref="GatherFocus"/>,
    /// и по той же причине: несёт объект слой взаимодействия или персонаж, а знать об этом нужно
    /// самому объекту — пасущееся животное не должно брести по своим делам, пока его тащат.
    /// </para>
    /// <para>
    /// Отдельно запоминается, к чему прикасался именно <b>игрок</b>. Это граница, за которую
    /// фермеру нельзя: он наводит порядок, но переставлять то, что игрок только что поставил сам, —
    /// худшее, что может сделать помощник. Игрок раскладывает грядки под слияние, и перетасовать
    /// их значит отнять у него ход.
    /// </para>
    /// </summary>
    public static class DragFocus
    {
        /// <summary>Сколько секунд вещь считается «только что тронутой игроком».</summary>
        public const float PlayerClaimSeconds = 60f;

        private static Transform _current;
        private static bool _byPlayer;
        private static readonly Dictionary<Transform, double> _playerTouched =
            new Dictionary<Transform, double>();

        /// <summary>Что несут, или null.</summary>
        public static Transform Current => _current != null ? _current : null;

        /// <summary>
        /// Несёт ли это игрок, а не фермер. Ложь, когда в руках пусто.
        /// <para>
        /// Отличать нужно интерфейсу: пока ношу ведёт мышь, HUD уступает ей дорогу, а таскает
        /// фермер почти всё время — на его ходки табло дёргаться не должно. По одной только
        /// <see cref="IsPlayerClaimed"/> это не читается: метка живёт минуту после того, как
        /// вещь отпустили.
        /// </para>
        /// </summary>
        public static bool ByPlayer => _current != null && _byPlayer;

        /// <summary>Несут ли именно этот объект.</summary>
        public static bool IsDragged(Transform candidate) =>
            candidate != null && ReferenceEquals(_current, candidate);

        /// <summary>Взяли или отпустили. Аргумент — новый объект в руках, null при отпускании.</summary>
        public static event Action<Transform> Changed;

        /// <summary>
        /// Отметить, что объект взяли.
        /// </summary>
        /// <param name="byPlayer">
        /// Взял игрок, а не фермер. Только в этом случае вещь получает защиту от перестановки.
        /// </param>
        public static void Set(Transform target, bool byPlayer = true)
        {
            if (target == null) target = null;

            if (byPlayer && target != null) Claim(target);

            // До проверки на «тот же объект»: сменить руки, не меняя ношу, — тоже смена состояния.
            _byPlayer = byPlayer && target != null;

            if (ReferenceEquals(_current, target)) return;

            _current = target;

            var handler = Changed;
            if (handler == null) return;
            try { handler(target); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void Clear() => Set(null, false);

        /// <summary>Отметить прикосновение игрока, не беря объект в руки.</summary>
        public static void Claim(Transform target)
        {
            if (target == null) return;
            _playerTouched[target] = FarmingRuntime.Now;
        }

        /// <summary>
        /// Трогал ли игрок этот объект недавно. Пока да — фермер обязан обходить его стороной.
        /// </summary>
        public static bool IsPlayerClaimed(Transform target, float seconds = PlayerClaimSeconds)
        {
            if (target == null) return false;
            if (!_playerTouched.TryGetValue(target, out double when)) return false;

            if (FarmingRuntime.Now - when <= seconds) return true;

            // Срок вышел — заодно подчищаем запись, чтобы словарь не рос вечно.
            _playerTouched.Remove(target);
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            _byPlayer = false;
            _playerTouched.Clear();
            Changed = null;
        }
    }
}

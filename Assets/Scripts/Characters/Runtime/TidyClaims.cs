using System.Collections.Generic;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    /// <summary>
    /// Застолбление целей уборки: какую грядку какой житель взялся переносить.
    /// <para>
    /// Метка со сроком годности, а не пара «занял/освободил»: у уборки полдюжины путей
    /// оборваться — созрела по дороге, игрок перехватил, пересмотр решения, загрузка, —
    /// и ручное освобождение на каждом из них было бы утечкой, ждущей забытой ветки.
    /// Житель продлевает метку каждый кадр, пока занят грядкой; брошенная протухает сама
    /// за <see cref="FreshSeconds"/>. Тот же приём, что у отметки игрока в
    /// <see cref="DragFocus.IsPlayerClaimed"/>.
    /// </para>
    /// </summary>
    public static class TidyClaims
    {
        /// <summary>
        /// Сколько секунд метка живёт без продления. Меньше пары секунд нельзя — кадровый
        /// затык снимал бы живую метку; сильно больше — брошенная грядка слишком долго
        /// стояла бы «занятой» для остальных.
        /// </summary>
        public const double FreshSeconds = 3.0;

        private struct Claim
        {
            public FarmerAgent Who;
            public double When;
        }

        private static readonly Dictionary<Growable, Claim> _claims = new Dictionary<Growable, Claim>();

        /// <summary>
        /// Поставить или продлить метку. Чужую свежую не перебивает: кто первым взялся,
        /// тот и несёт, — второй увидит занятость и отступится в своём же тике.
        /// </summary>
        public static void Stamp(FarmerAgent who, Growable plot)
        {
            if (who == null || plot == null) return;

            if (_claims.TryGetValue(plot, out var claim) &&
                claim.Who != null && claim.Who != who &&
                FarmingRuntime.Now - claim.When <= FreshSeconds)
                return;

            _claims[plot] = new Claim { Who = who, When = FarmingRuntime.Now };
        }

        /// <summary>Держит ли эту грядку свежей меткой кто-то другой.</summary>
        public static bool HeldByOther(FarmerAgent who, Growable plot)
        {
            if (plot == null || !_claims.TryGetValue(plot, out var claim)) return false;
            if (claim.Who == null || claim.Who == who) return false;

            if (FarmingRuntime.Now - claim.When <= FreshSeconds) return true;

            // Протухла — заодно подчистить запись, чтобы словарь не рос вечно.
            // Записи снесённых грядок доживают до конца сцены — их не спрашивают.
            _claims.Remove(plot);
            return false;
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload — чистим
        // явно, как DragFocus.ResetStatics.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _claims.Clear();
    }
}

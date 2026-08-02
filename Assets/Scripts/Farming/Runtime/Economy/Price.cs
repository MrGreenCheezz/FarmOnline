using System;
using System.Text;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>Количество одного ресурса, входящее в цену.</summary>
    [Serializable]
    public struct ResourceCost
    {
        public ResourceDefinition Resource;
        [Min(1)] public int Amount;

        public bool IsValid => Resource != null && Amount > 0;
    }

    /// <summary>
    /// Сколько что-то стоит: золото, ресурсы или и то и другое. Ресурсная часть цены —
    /// то, что держит ферму нужной, когда золото начинает копиться горой: амбар должен
    /// хотеть досок, а не только монет.
    /// </summary>
    [Serializable]
    public struct Price
    {
        [Min(0)] public int Gold;
        public ResourceCost[] Resources;

        public bool IsFree => Gold <= 0 && (Resources == null || Resources.Length == 0);

        /// <summary>Можно ли оплатить из этого кошелька и склада?</summary>
        public bool CanPay(int gold, IInventory storage, out string missing)
        {
            missing = null;

            if (gold < Gold)
            {
                missing = "не хватает " + (Gold - gold) + " золота";
                return false;
            }

            if (Resources == null) return true;

            foreach (var cost in Resources)
            {
                if (!cost.IsValid) continue;

                int have = storage != null ? storage.GetAmount(cost.Resource) : 0;
                if (have >= cost.Amount) continue;

                missing = "не хватает " + (cost.Amount - have) + " — " + cost.Resource.DisplayName;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Списать ресурсную половину цены со склада. Золото — забота кошелька.
        /// Вызывать только после успешного <see cref="CanPay"/> — повторной проверки тут нет.
        /// </summary>
        public void ChargeResources(IInventory storage)
        {
            if (Resources == null || storage == null) return;

            foreach (var cost in Resources)
                if (cost.IsValid) storage.TryRemove(cost.Resource, cost.Amount);
        }

        public override string ToString()
        {
            if (IsFree) return "бесплатно";

            var sb = new StringBuilder();
            if (Gold > 0) sb.Append(Gold).Append(" зол.");

            if (Resources == null) return sb.ToString();

            foreach (var cost in Resources)
            {
                if (!cost.IsValid) continue;
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append(cost.Amount).Append(' ').Append(cost.Resource.DisplayName);
            }

            return sb.ToString();
        }
    }
}

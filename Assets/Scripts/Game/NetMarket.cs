using System;
using UnityEngine;
using Farm.Farming;
using Farm.Net;

namespace Farm.Game
{
    /// <summary>
    /// Рынок между игроками — клиентская логика сделок. Окно рисует список и зовёт сюда;
    /// здесь порядок операций, от которого зависит честность.
    /// <para>
    /// Три правила, выведенные разбором гонок (см. docs/ONLINE.md, «Рынок»):
    /// деньги и склад не трогаются ДО ответа сервера — отказ тогда не стоит ничего;
    /// после ok всё применяется и немедленно сохраняется — дюп-окно сжимается до
    /// миллисекунд; при остановленном автосейве (<see cref="NetSession.PutBlocked"/>)
    /// рынок закрыт целиком — торговля, которая не персистится, это станок для дюпа.
    /// </para>
    /// <para>
    /// Цены назначает сервер от каталога (продавцу ×1.25, покупателю ×1.5, спред сгорает).
    /// Клиент их не считает и не проверяет — он только показывает.
    /// </para>
    /// </summary>
    public static class NetMarket
    {
        /// <summary>Сделка прошла — окно перечитывает список лотов.</summary>
        public static event Action Changed;

        /// <summary>Отказ рынка, словами для игрока.</summary>
        public static event Action<string> Refused;

        /// <summary>Можно ли сейчас торговать вообще. Причина отказа — наружу строкой.</summary>
        public static bool Available(out string reason)
        {
            if (!NetSession.LoggedIn) { reason = "рынок доступен только с аккаунтом"; return false; }
            if (GuestMode.IsGuest) { reason = "в гостях не торгуют"; return false; }

            if (NetSession.PutBlocked)
            {
                // Сейвы стоят после конфликта ревизий. Сделка при этом прошла бы на сервере,
                // но не записалась бы в снимок — золото и ресурсы разъехались бы молча.
                reason = "ферма открыта в другом окне — рынок закрыт";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Выставить лот со склада. Порядок: сервер сказал «да» — только потом склад худеет.
        /// Сервер сверяет лот с последним снимком, поэтому свежесобранное может честно
        /// отказаться листиться до ближайшего сейва — отказ придёт словами.
        /// </summary>
        public static async void Sell(ResourceDefinition resource, int amount)
        {
            if (!Available(out string reason)) { Refuse(reason); return; }
            if (resource == null || amount <= 0) return;

            var storage = FarmingRuntime.Sink as Inventory;
            if (storage == null) return;

            if (storage.GetAmount(resource) < amount)
            {
                Refuse("на складе нет столько: " + resource.DisplayName);
                return;
            }

            var res = await ApiClient.ListLotAsync(resource.Id, amount);
            if (!res.Transport) return;   // про сеть уже рассказал NetStatus

            if (res.Value == null || !res.Value.ok)
            {
                Refuse(ListRefusal(res.Value != null ? res.Value.error : null));
                return;
            }

            // Сервер принял — теперь можно снимать. Снятие после ok не может не сойтись:
            // количество мы проверили, а склад в одном потоке с нами.
            storage.TryRemove(resource, amount);
            SaveRunner.SaveIfPossible("лот выставлен");

            NetStatus.Set("лот выставлен: " + amount + " × " + resource.DisplayName
                          + " (получишь " + res.Value.sellerGold + " зол.)");
            Raise(Changed);
        }

        /// <summary>
        /// Купить лот. Золото списывается только из ответа сервера: проигравший гонку
        /// за лот получает «уже купили» и не теряет ни монеты.
        /// </summary>
        public static async void Buy(MarketLot lot)
        {
            if (!Available(out string reason)) { Refuse(reason); return; }
            if (lot == null) return;

            var wallet = Wallet.Instance;
            if (wallet == null || !wallet.CanAfford(lot.gold))
            {
                Refuse("не хватает золота");
                return;
            }

            var res = await ApiClient.BuyLotAsync(lot.id);
            if (!res.Transport) return;

            if (res.Value == null || !res.Value.ok)
            {
                Refuse(res.Value != null && res.Value.error == "lot_sold"
                    ? "лот уже купили"
                    : "покупка не прошла: " + (res.Value != null ? res.Value.error : "нет ответа"));
                Raise(Changed);   // список устарел — пусть окно перечитает
                return;
            }

            var registry = ContentRegistry.Instance;
            var resource = registry != null ? registry.Resource(res.Value.resourceId) : null;

            // Списываем и зачисляем в одном кадре с ответом. Событие покупателю сервер
            // не шлёт — этот ответ и есть его единственная накладная.
            wallet.TrySpend(res.Value.gold);

            if (resource != null && res.Value.amount > 0)
                AddWithOverflow(resource, res.Value.amount);

            SaveRunner.SaveIfPossible("покупка на рынке");

            NetStatus.Set("куплено: " + res.Value.amount + " × "
                          + (resource != null ? resource.DisplayName : res.Value.resourceId));
            Raise(Changed);
        }

        /// <summary>
        /// Положить купленное на склад даже сквозь потолок. Отвергнуть оплаченное нельзя:
        /// «заплатил и не получил» хуже временно переполненного склада, который к тому же
        /// видно глазами. Тем же переливом живёт рюкзак фермера.
        /// </summary>
        internal static void AddWithOverflow(ResourceDefinition resource, int amount)
        {
            if (!(FarmingRuntime.Sink is Inventory storage)) { FarmingRuntime.Sink.Add(resource, amount); return; }

            bool overflow = storage.AllowOverflow;
            storage.AllowOverflow = true;
            storage.Add(resource, amount);
            storage.AllowOverflow = overflow;
        }

        private static string ListRefusal(string code)
        {
            switch (code)
            {
                case "too_many_lots": return "лотов уже " + 6 + " — дождись покупателей";
                case "not_in_snapshot": return "сервер ещё не видел этот запас — подожди сохранения";
                case "daily_limit": return "дневной оборот рынка исчерпан";
                case "no_catalog": return "рынок закрыт: на сервере нет каталога цен";
                case "bad_resource": return "это не продаётся на рынке";
                default: return "лот не принят: " + (code ?? "нет ответа");
            }
        }

        private static void Refuse(string reason)
        {
            NetStatus.Set(reason);

            var handler = Refused;
            if (handler == null) return;
            try { handler(reason); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void Raise(Action handler)
        {
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            Refused = null;
        }
    }
}

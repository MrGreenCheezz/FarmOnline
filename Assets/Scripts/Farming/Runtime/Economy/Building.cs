using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Поставленная постройка. Хранит уровень, тратит ресурсы на улучшение, обслуживает персонажа.
    /// <para>
    /// Обслуживание — «спроси сам», а не «раздаём всем»: постройка никогда не тянется к фермеру,
    /// фермер подходит и просит. Это держит цикл нужд на экране — игрок видит, как персонаж идёт
    /// поесть, а не как шкала наполняется по закадровым причинам.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Building")]
    public sealed class Building : MonoBehaviour
    {
        [SerializeField] private BuildingDefinition _definition;
        [SerializeField, Min(1)] private int _level = 1;

        /// <summary>Уровень вырос. Второй аргумент — новый уровень.</summary>
        public event Action<Building, int> Upgraded;

        /// <summary>Обслужила персонажа. Числа — реально восстановленные сытость и вода.</summary>
        public event Action<Building, float, float> Served;

        public BuildingDefinition Definition => _definition;
        public int Level => _level;
        public bool IsMaxLevel => _definition != null && _level >= _definition.MaxLevel;
        public float Efficiency => _definition != null ? _definition.EfficiencyAt(_level) : 0f;
        public BuildingService Service => _definition != null ? _definition.Service : BuildingService.Kitchen;

        /// <summary>Множитель скорости мастерской или надбавка рынка на текущем уровне.</summary>
        public float Output => _definition != null ? _definition.OutputAt(_level) : 0f;

        private void OnEnable() => BuildingRegistry.Register(this);
        private void OnDisable() => BuildingRegistry.Unregister(this);

        internal int RegistryIndex = -1;

        public void Configure(BuildingDefinition definition, int level = 1)
        {
            _definition = definition;
            _level = Mathf.Max(1, level);

            // Компонент регистрируется в OnEnable, то есть ещё пустым: Instantiate + AddComponent
            // происходят раньше, чем сюда попадают данные. Без этого оклика только что купленный
            // силос числился бы постройкой «без определения» и не давал бы ничего до следующей
            // покупки — а купившему кажется, что деньги ушли впустую.
            BuildingRegistry.NotifyChanged();
        }

        // ---- улучшение ----

        public bool CanUpgrade(out string reason)
        {
            reason = null;

            if (_definition == null) { reason = "нет данных постройки"; return false; }
            if (IsMaxLevel) { reason = "максимальный уровень"; return false; }

            var cost = _definition.CostOf(_level + 1);
            if (cost == null) { reason = "нет данных уровня"; return false; }

            int gold = Wallet.Instance != null ? Wallet.Instance.Gold : 0;
            return cost.Value.CanPay(gold, FarmingRuntime.Sink as IInventory, out reason);
        }

        public bool TryUpgrade()
        {
            if (!CanUpgrade(out _)) return false;

            var cost = _definition.CostOf(_level + 1).Value;

            // Золото первым: если кошелёк откажет, склад ещё не тронут.
            if (cost.Gold > 0 && (Wallet.Instance == null || !Wallet.Instance.TrySpend(cost.Gold))) return false;
            cost.ChargeResources(FarmingRuntime.Sink as IInventory);

            _level++;
            Raise(Upgraded, _level);
            BuildingRegistry.RaiseUpgraded(this, _level);
            return true;
        }

        // ---- обслуживание ----

        /// <summary>Может ли эта постройка прямо сейчас чем-то помочь этим нуждам?</summary>
        public bool CanServe(float satiety01, float hydration01)
        {
            if (_definition == null) return false;

            switch (_definition.Service)
            {
                case BuildingService.Well:
                    return hydration01 < 0.999f;

                case BuildingService.Kitchen:
                    // Кухне нужна еда на складе, иначе визит будет впустую
                    return satiety01 < 0.999f && FindBestFood(out _, out _);

                // Мастерская и рынок работают сами. Ходить к ним незачем, и фермер
                // не должен считать их подходящей целью, когда голоден.
                default:
                    return false;
            }
        }

        /// <summary>
        /// Обслужить один визит. Возвращает, сколько каждой нужды восстановлено, в единицах шкалы.
        /// <para>
        /// Уровень постройки — это ПОТОЛОК визита, а не размер порции: колодец 1-го уровня
        /// наполняет до 0.6 шкалы из любого дефицита, прокачанный — до полной. Прежняя
        /// фиксированная порция (max × 0.35) из утреннего нуля поднимала лишь до 0.35,
        /// и фермер жил в вечной полосе жажды, прерываясь по 3–5 раз за день.
        /// </para>
        /// <para>
        /// У кухни еда отдаёт ПОЛНУЮ питательность. Раньше Efficiency сидела и в цене единицы,
        /// и в цели — и сокращалась: кухня 1-го уровня забирала со склада столько же еды,
        /// сколько 4-го, а 65% съеденного просто исчезало.
        /// </para>
        /// </summary>
        public void Serve(float satiety, float maxSatiety, float hydration, float maxHydration,
                          out float satietyGain, out float hydrationGain)
        {
            satietyGain = 0f;
            hydrationGain = 0f;
            if (_definition == null) return;

            // До какой доли шкалы этот уровень постройки способен наполнить: 0.6 на нуле
            // умения, 1.0 на пределе. Нижняя планка чуть выше порога комфорта нужд — визит
            // обязан выводить из штрафной зоны, иначе походы не кончаются никогда. Лерп, а
            // не жёсткий пол: под полом ступени 1 и 2 были бы неотличимы, и апгрейд первого
            // уровня не менял бы ничего.
            float target01 = Mathf.Lerp(0.6f, 1f, _definition.BaseRestore * Efficiency);

            if (_definition.Service == BuildingService.Well)
            {
                hydrationGain = Mathf.Max(0f, maxHydration * target01 - hydration);
            }
            else if (_definition.Service == BuildingService.Kitchen && FindBestFood(out var food, out int available))
            {
                var storage = FarmingRuntime.Sink as IInventory;

                // Съедаем ровно столько, сколько закрывает дефицит до потолка кухни.
                float deficit = Mathf.Max(0f, maxSatiety * target01 - satiety);
                float perUnit = Mathf.Max(0.01f, food.Nutrition);
                int want = Mathf.CeilToInt(deficit / perUnit);
                if (want <= 0) return;

                int taken = storage.TryRemove(food, Mathf.Min(want, available));

                satietyGain = taken * perUnit;
                hydrationGain = taken * food.Hydration;
            }

            if (satietyGain > 0f || hydrationGain > 0f) Raise(Served, satietyGain, hydrationGain);
        }

        /// <summary>
        /// Самая дешёвая еда в наличии. Намеренно не самая сытная: дорогие ступени выгоднее
        /// продать, чем съесть, и фермер, который ест лучшее первым, сжигает прибыль игрока.
        /// </summary>
        private bool FindBestFood(out ResourceDefinition food, out int amount)
        {
            food = null;
            amount = 0;

            var storage = FarmingRuntime.Sink as IInventory;
            if (storage == null) return false;

            int bestPrice = int.MaxValue;
            var entries = storage.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var r = entries[i].Resource;
                if (r == null || !r.IsFood || entries[i].Amount <= 0) continue;
                if (r.SellPrice >= bestPrice) continue;

                bestPrice = r.SellPrice;
                food = r;
                amount = entries[i].Amount;
            }

            return food != null;
        }

        private void Raise<T1, T2>(Action<Building, T1, T2> handler, T1 a, T2 b)
        {
            if (handler == null) return;
            try { handler(this, a, b); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void Raise(Action<Building, int> handler, int arg)
        {
            if (handler == null) return;
            try { handler(this, arg); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }

    /// <summary>Живой список поставленных построек — фермер ищет по нему ближайшую полезную.</summary>
    public static class BuildingRegistry
    {
        private static readonly List<Building> _all = new List<Building>(16);

        public static IReadOnlyList<Building> All => _all;
        public static int Count => _all.Count;

        /// <summary>
        /// Какая-то постройка выросла в уровне. Общефермовый близнец <see cref="Building.Upgraded"/> —
        /// звук и UI подписываются один раз, а не гоняются за каждой новой постройкой.
        /// </summary>
        public static event Action<Building, int> Upgraded;

        /// <summary>
        /// Набор построек изменился: появилась, исчезла или выросла в уровне. Отдельно от
        /// <see cref="Upgraded"/>, потому что тому, кто считает общефермовые надбавки, важен
        /// сам факт, а не какая именно постройка, — и снос постройки для него такое же событие.
        /// </summary>
        public static event Action Changed;

        internal static void RaiseUpgraded(Building building, int level)
        {
            var handler = Upgraded;
            if (handler != null)
            {
                try { handler(building, level); }
                catch (Exception e) { Debug.LogException(e, building); }
            }

            RaiseChanged();
        }

        /// <summary>Сообщить, что состав или данные построек изменились.</summary>
        internal static void NotifyChanged() => RaiseChanged();

        private static void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        internal static void Register(Building b)
        {
            if (b == null || b.RegistryIndex >= 0) return;
            b.RegistryIndex = _all.Count;
            _all.Add(b);
            RaiseChanged();
        }

        internal static void Unregister(Building b)
        {
            if (b == null) return;

            int i = b.RegistryIndex;
            if (i < 0 || i >= _all.Count || _all[i] != b) { b.RegistryIndex = -1; return; }

            int last = _all.Count - 1;
            _all[i] = _all[last];
            if (_all[i] != null) _all[i].RegistryIndex = i;
            _all.RemoveAt(last);
            b.RegistryIndex = -1;
            RaiseChanged();
        }

        /// <summary>
        /// Добавочная доля к каждой продаже благодаря рынкам. Берётся лучший рынок, а не сумма:
        /// суммирование превратило бы спам ларьками во всю игру, а игрок должен улучшать один
        /// рынок, а не покупать двенадцать.
        /// </summary>
        public static float MarketBonus
        {
            get
            {
                float best = 0f;
                for (int i = 0; i < _all.Count; i++)
                {
                    var b = _all[i];
                    if (b == null || b.Service != BuildingService.Market) continue;
                    if (b.Output > best) best = b.Output;
                }
                return best;
            }
        }

        /// <summary>Ближайшая постройка, реально способная помочь этим нуждам прямо сейчас.</summary>
        public static Building FindNearestUseful(Vector3 position, float satiety01, float hydration01,
                                                 BuildingService? service = null)
        {
            Building best = null;
            float bestSqr = float.PositiveInfinity;

            for (int i = 0; i < _all.Count; i++)
            {
                var b = _all[i];
                if (b == null) continue;
                if (service.HasValue && b.Service != service.Value) continue;
                if (!b.CanServe(satiety01, hydration01)) continue;

                float sqr = (b.transform.position - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = b;
            }

            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            Upgraded = null;
            Changed = null;
        }
    }
}

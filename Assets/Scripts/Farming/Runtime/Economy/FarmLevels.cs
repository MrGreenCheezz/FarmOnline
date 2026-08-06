using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Уровень фермы — единственная обязательная лестница игры: сколько земли у игрока и
    /// докуда ему хватает места. Всё прочее (виды, постройки, декор) — добровольный выбор.
    /// <para>
    /// Статика по образцу <see cref="FarmProgress"/>: уровень — свойство фермы, а не чьего-то
    /// компонента в сцене, и спросить его должны уметь и магазин, и забор, и рельеф, ни на кого
    /// не ссылаясь.
    /// </para>
    /// </summary>
    public static class FarmLevels
    {
        /// <summary>Одна ступень: докуда раздвигается земля и чем за это платят.</summary>
        public readonly struct Step
        {
            /// <summary>Радиус территории, метров.</summary>
            public readonly float Radius;

            /// <summary>Золотая часть цены. Ресурсная — в <see cref="Materials"/>.</summary>
            public readonly int Gold;

            /// <summary>Сколько досок (или иного материала ступени) просит расширение.</summary>
            public readonly int Boards;

            /// <summary>Сколько ночной росы. Её нельзя купить — только собрать ночью самому.</summary>
            public readonly int Dew;

            /// <summary>Короткое обещание игроку: чем эта ступень хороша.</summary>
            public readonly string Promise;

            public Step(float radius, int gold, int boards, int dew, string promise)
            {
                Radius = radius;
                Gold = gold;
                Boards = boards;
                Dew = dew;
                Promise = promise;
            }
        }

        /// <summary>
        /// Шесть ступеней. Первая — то, с чем ферма живёт сейчас (13 м): старые партии обязаны
        /// остаться собой, а не съёжиться.
        /// <para>
        /// Радиус растёт на 3 метра за ступень — это примерно вдвое больше места на каждом шаге
        /// (площадь круга растёт быстрее радиуса), а золото втрое. Шаг цены 3.2 мягче магазинного
        /// 4.5 нарочно: обязательную лестницу нельзя ставить круче добровольной, иначе она
        /// читается как стена, а не как цель. Ориентир — двое суток дохода фермы этого уровня.
        /// </para>
        /// <para>
        /// Роса в цене — то, ради чего стоит выходить ночью: её не продают в лавке ни за какие
        /// деньги (см. правило 2 в CLAUDE.md — ночь принадлежит игроку). Числа растут медленнее
        /// золота: собирать росу руками дольше, чем зарабатывать монеты.
        /// </para>
        /// </summary>
        private static readonly Step[] Ladder =
        {
            new Step(13f,       0,   0,   0, "родная поляна"),
            new Step(16f,    1500,  20,  10, "место под первый сад"),
            new Step(19f,    4800,  35,  20, "хватит на загон со скотиной"),
            new Step(22f,   15400,  50,  35, "поле под дорогие ступени"),
            new Step(25f,   49000,  70,  60, "простор для мастерских"),
            new Step(28f,  157000, 100, 100, "вся долина твоя"),

            // Верх лестницы (06.08.2026): тот же шаг золота ×3.2 и те же 3 метра, что ниже, —
            // прогрессия не меняет характера, она просто продолжается. Роса растёт медленнее
            // золота и здесь: её собирают руками по ночам, и 320 капель — уже недели ритуала.
            new Step(31f,  500000, 130, 135, "за ограду, в перелесок"),
            new Step(34f, 1600000, 165, 180, "холмы под большой сад"),
            new Step(37f, 5100000, 210, 240, "своя сторона леса"),
            new Step(40f, 16000000, 265, 320, "земля до горизонта"),
        };

        public static int MaxLevel => Ladder.Length;

        /// <summary>Текущий уровень, с единицы.</summary>
        public static int Current { get; private set; } = 1;

        /// <summary>Радиус территории на текущем уровне.</summary>
        public static float Radius => StepOf(Current).Radius;

        /// <summary>Уровень куплен — HUD перерисовывает лестницу, оформители двигают край.</summary>
        public static event Action<int> Changed;

        public static bool IsMax => Current >= MaxLevel;

        public static Step StepOf(int level) => Ladder[Mathf.Clamp(level, 1, MaxLevel) - 1];

        /// <summary>Что стоит следующая ступень. Осмысленно только когда <see cref="IsMax"/> ложно.</summary>
        public static Step Next => StepOf(Mathf.Min(Current + 1, MaxLevel));

        /// <summary>
        /// Хватает ли на следующую ступень. Причина отказа — наружу строкой: молчаливое
        /// «кнопка не нажалась» здесь читалось бы как поломка.
        /// </summary>
        public static bool CanAfford(out string missing)
        {
            missing = null;

            if (IsMax)
            {
                missing = "ферма уже во всю долину";
                return false;
            }

            var step = Next;
            var wallet = Wallet.Instance;
            int gold = wallet != null ? wallet.Gold : 0;
            if (gold < step.Gold)
            {
                missing = "не хватает " + (step.Gold - gold) + " золота";
                return false;
            }

            var storage = FarmingRuntime.Sink as IInventory;
            if (!Enough(storage, BoardsResource, step.Boards, out missing)) return false;
            if (!Enough(storage, DewResource, step.Dew, out missing)) return false;

            return true;
        }

        /// <summary>
        /// Купить следующую ступень: списать цену и раздвинуть границы. Возвращает false, если
        /// не по карману, — вызывающий обязан показать <paramref name="refusal"/> игроку.
        /// </summary>
        public static bool TryBuyNext(out string refusal)
        {
            // Запрет живёт здесь, а не в кнопке: в гостях кошелёк и склад — хозяйские,
            // и нажатие списало бы чужое золото, подвинуло чужой забор и подменило нашу
            // статику уровня. Правило в системе, а не в интерфейсе, — иначе оно держится
            // ровно до первого нового способа нажать.
            if (GuestMode.IsGuest)
            {
                refusal = "в гостях ферму не расширяют";
                return false;
            }

            if (!CanAfford(out refusal)) return false;

            var step = Next;
            var wallet = Wallet.Instance;
            if (wallet == null || !wallet.TrySpend(step.Gold))
            {
                refusal = "кошелёк не отозвался";
                return false;
            }

            var storage = FarmingRuntime.Sink as IInventory;
            if (storage != null)
            {
                if (step.Boards > 0 && BoardsResource != null) storage.TryRemove(BoardsResource, step.Boards);
                if (step.Dew > 0 && DewResource != null) storage.TryRemove(DewResource, step.Dew);
            }

            Apply(Current + 1);
            refusal = null;
            return true;
        }

        /// <summary>Вернуть уровень из сохранения. Тихо: это восстановление, а не покупка.</summary>
        public static void RestoreState(int level) => Apply(level);

        private static void Apply(int level)
        {
            Current = Mathf.Clamp(level, 1, MaxLevel);

            // Границы — следствие уровня. Двигаем их здесь, чтобы не осталось места, где
            // уровень куплен, а земля прежняя.
            var bounds = FarmBounds.Instance;
            if (bounds != null) bounds.SetRadius(Radius);

            var handler = Changed;
            if (handler == null) return;
            try { handler(Current); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ---- материалы цены ----

        /// <summary>Строительный материал расширения — тот же, что идёт на постройки.</summary>
        private const string BoardsId = "plank";

        /// <summary>Ночная роса: единственная валюта, за которой надо выходить в ночь.</summary>
        private const string DewId = "moondew";

        public static ResourceDefinition BoardsResource => Resolve(BoardsId);
        public static ResourceDefinition DewResource => Resolve(DewId);

        private static ResourceDefinition Resolve(string id)
        {
            var registry = ContentRegistry.Instance;
            return registry != null ? registry.Resource(id) : null;
        }

        private static bool Enough(IInventory storage, ResourceDefinition resource, int amount, out string missing)
        {
            missing = null;
            if (amount <= 0) return true;

            // Материал не нашёлся в реестре — переименовали id, ассет выпал из ContentRegistry.
            // Промолчать значило бы продать ступень за одно золото: цена тихо теряет половину.
            if (resource == null)
            {
                Debug.LogError("[Ферма] Материал ступени не найден в реестре контента — расширение остановлено");
                missing = "не найден материал ступени — скажи об этом разработчику";
                return false;
            }

            int have = storage != null ? storage.GetAmount(resource) : 0;
            if (have >= amount) return true;

            missing = "не хватает " + (amount - have) + " — " + resource.DisplayName;
            return false;
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload — иначе вторая
        // партия начнётся с чужим уровнем и чужой землёй.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            Current = 1;
            Changed = null;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using Farm.Farming;

namespace Farm.Characters
{
    /// <summary>
    /// Дворы культур: у каждого вида грядок есть одно место на ферме, куда жители их сносят.
    /// <para>
    /// Раньше уборка искала не место, а <b>пару</b>: самую разъехавшуюся двойку одинаковых
    /// грядок. Пока одинаковых было ровно две, это работало; на трёх начинался маятник —
    /// поднёс А к Б, и самой дальней парой стали А и В, так что та же А ехала обратно.
    /// Игрок видел жителя, который таскает одно и то же растение из угла в угол: ровно тот
    /// случай, когда умное поведение читается поломкой.
    /// </para>
    /// <para>
    /// Здесь у вида есть <b>двор</b> — якорная грядка, вокруг которой уже стоит больше всего
    /// своих. Носят только снаружи внутрь, поэтому каждая ходка строго приближает грядку
    /// к якорю: маятник невозможен по построению, а не по удачному подбору чисел.
    /// </para>
    /// <para>
    /// Двор общий на ферму, а не свой у каждого жителя. Иначе двое с разными дворами
    /// растащили бы одну кучу в разные стороны, и каждый носил бы обратно за другим.
    /// </para>
    /// <para>
    /// Носят при этом только <b>одиночек</b>: грядку, у которой рядом уже стоит своя, житель
    /// не трогает, даже если её кучка не главная. Двор собирает разбредшихся, а не переселяет
    /// то, что игрок свёл своими руками, — см. <see cref="_lonely"/>.
    /// </para>
    /// </summary>
    public static class TidyGroups
    {
        /// <summary>
        /// Сколько секунд живёт разбор фермы на дворы. Секунда — не экономия, а устойчивость:
        /// двор, пересчитываемый на каждое раздумье, дрожал бы вместе с несомой ношей.
        /// </summary>
        private const double RefreshSeconds = 1.0;

        /// <summary>
        /// В скольких метрах грядки считаются стоящими одной кучей. В метрах, а не в шагах
        /// раскладки: где стоит двор — свойство фермы, и оно не должно меняться от того,
        /// кто из жителей спросил первым.
        /// </summary>
        private const float NeighbourRadius = 3.2f;

        private struct Grove
        {
            /// <summary>Грядка, вокруг которой стоит двор. К ней и носят.</summary>
            public Growable Anchor;

            /// <summary>Сколько своих стоит вокруг якоря — по этому числу он и выбран.</summary>
            public int Weight;

            /// <summary>Сколько грядок этого вида на ферме всего.</summary>
            public int Total;

            /// <summary>Прежний якорь и его нынешний вес — для липкости двора.</summary>
            public Growable Kept;
            public int KeptWeight;
        }

        private static readonly Dictionary<GrowableDefinition, Grove> _groves =
            new Dictionary<GrowableDefinition, Grove>(16);

        private static readonly Dictionary<GrowableDefinition, Growable> _previous =
            new Dictionary<GrowableDefinition, Growable>(16);

        private static readonly List<GrowableDefinition> _keys = new List<GrowableDefinition>(16);

        /// <summary>
        /// Грядки, у которых рядом нет ни одной своей. Носят только их: тот, кто уже стоит
        /// со своими, остаётся стоять, даже если его кучка не главная. Игрок сводит грядки
        /// под слияние собственными руками, и разлучить сведённую им пару значит отнять ход —
        /// та же граница, что стережёт <see cref="DragFocus.IsPlayerClaimed"/>, только без
        /// срока годности.
        /// </summary>
        private static readonly HashSet<Growable> _lonely = new HashSet<Growable>();

        private static double _refreshedAt = -1.0;

        /// <summary>
        /// Пересчитать дворы, если прошлый разбор устарел. Зовут все, кому нужен двор, —
        /// работа делается раз в <see cref="RefreshSeconds"/> на всю ферму, а не на жителя.
        /// <para>
        /// Проход квадратичный по числу грядок: при сотне это десять тысяч сравнений раз
        /// в секунду — дешевле одного кадра анимации, и мерить кучность дешевле нельзя,
        /// не заведя пространственную сетку ради задачи, которой она не нужна.
        /// </para>
        /// </summary>
        public static void Refresh()
        {
            double now = FarmingRuntime.Now;
            if (_refreshedAt > 0.0 && now - _refreshedAt < RefreshSeconds) return;
            _refreshedAt = now;

            // Прежние якоря — в сторону: без них двор переезжал бы от каждой мелочи.
            _previous.Clear();
            foreach (var pair in _groves)
                if (pair.Value.Anchor != null) _previous[pair.Key] = pair.Value.Anchor;

            _groves.Clear();
            _lonely.Clear();

            var all = GrowableRegistry.All;
            Vector3 center = FarmBounds.Instance != null ? FarmBounds.Instance.Center : Vector3.zero;
            float nearSqr = NeighbourRadius * NeighbourRadius;

            for (int i = 0; i < all.Count; i++)
            {
                var plot = all[i];
                if (!Counts(plot)) continue;

                var definition = plot.Definition;

                int weight = 0;
                for (int j = 0; j < all.Count; j++)
                {
                    var other = all[j];
                    if (other == plot || !Counts(other) || other.Definition != definition) continue;
                    if (Flat(other.transform.position - plot.transform.position) <= nearSqr) weight++;
                }

                if (weight == 0) _lonely.Add(plot);

                _groves.TryGetValue(definition, out var grove);
                grove.Total++;

                bool mayAnchor = MayAnchor(plot);

                if (mayAnchor && _previous.TryGetValue(definition, out var kept) && kept == plot)
                {
                    grove.Kept = plot;
                    grove.KeptWeight = weight;
                }

                if (mayAnchor && (grove.Anchor == null || Better(plot, weight, grove, center)))
                {
                    grove.Anchor = plot;
                    grove.Weight = weight;
                }

                _groves[definition] = grove;
            }

            // Липкость: пока прежний якорь не хуже нового, двор остаётся на месте. Без неё
            // две равные кучки перебрасывали бы двор туда-сюда, а жители носили бы следом —
            // тот же маятник, только раз в секунду.
            _keys.Clear();
            foreach (var key in _groves.Keys) _keys.Add(key);

            for (int i = 0; i < _keys.Count; i++)
            {
                var grove = _groves[_keys[i]];
                if (grove.Kept == null || grove.KeptWeight < grove.Weight) continue;

                grove.Anchor = grove.Kept;
                grove.Weight = grove.KeptWeight;
                _groves[_keys[i]] = grove;
            }
        }

        /// <summary>Якорь двора этого вида, или null, если двора нет.</summary>
        public static Growable AnchorFor(GrowableDefinition definition)
        {
            if (definition == null || !_groves.TryGetValue(definition, out var grove)) return null;

            // Якорь мог быть слит или снесён после разбора: до следующего пересчёта
            // двора у вида просто нет, и уборка этого вида замирает на секунду.
            return grove.Anchor != null ? grove.Anchor : null;
        }

        /// <summary>
        /// Насколько широк двор вида: в этом радиусе от якоря грядка считается на месте.
        /// <para>
        /// Радиус растёт от числа грядок вида и меряется шагами раскладки, потому что двор
        /// обязан вмещать всё, что в него принесут: место снаружи радиуса тут же снова
        /// читалось бы беспорядком, и житель носил бы одну и ту же грядку по кругу.
        /// Корень — от того, что грядки укладываются кольцами: место растёт как площадь.
        /// </para>
        /// <para>
        /// Числа подобраны так, чтобы колец хватало с запасом: при шаге 1.3 двадцать грядок
        /// вида получают радиус 4.6 — три кольца, 36 мест, — а сорок получают 5.5 и четыре
        /// кольца на 60 мест. Скупее нельзя: не нашедшая места грядка останется стоять
        /// в стороне молча, а молчаливый отказ на ферме считается дефектом.
        /// </para>
        /// </summary>
        public static float HomeRadius(GrowableDefinition definition, float step)
        {
            int total = definition != null && _groves.TryGetValue(definition, out var grove) ? grove.Total : 1;
            return HomeRadiusFor(total, step);
        }

        /// <summary>
        /// Отбилась ли грядка от своих — то есть есть ли смысл её переносить. Отбившаяся —
        /// это одинокая (<see cref="_lonely"/>) и стоящая вне двора: ни того, кто уже с
        /// компанией, ни того, кто дома, житель не трогает.
        /// <para>
        /// Про «можно ли трогать прямо сейчас» отвечает <c>FarmerBrain.CanTidy</c>: спелость
        /// и рука игрока — это «не сейчас», а не «прибрано».
        /// </para>
        /// </summary>
        public static bool IsAstray(Growable plot, float step)
        {
            if (!Counts(plot)) return false;
            if (!_lonely.Contains(plot)) return false;
            if (!_groves.TryGetValue(plot.Definition, out var grove)) return false;

            // Одинокой грядке не с кем стоять — беспорядка из одной не бывает.
            if (grove.Total < 2) return false;

            var anchor = grove.Anchor;
            if (anchor == null || anchor == plot) return false;

            float radius = HomeRadiusFor(grove.Total, step);
            return Flat(plot.transform.position - anchor.transform.position) > radius * radius;
        }

        private static float HomeRadiusFor(int total, float step) =>
            step * (1.4f + 0.45f * Mathf.Sqrt(Mathf.Max(1, total)));

        /// <summary>Считается ли грядка при разборе: посаженная и с известным видом.</summary>
        private static bool Counts(Growable plot) =>
            plot != null && plot.Definition != null && plot.Phase != GrowthPhase.Empty;

        /// <summary>
        /// Годится ли грядка в якоря. Несомая — нет: двор, который едет на руках у жителя,
        /// увёл бы за собой всех остальных.
        /// </summary>
        private static bool MayAnchor(Growable plot) => !DragFocus.IsDragged(plot.transform);

        /// <summary>
        /// Лучше ли этот кандидат нынешнего. Кучность решает всё; при равной кучности
        /// побеждает тот, кто ближе к середине фермы, — тай-брейк обязан быть общим для
        /// всех жителей, иначе «ближе ко мне» развело бы дворы по разным домам.
        /// </summary>
        private static bool Better(Growable plot, int weight, in Grove grove, Vector3 center)
        {
            if (weight != grove.Weight) return weight > grove.Weight;

            return Flat(plot.transform.position - center) <
                   Flat(grove.Anchor.transform.position - center);
        }

        private static float Flat(Vector3 delta)
        {
            delta.y = 0f;
            return delta.sqrMagnitude;
        }

        // Статики переживают перезапуск Play Mode при отключённом domain reload —
        // чистим явно, как TidyClaims и DragFocus.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _groves.Clear();
            _previous.Clear();
            _keys.Clear();
            _lonely.Clear();
            _refreshedAt = -1.0;
        }
    }
}

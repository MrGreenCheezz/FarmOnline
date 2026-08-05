using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Память земли о том, где ходят. Каждый ходок докладывает свои шаги, и клетка, по которой
    /// прошли достаточно, объявляется натоптанной — там появляется тропинка. Клетка, по которой
    /// ходить перестали, постепенно зарастает обратно.
    /// <para>
    /// Это самый дешёвый вид «фермер сам меняет ферму»: тропинки никто не решает проложить,
    /// они — след прожитой жизни. Дорожка от дома к колодцу существует потому, что он правда
    /// ходит от дома к колодцу; переставит игрок колодец — старая зарастёт, а к новому месту
    /// протопчется новая. Без зарастания ферма копила бы все маршруты за всю партию и к вечеру
    /// выглядела бы вытоптанной целиком.
    /// </para>
    /// <para>
    /// Статик без сцены, как <see cref="GrowableRegistry"/>: докладывают персонажи и животные
    /// (сборка Farming и её наследники), а рисует <see cref="FootpathLayer"/>. Разделение
    /// намеренное — сколько натоптано, это данные фермы, а как это выглядит, дело слоя.
    /// </para>
    /// </summary>
    public static class Footpaths
    {
        /// <summary>Размер клетки, метров. Шаг человека и есть масштаб тропинки.</summary>
        public const float CellSize = 1f;

        /// <summary>Сколько метров нужно пройти по клетке, чтобы она протопталась.</summary>
        public static float WearThreshold = 30f;

        /// <summary>
        /// Выше этого износ не копится. Потолок — не оптимизация, а игровое правило: он решает,
        /// <b>как долго держится</b> тропинка, по которой перестали ходить. Всё, что набрано
        /// сверх порога, тропинка тратит, оставаясь на полной силе, и только потом начинает
        /// бледнеть. Поэтому главная дорога живёт заметно дольше случайного крюка, хотя
        /// выглядят они одинаково.
        /// </summary>
        public static float WearCeiling = 90f;

        /// <summary>
        /// За сколько секунд полностью натоптанная клетка зарастает начисто. Отсчёт идёт по
        /// игровому времени (<see cref="FarmingRuntime.Now"/>), то есть только пока игра идёт.
        /// При сутках в 600 с это трое суток: сутки на видимое выцветание и двое про запас.
        /// </summary>
        public static float FadeSeconds = 1800f;

        /// <summary>
        /// Как часто пересматриваются клетки, по которым сейчас не ходят, секунд.
        /// Раз в пару секунд достаточно: зарастание идёт минутами, и чаще смотреть не на что.
        /// </summary>
        private const float SweepInterval = 2f;

        /// <summary>
        /// На сколько ступеней делится видимая сила тропинки. Нужна, чтобы слой не перерисовывал
        /// картинку на каждую сотую долю выцветания: между ступенями проходят десятки секунд,
        /// и перерисовки остаются редкими.
        /// </summary>
        private const int Steps = 8;

        /// <summary>Что известно про одну клетку.</summary>
        private struct Track
        {
            /// <summary>Накопленные метры.</summary>
            public float Wear;

            /// <summary>Когда по ней прошли в последний раз.</summary>
            public double Touched;

            /// <summary>Объявлена ли она тропинкой.</summary>
            public bool Worn;

            /// <summary>Видимая сила в ступенях — по ней решается, устарела ли картинка.</summary>
            public int Step;
        }

        /// <summary>Клетка-тропинка снаружи: где она и насколько ярко её рисовать.</summary>
        public readonly struct WornCell
        {
            public readonly Vector2Int Cell;

            /// <summary>0..1. Падает, пока клетка зарастает, — тропинка бледнеет, а не гаснет разом.</summary>
            public readonly float Strength;

            public WornCell(Vector2Int cell, float strength)
            {
                Cell = cell;
                Strength = strength;
            }
        }

        private static readonly Dictionary<Vector2Int, Track> _tracks = new Dictionary<Vector2Int, Track>(256);
        private static readonly List<Vector2Int> _scratch = new List<Vector2Int>(256);

        private static int _wornCount;
        private static double _lastSweep;

        /// <summary>Клетка протопталась. Аргумент — её центр в мире (высота — по рельефу).</summary>
        public static event Action<Vector3> Worn;

        /// <summary>
        /// Нарисованное устарело: что-то протопталось, заросло или сменило видимую силу.
        /// Слою достаточно этого одного события — он перерисовывает картинку целиком.
        /// </summary>
        public static event Action Changed;

        /// <summary>Сколько клеток сейчас считаются тропинками.</summary>
        public static int WornCount => _wornCount;

        /// <summary>Доложить о пройденном шаге. Вызывается из движения каждый кадр — потому и дёшево.</summary>
        public static void Report(Vector3 from, Vector3 to)
        {
            float stepped = (to - from).magnitude;
            if (stepped < 0.0001f) return;

            double now = FarmingRuntime.Now;

            var cell = new Vector2Int(
                Mathf.FloorToInt(to.x / CellSize),
                Mathf.FloorToInt(to.z / CellSize));

            _tracks.TryGetValue(cell, out Track track);

            // Зарастание с прошлого касания досчитывается здесь же, а не таймером: клетку,
            // по которой снова пошли, незачем было пересматривать всё время простоя.
            Fade(ref track, now);

            track.Wear = Mathf.Min(track.Wear + stepped, WearCeiling);
            track.Touched = now;

            bool wasWorn = track.Worn;
            if (!track.Worn && track.Wear >= WearThreshold)
            {
                track.Worn = true;
                _wornCount++;
            }

            bool stepChanged = Restep(ref track);
            _tracks[cell] = track;

            if (!wasWorn && track.Worn) Announce(cell);
            if (stepChanged || (!wasWorn && track.Worn)) Raise();

            // Обход держится на движении: пока кто-то ходит, зарастание идёт. Замрёт вся
            // ферма — обход встанет, но и досчитается он потом верно, потому что считает
            // по времени, а не по числу вызовов.
            Sweep(now);
        }

        /// <summary>
        /// Натоптанные клетки для сохранения: пары координат подряд (x, y, x, y…).
        /// <para>
        /// Размер набирается по ходу, а не из <see cref="WornCount"/>: счётчик — вещь, которую
        /// легко рассинхронить будущей правкой, и цена такой ошибки здесь — обвал сохранения,
        /// то есть потерянная партия. Лишний список на сохранении дешевле этого риска.
        /// </para>
        /// </summary>
        public static int[] CaptureWorn()
        {
            var packed = new List<int>(_wornCount * 2);

            foreach (var pair in _tracks)
            {
                if (!pair.Value.Worn) continue;
                packed.Add(pair.Key.x);
                packed.Add(pair.Key.y);
            }

            return packed.ToArray();
        }

        /// <summary>
        /// Вернуть тропинки из сохранения.
        /// <para>
        /// Износ восстанавливается ровно на пороге, а не под потолок. Так загруженная ферма
        /// не получает вечных дорог: те, которыми пользуются, тут же наберут запас обратно,
        /// а брошенные зарастут — то есть ровно то, ради чего зарастание и заводили. Сохранять
        /// сам износ не стали: формат остался парами координат и читает старые партии.
        /// </para>
        /// </summary>
        public static void RestoreWorn(int[] packed)
        {
            _tracks.Clear();
            _wornCount = 0;

            double now = FarmingRuntime.Now;
            _lastSweep = now;

            if (packed != null)
            {
                for (int i = 0; i + 1 < packed.Length; i += 2)
                {
                    var cell = new Vector2Int(packed[i], packed[i + 1]);
                    if (_tracks.ContainsKey(cell)) continue;

                    var track = new Track { Wear = WearThreshold, Touched = now, Worn = true };
                    Restep(ref track);
                    _tracks[cell] = track;
                    _wornCount++;

                    Announce(cell);
                }
            }

            Raise();
        }

        /// <summary>Все тропинки разом — слою, чтобы перерисовать картинку целиком.</summary>
        public static void CopyWornTo(List<WornCell> buffer)
        {
            buffer.Clear();

            foreach (var pair in _tracks)
            {
                if (!pair.Value.Worn) continue;
                buffer.Add(new WornCell(pair.Key, Strength(pair.Value.Wear)));
            }
        }

        // ---- внутреннее ----

        /// <summary>Убрать износ, набежавший за простой. Ноль метров в секунду не бывает — делим.</summary>
        private static void Fade(ref Track track, double now)
        {
            if (track.Touched <= 0.0) return;

            float idle = (float)(now - track.Touched);
            if (idle <= 0f) return;

            track.Wear -= idle * (WearCeiling / Mathf.Max(1f, FadeSeconds));
            if (track.Wear < 0f) track.Wear = 0f;

            track.Touched = now;
        }

        /// <summary>Видимая сила: полная, пока износ не упал ниже порога, дальше — на убыль.</summary>
        private static float Strength(float wear) => Mathf.Clamp01(wear / Mathf.Max(0.01f, WearThreshold));

        /// <summary>Пересчитать ступень видимой силы. true — картинка устарела.</summary>
        private static bool Restep(ref Track track)
        {
            int step = track.Worn ? Mathf.Max(1, Mathf.RoundToInt(Strength(track.Wear) * Steps)) : 0;
            if (step == track.Step) return false;

            track.Step = step;
            return true;
        }

        /// <summary>Пересмотреть клетки, по которым сейчас не ходят.</summary>
        private static void Sweep(double now)
        {
            if (now - _lastSweep < SweepInterval) return;
            _lastSweep = now;

            // Ключи вперёд, правки потом: словарь нельзя менять во время обхода.
            _scratch.Clear();
            foreach (var pair in _tracks) _scratch.Add(pair.Key);

            bool changed = false;

            for (int i = 0; i < _scratch.Count; i++)
            {
                var cell = _scratch[i];
                var track = _tracks[cell];

                Fade(ref track, now);

                if (track.Wear <= 0f)
                {
                    _tracks.Remove(cell);
                    if (!track.Worn) continue;

                    _wornCount--;
                    changed = true;
                    continue;
                }

                if (Restep(ref track)) changed = true;
                _tracks[cell] = track;
            }

            if (changed) Raise();
        }

        /// <summary>Сказать, где именно протопталось.</summary>
        private static void Announce(Vector2Int cell)
        {
            var handler = Worn;
            if (handler == null) return;

            var center = new Vector3((cell.x + 0.5f) * CellSize, 0f, (cell.y + 0.5f) * CellSize);
            center.y = FarmingRuntime.Ground.SampleHeight(center);

            try { handler(center); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void Raise()
        {
            var handler = Changed;
            if (handler == null) return;

            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tracks.Clear();
            _scratch.Clear();
            _wornCount = 0;
            _lastSweep = 0.0;

            Worn = null;
            Changed = null;

            WearThreshold = 30f;
            WearCeiling = 90f;
            FadeSeconds = 1800f;
        }
    }

    /// <summary>
    /// Рисует натоптанное: закрашивает вытоптанные места в карте, которую читает материал земли
    /// (шейдер <c>Farm/Ground</c>). Чистое оформление — удали, и тропинки просто перестанут быть
    /// видны, а земля вернётся к сплошной траве.
    /// <para>
    /// Раньше здесь на каждую клетку клалась отдельная плита. Плита квадратная и жёсткая, а
    /// рельеф волнистый: на стыках соседних проплешин торчали углы, и было видно, что тропинка
    /// собрана из деталей. Пятно в текстуре не имеет ни краёв, ни толщины, ни предела в сто
    /// сорок штук — и не спорит с рельефом, потому что рельеф несёт его на себе.
    /// </para>
    /// <para>
    /// Картинка перерисовывается <b>целиком</b>, а не мазок за мазком. Так она стала чистой
    /// функцией от данных: с зарастанием мазки не только добавляются, но и слабеют, а вычесть
    /// один мазок из складывающихся невозможно — на перекрёстке он перемешан с соседними.
    /// Перерисовка стоит одну очистку буфера и сотню-другую мазков, и случается она редко:
    /// <see cref="Footpaths.Changed"/> поднимается только когда видимая сила сменила ступень.
    /// </para>
    /// <para>
    /// Карта раздаётся глобально (<c>Shader.SetGlobalTexture</c>), а не свойством материала:
    /// так её сможет спросить и трава, и что угодно ещё, чему полагается расступаться перед
    /// тропинкой. Свойство материала перебило бы глобальное значение, поэтому в шейдере эти
    /// два имени намеренно не объявлены как Properties.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Footpath Layer")]
    public sealed class FootpathLayer : MonoBehaviour
    {
        [Tooltip("Сколько метров нужно находить по клетке, чтобы она протопталась.")]
        [SerializeField, Min(2f)] private float _wearThreshold = 30f;

        [Tooltip("Запас сверх порога. Он не делает тропинку заметнее — он решает, сколько она " +
                 "продержится, если по ней перестанут ходить. Главная дорога набирает его до " +
                 "конца, случайный крюк — почти нет, и зарастают они по-разному.")]
        [SerializeField, Min(2f)] private float _wearCeiling = 90f;

        [Tooltip("За сколько секунд полностью натоптанная клетка зарастает начисто. " +
                 "Игровое время: пока игра не идёт, ничего не зарастает. Сутки в игре — 600 с.")]
        [SerializeField, Min(10f)] private float _fadeSeconds = 1800f;

        [Header("Область памяти (читается при включении слоя)")]
        [Tooltip("Центр области, которая помнит тропинки, в мировых координатах (x, z). " +
                 "Ферма стоит в начале координат, поэтому по умолчанию ноль.")]
        [SerializeField] private Vector2 _areaCenter = Vector2.zero;

        [Tooltip("Сторона этой области, метров. За её пределами тропинки не рисуются — " +
                 "и об этом будет сказано в консоли, а не молча.")]
        [SerializeField, Min(16f)] private float _areaSize = 80f;

        [Tooltip("Пикселей карты на метр. Вдвое больше — вчетверо больше памяти: " +
                 "80 м при 8 пикс/м это карта 640×640, около 400 КБ.")]
        [SerializeField, Range(2, 16)] private int _pixelsPerMeter = 8;

        [Header("Мазок")]
        [Tooltip("Радиус пятна от одной клетки, метров. Меньше метра нельзя: клетки по " +
                 "диагонали отстоят на 1.4 м, и от мазков поменьше тропинка выходит цепочкой " +
                 "бусин вместо ленты.")]
        [SerializeField, Range(0.2f, 3f)] private float _brushRadius = 1.15f;

        [Tooltip("Какая доля радиуса закрашена насухо. Нужна ровно затем же: у мазка-конуса " +
                 "соседи по диагонали складываются в четверть силы и не дотягивают до порога.")]
        [SerializeField, Range(0f, 0.9f)] private float _brushCore = 0.3f;

        private static readonly int FootpathMapId = Shader.PropertyToID("_FootpathMap");
        private static readonly int FootpathAreaId = Shader.PropertyToID("_FootpathArea");

        private readonly List<Footpaths.WornCell> _cells = new List<Footpaths.WornCell>(256);

        private Texture2D _map;
        private byte[] _pixels;
        private int _side;
        private float _pixelsPerMeterActual;
        private Vector2 _origin;
        private bool _dirty;
        private bool _toldAboutOutside;

        private void OnEnable()
        {
            ApplyNumbers();
            Footpaths.Changed += OnChanged;

            Build();
            _dirty = true;   // сцена могла загрузиться уже с тропинками
        }

        private void OnDisable()
        {
            Footpaths.Changed -= OnChanged;

            // Земля должна вернуться к траве, а не держать ссылку на уничтоженную текстуру.
            // Нулевой `w` — это и есть «слоя нет» для шейдера.
            Shader.SetGlobalVector(FootpathAreaId, Vector4.zero);
            Shader.SetGlobalTexture(FootpathMapId, null);

            if (_map == null) return;

            if (Application.isPlaying) Destroy(_map);
            else DestroyImmediate(_map);

            _map = null;
            _pixels = null;
        }

        private void OnValidate() => ApplyNumbers();

        private void OnChanged() => _dirty = true;

        private void LateUpdate()
        {
            // Одна отправка в видеопамять на кадр, сколько бы клеток за него ни сменилось.
            if (!_dirty || _map == null) return;

            Repaint();

            _map.SetPixelData(_pixels, 0);
            _map.Apply(false, false);
            _dirty = false;
        }

        private void ApplyNumbers()
        {
            Footpaths.WearThreshold = Mathf.Max(2f, _wearThreshold);
            Footpaths.WearCeiling = Mathf.Max(Footpaths.WearThreshold, _wearCeiling);
            Footpaths.FadeSeconds = Mathf.Max(10f, _fadeSeconds);
        }

        /// <summary>Завести карту и сказать шейдерам, какому куску мира она соответствует.</summary>
        private void Build()
        {
            // Байт на пиксель — весь смысл затеи: 640×640 это 400 КБ, а в RGBA было бы полтора
            // мегабайта. Если платформа такого не умеет, тропинок просто не будет, и лучше
            // узнать об этом строкой в консоли, чем разглядывая пустой луг.
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.R8))
            {
                Debug.LogError("[Тропинки] Платформа не поддерживает однобайтовые текстуры (R8) — " +
                               "тропинки рисоваться не будут", this);
                return;
            }

            _side = Mathf.Clamp(Mathf.RoundToInt(_areaSize * _pixelsPerMeter), 64, 2048);
            _pixelsPerMeterActual = _side / _areaSize;
            _origin = _areaCenter - new Vector2(_areaSize, _areaSize) * 0.5f;

            // Линейная, не sRGB: это не картинка, а величина «насколько тут натоптано».
            _map = new Texture2D(_side, _side, TextureFormat.R8, false, true)
            {
                name = "Footpaths",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            _pixels = new byte[_side * _side];
            _map.SetPixelData(_pixels, 0);
            _map.Apply(false, false);

            Shader.SetGlobalTexture(FootpathMapId, _map);
            Shader.SetGlobalVector(FootpathAreaId,
                new Vector4(_origin.x, _origin.y, 1f / _areaSize, 1f));
        }

        /// <summary>Собрать картинку заново по всем натоптанным клеткам.</summary>
        private void Repaint()
        {
            Array.Clear(_pixels, 0, _pixels.Length);
            Footpaths.CopyWornTo(_cells);

            bool outside = false;

            for (int i = 0; i < _cells.Count; i++)
            {
                var worn = _cells[i];
                float x = (worn.Cell.x + 0.5f) * Footpaths.CellSize;
                float z = (worn.Cell.y + 0.5f) * Footpaths.CellSize;

                if (!Stamp(x, z, worn.Strength)) outside = true;
            }

            if (!outside || _toldAboutOutside) return;

            // Молчаливый отказ — тоже отказ. Один раз сказать вслух дешевле, чем однажды
            // полдня искать, почему в дальнем углу фермы тропинки не появляются.
            _toldAboutOutside = true;
            Debug.Log("[Тропинки] Натоптано за пределами области памяти (" + _areaSize +
                      " м вокруг " + _areaCenter + ") — здесь тропинки не рисуются", this);
        }

        /// <summary>Положить мазок. false — клетка целиком за пределами карты.</summary>
        private bool Stamp(float worldX, float worldZ, float strength)
        {
            // Координаты центра клетки в пикселях: у пикселя с индексом i центр приходится
            // ровно на i, поэтому полпикселя вычитается сразу.
            float cx = (worldX - _origin.x) * _pixelsPerMeterActual - 0.5f;
            float cy = (worldZ - _origin.y) * _pixelsPerMeterActual - 0.5f;
            float radius = _brushRadius * _pixelsPerMeterActual;

            int minX = Mathf.Max(0, Mathf.CeilToInt(cx - radius));
            int maxX = Mathf.Min(_side - 1, Mathf.FloorToInt(cx + radius));
            int minY = Mathf.Max(0, Mathf.CeilToInt(cy - radius));
            int maxY = Mathf.Min(_side - 1, Mathf.FloorToInt(cy + radius));

            if (minX > maxX || minY > maxY) return false;

            float core = radius * _brushCore;
            float invSlope = 1f / Mathf.Max(0.0001f, radius - core);

            for (int y = minY; y <= maxY; y++)
            {
                int row = y * _side;
                float dy = y - cy;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx;
                    float k = (radius - Mathf.Sqrt(dx * dx + dy * dy)) * invSlope;
                    if (k <= 0f) continue;
                    if (k > 1f) k = 1f;

                    // Плавный спад к краю мазка: у линейного конуса виден острый кончик,
                    // и одинокая клетка читается как капля, а не как вытоптанное место.
                    k = k * k * (3f - 2f * k) * strength;

                    // Складываем, а не заменяем: там, где тропинки пересекаются, натоптано
                    // сильнее — перекрёсток и должен быть заметнее своих дорожек.
                    int i = row + x;
                    int sum = _pixels[i] + Mathf.RoundToInt(k * 255f);
                    _pixels[i] = (byte)(sum > 255 ? 255 : sum);
                }
            }

            return true;
        }
    }
}

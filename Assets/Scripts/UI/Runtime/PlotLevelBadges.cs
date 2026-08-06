using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Вешает плашку над каждой грядкой: чем она занята и какого уровня — игрок с одного взгляда
    /// видит и что с чем сливается, и что вообще растёт.
    /// <para>
    /// Один слой с пулом плашек вместо world-space канваса на каждую грядку: плашки должны быть
    /// выровнены по экрану и читаться под любым углом камеры, а канвас на грядку добавил бы
    /// рендерер и draw call тому, что всегда было парой символов.
    /// </para>
    /// <para>
    /// Иконка появилась не для красоты. Восемь пород дерева стоят на ферме одной моделью,
    /// различаясь приглушённым оттенком, и одна цифра уровня над ними не отвечала на главный
    /// вопрос — <i>что это</i>. Иконка ресурса берётся та же, что в инвентаре, поэтому цвет
    /// фишки связывает грядку в мире со строкой на складе без единого слова.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Plot Level Badges")]
    public sealed class PlotLevelBadges : MonoBehaviour
    {
        [SerializeField] private Camera _camera;

        [Tooltip("На какой высоте над грядкой висит плашка.")]
        [SerializeField] private float _worldHeight = 1.15f;

        [Tooltip("Дальше этого расстояния от камеры плашки не рисуются.")]
        [SerializeField, Min(1f)] private float _maxDistance = 45f;

        [Tooltip("Показывать плашки. Переключается кнопкой в HUD.")]
        [SerializeField] private bool _visible = true;

        [Tooltip("На сколько пикселей спрятанная плашка обязана отойти от панели, чтобы вернуться.\n" +
                 "Ноль означает дребезг: камера идёт плавно, и плашка моргает, пока пересекает кромку.")]
        [SerializeField, Min(0f)] private float _blockSlack = 8f;

        /// <summary>Одна плашка: строка из иконки, уровня и отсчёта, под ней — полоса роста.</summary>
        private sealed class Badge
        {
            public VisualElement Root;
            public VisualElement Icon;
            public Label Level;
            public Label Quality;
            public Label Time;
            public VisualElement Bar;
            public VisualElement Fill;

            /// <summary>Что уже написано и налито. Присваивать текст и ширину каждый кадр
            /// значит перестраивать раскладку полутора десятков плашек на ровном месте.</summary>
            public string TimeText;
            public int FillPercent = -1;

            /// <summary>Спрятана ли под HUD. Помнится ради несимметричного порога — см. LateUpdate.</summary>
            public bool Blocked;
        }

        private VisualElement _layer;
        private readonly List<Badge> _pool = new List<Badge>();

        /// <summary>Куски HUD, под которые плашки не заезжают: полоса кнопок и колонка панелей.</summary>
        private readonly List<VisualElement> _blockers = new List<VisualElement>();

        /// <summary>Окна поверх мира. Пока открыто хоть одно, плашки уходят целиком.</summary>
        private readonly List<VisualElement> _overlays = new List<VisualElement>();

        /// <summary>
        /// Видны ли плашки. Выключенные не просто прячутся — вся покадровая работа
        /// пропускается: на большой ферме это десятки проекций в кадр впустую.
        /// </summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (!_visible) HideFrom(0);
            }
        }

        public void Toggle() => Visible = !_visible;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            _layer = root?.Q<VisualElement>("world-badges");

            // Мировой слой объявлен первым ребёнком, но рисуется поверх панелей: порядок
            // отрисовки у UI Toolkit — порядок в дереве, а HUD лежит ниже. Значит прятать
            // плашки приходится нам самим — иначе цифра уровня повисает на кнопке топбара
            // и читается как ошибка сборки.
            //
            // Собираем по КЛАССУ, а не по списку имён. Список имён однажды уже подвёл:
            // новое окно «Дела» появилось в тот же день и в него не записалось, а пятнадцать
            // плашек повисли ровно поверх него. Правило «мир отступает под окном» не должно
            // зависеть от того, вспомнил ли автор нового окна про этот файл.
            _overlays.Clear();
            if (root != null)
            {
                root.Query<VisualElement>(className: "overlay").ForEach(_overlays.Add);

                // Панель постройки — окно по сути, но своего класса overlay не носит:
                // она без затемнения и живёт у правого края, чтобы игрок видел саму постройку.
                var building = root.Q<VisualElement>("building-panel");
                if (building != null) _overlays.Add(building);
            }

            // Запретные зоны: полоса кнопок и колонка панелей. Раньше здесь был один топбар —
            // при неподвижной камере до колонки плашки просто не доставали. С панорамой и зумом
            // достают, и цифра ложится на «ФЕРМЕР» ничуть не реже.
            _blockers.Clear();
            Add(root?.Q<VisualElement>("topbar"));
            Add(root?.Q<VisualElement>("hud-panels"));

            if (_camera == null) _camera = Camera.main;
            if (_layer == null) enabled = false;

            void Add(VisualElement element)
            {
                if (element != null) _blockers.Add(element);
            }
        }

        /// <summary>Открыто ли хоть одно окно поверх мира.</summary>
        private bool AnyWindowOpen()
        {
            for (int i = 0; i < _overlays.Count; i++)
            {
                var overlay = _overlays[i];
                if (overlay != null && overlay.resolvedStyle.display != DisplayStyle.None) return true;
            }

            return false;
        }

        /// <summary>Лежит ли плашка на каком-нибудь куске HUD.</summary>
        private bool BlockedBy(Rect badge)
        {
            for (int i = 0; i < _blockers.Count; i++)
            {
                var blocker = _blockers[i];
                if (blocker == null || blocker.resolvedStyle.display == DisplayStyle.None) continue;

                var bounds = blocker.worldBound;
                if (bounds.height > 0f && bounds.Overlaps(badge)) return true;
            }

            return false;
        }

        private void OnDisable() => HideFrom(0);

        // LateUpdate: грядки к этому моменту уже сдвинуты перетаскиванием, плашки не отстают на кадр.
        private void LateUpdate()
        {
            if (!_visible || _camera == null || _layer == null) return;

            var panel = _layer.panel;
            if (panel == null) return;

            // Открыто окно — мир под ним отступает целиком. Иначе полтора десятка ярких
            // плашек висят поверх затемнения и спорят с тем, ради чего окно открыли.
            if (AnyWindowOpen()) { HideFrom(0); return; }

            var plots = GrowableRegistry.All;
            float maxSqr = _maxDistance * _maxDistance;
            Vector3 camPos = _camera.transform.position;

            for (int i = 0; i < plots.Count; i++)
            {
                // Слот пула закреплён за грядкой по её месту в реестре, а не за счётчиком
                // показанных. Со счётчиком любая плашка, ушедшая под HUD, сдвигала все
                // следующие на слот назад: содержимое слотов менялось, вместе с ним менялась
                // ширина — а позиция считается по ширине прошлого кадра. Это и было мерцание
                // на краю экрана при движении камеры.
                var badge = GetBadge(i);
                var plot = plots[i];

                if (plot == null || plot.Phase == GrowthPhase.Empty) { Hide(badge); continue; }

                Vector3 world = plot.transform.position + Vector3.up * _worldHeight;
                if ((world - camPos).sqrMagnitude > maxSqr) { Hide(badge); continue; }

                // Позади камеры рисовать нечего — иначе плашка «отразится» на другую сторону экрана.
                if (_camera.WorldToViewportPoint(world).z <= 0f) { Hide(badge); continue; }

                // Привязываемся точкой над грядкой, а центрирование отдано раскладке
                // (translate в процентах, см. Make). Считать сдвиг самим значило бы спрашивать
                // ширину, которой на кадре смены содержимого ещё нет.
                Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, _camera);
                badge.Root.style.left = point.x;
                badge.Root.style.top = point.y;

                int level = plot.Level;
                badge.Level.text = level.ToString();
                ApplyLevelClass(badge.Root, level);

                // Качество земли — звёздами, отдельно от уровня: уровень говорит «с чем сливать»,
                // звезда — «эту грядку выхаживали». Пустая строка сжимается раскладкой сама.
                int bonus = plot.QualityBonus;
                badge.Quality.text = bonus >= 2 ? "★★" : bonus == 1 ? "★" : "";

                // Иконка есть не у всего: у жилы или дерева без назначенного ресурса плашка
                // просто остаётся цифрой, а не показывает пустой квадрат.
                var icon = IconOf(plot);
                badge.Icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : StyleKeyword.None;
                badge.Icon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;

                // Готовность подсвечиваем фоном, а не рамкой — рамка держит цвет уровня,
                // иначе, увидев «готово», игрок терял бы информацию о том, с чем это сливать.
                badge.Root.EnableInClassList("badge--ready", plot.IsReady);

                ApplyWait(badge, plot);

                badge.Root.style.display = DisplayStyle.Flex;

                // Заехала под HUD — не показываем. Прятать обязаны visibility, а не display:
                // выключенная из раскладки плашка теряет измеренный размер, и на следующем
                // кадре проверка пересечения видела нулевой прямоугольник — «свободно» —
                // показывала — раскладка меряла — «занято» — прятала. Это и было мерцание
                // на кромке панелей. Невидимая плашка размер сохраняет, и решение стоит.
                //
                // Порог несимметричен: спрятанная возвращается, лишь отойдя от панели на
                // _blockSlack, — ровный порог дребезжит на плавном ходе камеры.
                var size = badge.Root.resolvedStyle;
                bool measured = !float.IsNaN(size.width) && size.width > 0f;

                if (measured)
                {
                    var rect = new Rect(point.x - size.width * 0.5f, point.y - size.height, size.width, size.height);
                    float slack = badge.Blocked ? _blockSlack : 0f;

                    badge.Blocked = BlockedBy(new Rect(rect.x - slack, rect.y - slack,
                                                       rect.width + slack * 2f, rect.height + slack * 2f));
                }
                // Неизмеренная (первый кадр жизни) держит прошлое решение — гадать нечем.

                badge.Root.style.visibility = badge.Blocked ? Visibility.Hidden : Visibility.Visible;
            }

            HideFrom(plots.Count);
        }

        private static void Hide(Badge badge)
        {
            badge.Root.style.display = DisplayStyle.None;
            badge.Root.style.visibility = Visibility.Visible;
            badge.Blocked = false;
        }

        /// <summary>
        /// Долго ли ещё ждать. Отсчёт и полоса живут только у растущей грядки: у спелой их
        /// нет вовсе, и это не экономия места, а разница, которую видно боковым зрением —
        /// спелые отличаются от остальных не только цветом фона, но и формой плашки.
        /// <para>
        /// Полоса и цифры отвечают на разные вопросы и потому стоят вместе: полоса —
        /// «скоро ли» одним взглядом через всё поле, цифры — «успею ли до автобуса».
        /// </para>
        /// </summary>
        private static void ApplyWait(Badge badge, Growable plot)
        {
            double wait = plot.IsReady ? 0.0 : plot.TimeUntilReady;

            // -1 значит «ничего не растёт»: увядшая или замершая грядка. Врать ей отсчётом
            // хуже, чем молчать, — игрок пошёл бы ждать урожай, которого не будет.
            if (plot.IsReady || wait < 0.0)
            {
                badge.Time.style.display = DisplayStyle.None;
                badge.Bar.style.display = DisplayStyle.None;
                return;
            }

            string text = FormatWait(wait);
            if (badge.TimeText != text)
            {
                badge.Time.text = text;
                badge.TimeText = text;
            }

            int percent = Mathf.Clamp(Mathf.RoundToInt(plot.Progress01 * 100f), 0, 100);
            if (badge.FillPercent != percent)
            {
                badge.Fill.style.width = Length.Percent(percent);
                badge.FillPercent = percent;
            }

            badge.Time.style.display = DisplayStyle.Flex;
            badge.Bar.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Сколько ждать, словами. Округление всегда вверх: «1 м» на грядке, которой осталось
        /// пять секунд, читается как обман, а лишние пять секунд ожидания — нет.
        /// <para>
        /// Дальше десяти часов минуты не показываются: за таким урожаем возвращаются не
        /// «через 11 ч 20 м», а завтра, и вторая цифра только съедает место на плашке.
        /// </para>
        /// </summary>
        private static string FormatWait(double seconds)
        {
            if (seconds < 60.0) return Mathf.CeilToInt((float)seconds) + " с";
            if (seconds < 3600.0) return Mathf.CeilToInt((float)(seconds / 60.0)) + " м";

            int hours = (int)(seconds / 3600.0);
            int minutes = Mathf.CeilToInt((float)((seconds - hours * 3600.0) / 60.0));
            if (minutes >= 60) { hours++; minutes = 0; }

            return hours >= 10 || minutes == 0 ? hours + " ч" : hours + " ч " + minutes + " м";
        }

        /// <summary>Иконка того, что грядка производит. Null — показывать нечего.</summary>
        private static Sprite IconOf(Growable plot)
        {
            var resource = plot.Definition != null ? plot.Definition.YieldResource : null;
            return resource != null ? resource.Icon : null;
        }

        private Badge GetBadge(int index)
        {
            while (_pool.Count <= index)
            {
                // Мир не отвечает на клик, и плашка тоже: правило «отвечает на клик — ловит
                // клик» держится одним проходом, а не разметкой каждого нового элемента.
                var root = Make<VisualElement>("badge");

                // Плашка привязана точкой над грядкой и сама сдвигается на полширины влево
                // и на всю высоту вверх. Раскладка знает свой размер в том же кадре, в котором
                // он изменился, — а код, спрашивающий resolvedStyle, всегда знает вчерашний.
                root.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-100f));

                var row = Make<VisualElement>("badge__row");
                root.Add(row);

                var icon = Make<VisualElement>("badge__icon");
                row.Add(icon);

                var level = Make<Label>("badge__level");
                row.Add(level);

                var quality = Make<Label>("badge__quality");
                row.Add(quality);

                var time = Make<Label>("badge__time");
                row.Add(time);

                var bar = Make<VisualElement>("badge__bar");
                var fill = Make<VisualElement>("badge__fill");
                bar.Add(fill);
                root.Add(bar);

                _layer.Add(root);
                _pool.Add(new Badge { Root = root, Icon = icon, Level = level, Quality = quality, Time = time, Bar = bar, Fill = fill });
            }

            return _pool[index];
        }

        private static T Make<T>(string className) where T : VisualElement, new()
        {
            var element = new T { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            return element;
        }

        private void HideFrom(int index)
        {
            for (int i = index; i < _pool.Count; i++) _pool[i].Root.style.display = DisplayStyle.None;
        }

        private static void ApplyLevelClass(VisualElement badge, int level)
        {
            int tier = Mathf.Clamp(level, 1, 5);
            for (int i = 1; i <= 5; i++) badge.EnableInClassList("badge--lvl" + i, i == tier);
        }
    }
}

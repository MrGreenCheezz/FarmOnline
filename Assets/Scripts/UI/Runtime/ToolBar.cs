using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Панель инструментов: чем игрок сейчас трогает ферму и сколько у него ведёр, мешков
    /// и мерок удобрения.
    /// <para>
    /// Инструмент берут двумя способами, и оба ведут в одну точку — <see cref="PlotDragger.ApplyToolAt"/>.
    /// Нажатие оставляет инструмент в руке: поливать десять грядок подряд, каждый раз
    /// возвращаясь к панели, — работа, а не игра. Перетаскивание иконки прямо на цель
    /// применяет инструмент разово и возвращает руку: это жест «донёс ведро и поставил»,
    /// после него игрок обычно собирает, а не поливает дальше.
    /// </para>
    /// <para>
    /// Каждая иконка — <c>Button</c>, и это не стиль, а требование: <c>GameHud.MakeReadout</c>
    /// снимает picking со всего поддерева HUD и возвращает его только кнопкам, поэтому
    /// иконка-<c>VisualElement</c> молча не получила бы ни одного события.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Tool Bar")]
    public sealed class ToolBar : MonoBehaviour
    {
        private VisualElement _root;
        private VisualElement _bar;
        private VisualElement _ghost;

        /// <summary>Карточка инструмента: сама кнопка и её счётчик зарядов.</summary>
        private struct Card
        {
            public Button Button;
            public Label Count;
        }

        private Card _hand;
        private Card _move;
        private Card _water;
        private Card _feed;
        private Card _fert;

        /// <summary>Указатель, которым тащат прямо сейчас. Отдельное поле, а не «инструмент
        /// равен Hand»: рука — такой же инструмент, и сентинелом ей быть нельзя.</summary>
        private const int NoPointer = -1;
        private int _dragPointer = NoPointer;
        private FarmToolKind _dragging = FarmToolKind.Hand;

        /// <summary>Прибрать перетаскивание: отпустить захват, убрать призрак, забыть указатель.</summary>
        private void EndDrag(Button button, int pointerId)
        {
            if (_dragPointer != pointerId) return;

            if (button != null && button.HasPointerCapture(pointerId)) button.ReleasePointer(pointerId);
            HideGhost();
            _dragPointer = NoPointer;
            _dragging = FarmToolKind.Hand;
        }

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>()?.rootVisualElement;
            if (_root == null) { enabled = false; return; }

            _bar = _root.Q<VisualElement>("toolbar");
            if (_bar == null) { enabled = false; return; }

            _hand = Find("hand");
            _move = Find("move");
            _water = Find("water");
            _feed = Find("feed");
            _fert = Find("fert");

            // Иконки задаются стилями (GameHud.uss, .tool__icon--*), а не кодом: спрайт,
            // взятый через AssetDatabase, живёт только в редакторе и исчез бы в сборке —
            // весь остальной скин проекта тоже приходит из USS.
            Wire(_hand.Button, FarmToolKind.Hand);
            Wire(_move.Button, FarmToolKind.Move);
            Wire(_water.Button, FarmToolKind.Water);
            Wire(_feed.Button, FarmToolKind.Feed);
            Wire(_fert.Button, FarmToolKind.Fertilizer);

            FarmTool.Changed += OnToolChanged;
            FarmWater.Changed += Refresh;
            FarmFeed.Changed += Refresh;
            FarmFertilizer.Changed += Refresh;
            GuestMode.Changed += Refresh;
            BuildingRegistry.Changed += Refresh;

            Refresh();
        }

        private void OnDisable()
        {
            FarmTool.Changed -= OnToolChanged;
            FarmWater.Changed -= Refresh;
            FarmFeed.Changed -= Refresh;
            FarmFertilizer.Changed -= Refresh;
            GuestMode.Changed -= Refresh;
            BuildingRegistry.Changed -= Refresh;
        }

        private void Wire(Button button, FarmToolKind tool)
        {
            if (button == null) return;

            button.clicked += () => FarmTool.Current = tool;

            // Перетаскивание — на той же кнопке, что и нажатие: два разных элемента под
            // одно действие разошлись бы на первой правке. CapturePointer держит указатель
            // за кнопкой, пока палец в пути, иначе призрак теряется на первом же движении.
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                // Второй палец, пока первый в пути, игнорируем: одно перетаскивание за раз,
                // иначе колбэк первого выйдет по сторожу и оставит призрак висеть навсегда.
                if (_dragPointer != NoPointer) return;

                _dragging = tool;
                _dragPointer = e.pointerId;
                button.CapturePointer(e.pointerId);
                // Имя берём у самого инструмента: текст с карточки ушёл в подпись-ребёнка,
                // и button.text теперь пуст — призрак остался бы безымянным пятном.
                ShowGhost(FarmTool.NameOf(tool), e.position);
            });

            button.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragPointer != e.pointerId) return;
                MoveGhost(e.position);
            });

            button.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragPointer != e.pointerId) return;

                EndDrag(button, e.pointerId);

                // Отпустили над ПАНЕЛЬЮ (любой её точкой, включая зазоры между кнопками) —
                // это выбор инструмента, а не сброс на ферму. Сравнение с одной кнопкой
                // отправляло промах мимо её края прямиком в грядку под панелью: заряд
                // списывался за подложкой, и игрок не видел ничего (блокер судей).
                if (_bar != null && _bar.worldBound.Contains(e.position))
                {
                    FarmTool.Current = tool;   // соскользнувший тап всё равно выбирает инструмент
                    return;
                }

                // Точку экрана возьмёт слой ввода: панель не знает Input System и не должна
                // пересчитывать координаты панели обратно в экранные (обратного хелпера
                // в проекте нет, а инверсия на масштабируемой PanelSettings врёт в портрете).
                FarmTool.Current = tool;
                FarmTool.RequestApply();

                // Донёс и поставил — рука возвращается: следующий жест после полива почти
                // всегда «собрать», и оставленное ведро снова сделало бы клик поливом.
                FarmTool.Reset();
            });

            // Жест могут отобрать: браузер забрал указатель, палец ушёл за край, система
            // отменила касание. Без уборки призрак остаётся висеть поверх всего навсегда —
            // застрявшая метка читается как поломка интерфейса.
            button.RegisterCallback<PointerCancelEvent>(e => EndDrag(button, e.pointerId));
            button.RegisterCallback<PointerCaptureOutEvent>(e => EndDrag(button, e.pointerId));
        }

        private Card Find(string key) => new Card
        {
            Button = _root.Q<Button>("tool-" + key),
            Count = _root.Q<Label>("tool-" + key + "-count"),
        };

        private void OnToolChanged(FarmToolKind tool) => Refresh();

        private void Refresh()
        {
            if (_bar == null) return;

            // В гостях панель прячется целиком: у гостя своя грамматика жестов со своими
            // лимитами (PlotDragger.GuestPress), и инструменты хозяина ему не принадлежат.
            bool guest = GuestMode.IsGuest;
            _bar.style.display = guest ? DisplayStyle.None : DisplayStyle.Flex;
            if (guest) return;

            var current = FarmTool.Current;

            // У руки и переноса запаса нет — счётчик у них пуст и места не занимает.
            Mark(_hand, current == FarmToolKind.Hand, true);
            Mark(_move, current == FarmToolKind.Move, true);

            // Счёт зарядов — на самой карточке: инструмент и его запас один предмет.
            // Отдельная строка в топбаре, как было у воды, заставляла игрока держать
            // эту связь в голове.
            Count(_water, FarmWater.Charges, FarmWater.Capacity > 0, current == FarmToolKind.Water);
            Count(_feed, FarmFeed.Charges, FarmFeed.Capacity > 0, current == FarmToolKind.Feed);

            // Удобрение без числа, пока его ни разу не давали: постоянный «0» на карточке
            // читается как недоделка, а не как пустой запас.
            int fertilizer = FarmFertilizer.Charges;
            Count(_fert, fertilizer, fertilizer > 0, current == FarmToolKind.Fertilizer);
        }

        private static void Mark(Card card, bool on, bool usable)
        {
            if (card.Button == null) return;
            card.Button.EnableInClassList("tool--on", on);
            card.Button.EnableInClassList("tool--empty", !usable);
        }

        private static void Count(Card card, int charges, bool hasSource, bool on)
        {
            if (card.Button == null) return;

            // Карточка остаётся нажимаемой и пустой: нажатый пустой инструмент скажет вслух,
            // чего не хватает («корма нет — мешок через 2 ч»), а спрятанный промолчит.
            if (card.Count != null)
            {
                card.Count.style.display = hasSource ? DisplayStyle.Flex : DisplayStyle.None;
                if (hasSource) card.Count.text = charges.ToString();
            }

            Mark(card, on, hasSource && charges > 0);
        }

        // ---- призрак ----

        private void ShowGhost(string caption, Vector2 panelPoint)
        {
            if (_ghost == null)
            {
                _ghost = new Label();
                _ghost.AddToClassList("tool-ghost");
                _ghost.pickingMode = PickingMode.Ignore;   // призрак не должен ловить свой же сброс
                _root.Add(_ghost);
            }

            ((Label)_ghost).text = caption;
            _ghost.style.display = DisplayStyle.Flex;
            MoveGhost(panelPoint);
        }

        private void MoveGhost(Vector2 panelPoint)
        {
            if (_ghost == null) return;
            _ghost.style.left = panelPoint.x;
            _ghost.style.top = panelPoint.y;
        }

        private void HideGhost()
        {
            if (_ghost != null) _ghost.style.display = DisplayStyle.None;
        }
    }
}

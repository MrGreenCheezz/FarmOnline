using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Farm.Farming;
using Farm.Juice;

namespace Farm.Interaction
{
    /// <summary>
    /// Перетаскивание по ферме мышью. Растимое — и посевы, и животные — сливается при сбросе
    /// на такое же той же породы и уровня; постройки с меткой <see cref="Movable"/> можно
    /// переставлять, но они не сливаются никогда.
    /// <para>
    /// Захват меряется <i>на экране</i>, а не по земле. С наклонной камерой клик по верхушке
    /// высокого растения проецируется на землю на метр с лишним позади его основания — высокие
    /// культуры было фактически не схватить, пока низкие работали. К тому же экранное
    /// расстояние — это просто то, чем игрок целится.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Plot Dragger")]
    public sealed class PlotDragger : MonoBehaviour
    {
        [SerializeField] private Camera _camera;

        [Tooltip("UIDocument, поверх которого нельзя тащить. Пусто — найдётся первый в сцене.")]
        [SerializeField] private UIDocument _ui;

        [Header("Захват")]
        [Tooltip("Радиус захвата в пикселях при высоте окна 1080; масштабируется под другие разрешения.")]
        [SerializeField, Min(4f)] private float _pickRadiusPixels = 80f;

        [Tooltip("Радиус, в котором объект под курсором считается целью слияния, в тех же пикселях.")]
        [SerializeField, Min(4f)] private float _dropRadiusPixels = 90f;

        [Tooltip("Радиус захвата собираемого руками. Меньше обычного: узлы мелкие и стоят кучно, " +
                 "и щедрый радиус означал бы, что клик уходит не в тот.")]
        [SerializeField, Min(4f)] private float _gatherRadiusPixels = 55f;

        [Tooltip("Высота «центра» грядки при наведении. Растение рисуется вверх от основания.")]
        [SerializeField, Min(0f)] private float _plotAimHeight = 0.45f;

        [Tooltip("Насколько курсор может уехать, чтобы это всё ещё считалось кликом, а не перетаскиванием.\n" +
                 "В тех же пикселях при высоте окна 1080.")]
        [SerializeField, Min(1f)] private float _tapSlopPixels = 12f;

        [Header("Вид")]
        [SerializeField, Min(0f)] private float _liftHeight = 0.5f;

        [Tooltip("Во сколько раз увеличивать цель, на которую можно слить.")]
        [SerializeField, Min(1f)] private float _targetHighlightScale = 1.2f;

        private Transform _dragged;
        private Growable _draggedGrowable;
        private Growable _highlighted;
        private Gatherable _gathering;
        private Vector3 _highlightScale;
        private float _groundY;
        private Vector2 _pressScreen;

        /// <summary>Спелая грядка под пальцем гостя — кандидат на «помочь» при отпускании.</summary>
        private Growable _guestPlot;

        /// <summary>Нажатие гостя было принято миром (не съедено UI) — отпускание имеет право действовать.</summary>
        private bool _guestPressValid;

        /// <summary>Узел, который игрок сейчас собирает руками, или null.</summary>
        public Gatherable Gathering => _gathering;

        /// <summary>Грядка в руке, если тащат именно грядку.</summary>
        public Growable Dragged => _draggedGrowable;

        /// <summary>Что угодно в руке — грядка, животное или постройка.</summary>
        public Transform DraggedObject => _dragged;

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
            if (_ui == null) _ui = FindFirstObjectByType<UIDocument>();
        }

        private void OnEnable() => FarmTool.ApplyRequested += OnToolDropped;

        /// <summary>
        /// Иконку инструмента бросили на ферму (панель в Farm.UI позвала через FarmTool).
        /// Точку берём у мыши здесь: панель про Input System не знает и знать не должна.
        /// </summary>
        private void OnToolDropped()
        {
            // Pointer, а не Mouse: на телефоне мыши нет вовсе, и сброс иконки молча
            // не делал бы ничего — тихая точка отказа там, где жест только что был.
            var pointer = Pointer.current;
            if (pointer == null)
            {
                FarmingEvents.RaiseNotice("не понял, куда", transform.position);
                return;
            }

            Vector2 screen = pointer.position.ReadValue();

            // Сброс над интерфейсом — это отмена, а не применение: иначе иконка, отпущенная
            // над панелью или топбаром, тратила бы заряд на грядку, спрятанную ЗА ними
            // (блокер судей). Путь нажатия ту же проверку делает в Update.
            if (IsPointerOverUI(screen))
            {
                Sfx.Play(b => b.UiClose);
                return;
            }

            ApplyToolAt(screen);
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _camera == null) return;

            Vector2 screen = mouse.position.ReadValue();

            // В гостях руки связаны нарочно: таскать, сливать и собирать ночное может
            // только хозяин (правило 1). Гостю остаётся ровно один жест — тап-помощь
            // по спелой грядке, и он идёт отдельной веткой, не касаясь захвата.
            if (GuestMode.IsGuest)
            {
                if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI(screen)) GuestPress(screen);
                if (mouse.leftButton.wasReleasedThisFrame) GuestRelease(screen);
                return;
            }

            // Сбор проверяем раньше перетаскивания: узлы мельче грядок, и если сначала
            // хватать грядку, до светлячка над ней клик не дойдёт никогда.
            if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI(screen))
            {
                // Ночные узлы берут ТОЛЬКО рукой: с ведром в руке клик по светлячку над
                // грядкой уходил бы в сбор, и названный инструмент молча подменялся чужим
                // действием. Правило 2 цело — ночь по-прежнему собирает игрок и только он.
                bool gathered = FarmTool.Current == FarmToolKind.Hand && TryStartGather(screen);
                if (!gathered)
                {
                    // Инструмент решает всё: поднимает грядку в руку ТОЛЬКО перенос.
                    // Раньше объект поднимался на любом нажатии, а смысл жеста выяснялся
                    // при отпускании — оттуда и брались случайные поливы соседней грядки.
                    if (FarmTool.Current == FarmToolKind.Move) TryPick(screen);
                    else ApplyToolAt(screen);
                }
            }
            else if (_gathering != null && mouse.leftButton.isPressed) ContinueGather();
            else if (_dragged != null && mouse.leftButton.isPressed) DragTo(screen);

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                SetGathering(null);
                if (_dragged != null) Drop(screen);
            }

            if (_gathering != null && _gathering.IsEmpty) SetGathering(null);

            // Объект могли уничтожить, пока он в руке (слияние с другой стороны и т.п.)
            if (_dragged == null)
            {
                _draggedGrowable = null;
                ClearHighlight();
            }
        }

        // ---- гостевая помощь ----

        private void GuestPress(Vector2 screen)
        {
            _pressScreen = screen;
            _guestPressValid = true;
            _guestPlot = FindGuestPlot(screen);
        }

        private void GuestRelease(Vector2 screen)
        {
            var plot = _guestPlot;
            _guestPlot = null;

            // Отпускание без принятого нажатия (нажали над UI — GuestPress не звался):
            // сверка со старым _pressScreen дала бы ложный ночной отказ по клику в панель.
            if (!_guestPressValid) return;
            _guestPressValid = false;

            if ((screen - _pressScreen).sqrMagnitude > TapSlopSqr()) return;

            // Узел — раньше грядки, как и дома («узлы мельче грядок»): грамматика жеста
            // не должна меняться в гостях. Собрать нельзя (ночь хозяйская, правило 2),
            // но молчать по видимому светящемуся объекту — читаться поломкой.
            if (FindNearestGatherable(screen) != null)
            {
                GuestMode.RefuseNight();
                Sfx.Play(b => b.UiClose);
                return;
            }

            if (plot == null) return;

            // Тот же язык жестов, что и дома: тап по спелой — собрать, по растущей — полить.
            // Гостю не приходится учить вторую грамматику ради визита.
            if (plot.IsReady) GuestHelp(plot);
            else if (plot.CanWater) GuestCare(plot);
        }

        private void GuestHelp(Growable plot)
        {
            if (!GuestMode.CanHelp)
            {
                // Лимит кончился, а игрок кликает — молчать нельзя, UI слушает это событие.
                GuestMode.RefuseHelp();
                Sfx.Play(b => b.UiClose);
                return;
            }

            // Всё нужное — до сбора: однолетку TryHarvest уничтожает вместе с определением.
            var definition = plot.Definition;
            string uid = plot.Uid;
            Vector3 at = plot.transform.position;
            int level = plot.Level;

            // Урожай — в пустышку: он хозяйский и материализуется у хозяина при
            // проигрывании события, а не в чужом складе гостя.
            if (!plot.TryHarvest(out HarvestResult result, DiscardSink.Instance)) return;

            Effects.Play(l => l.Harvest, at + Vector3.up * _plotAimHeight);
            Sfx.Play(b => b.Harvest);

            GuestMode.ReportHelp(new HelpReport(
                uid,
                at,
                definition != null ? definition.Id : "",
                level,
                result.Resource != null ? result.Resource.Id : "",
                result.Amount));
        }

        private void GuestCare(Growable plot)
        {
            if (!GuestMode.CanCare)
            {
                // Свой отказ, не RefuseHelp: лимиты сбора и полива раздельные, и текст
                // «помощь исчерпана» при счётчике «помощь 0/5» на экране был бы враньём.
                GuestMode.RefuseCare();
                Sfx.Play(b => b.UiClose);
                return;
            }

            // Поливаем локальную копию, минуя колодец: вода хозяина не тратится — гостевая
            // сцена вообще черновик, настоящий эффект случится у хозяина при проигрывании
            // события. Локальный полив нужен, чтобы гость видел сделанное и не поливал дважды.
            if (!plot.TryWater(FarmWater.CycleFraction)) return;

            Effects.Play(l => l.Plant, plot.transform.position + Vector3.up * _plotAimHeight);
            Sfx.Play(b => b.Plant);

            GuestMode.ReportCare(new CareReport(plot.Uid, plot.CycleId));
        }

        /// <summary>Ближайшая грядка, с которой гостю есть что делать: спелая или растущая неполитая.</summary>
        private Growable FindGuestPlot(Vector2 screen)
        {
            float scale = Screen.height > 0 ? Screen.height / 1080f : 1f;
            float radius = _pickRadiusPixels * Mathf.Max(0.5f, scale);
            float bestSqr = radius * radius;

            Growable best = null;
            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
            {
                var g = plots[i];
                if (g == null || (!g.IsReady && !g.CanWater)) continue;
                if (!TryScreenDistance(g.transform.position, _plotAimHeight, screen, out float sqr)) continue;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = g;
            }

            return best;
        }

        // ---- сбор руками ----

        /// <summary>Схватить ближайший собираемый узел под курсором. True, если клик ушёл ему.</summary>
        private bool TryStartGather(Vector2 screen)
        {
            var node = FindNearestGatherable(screen);
            if (node == null) return false;

            if (node.Mode == GatherMode.Click)
            {
                // Клик отрабатывает сразу и не удерживает узел: смысл режима в том, чтобы
                // щёлкать по разным, а не зажимать один.
                if (node.Collect()) Feedback(node);
                return true;
            }

            SetGathering(node);
            return true;
        }

        private void ContinueGather()
        {
            if (_gathering == null) return;

            if (_gathering.Hold(Time.deltaTime)) Feedback(_gathering);
        }

        /// <summary>Держим удерживаемый узел и общий фокус в согласии — по нему рисуется индикатор.</summary>
        private void SetGathering(Gatherable node)
        {
            _gathering = node;
            GatherFocus.Set(node);
        }

        private void Feedback(Gatherable node)
        {
            if (node == null) return;

            Tween.Punch(node.transform, 0.22f, 0.18f);
            Effects.Play(l => l.Harvest, node.transform.position + Vector3.up * node.AimHeight);
            Sfx.Play(b => b.Harvest);
        }

        private Gatherable FindNearestGatherable(Vector2 screen)
        {
            float scale = Screen.height > 0 ? Screen.height / 1080f : 1f;
            float radius = _gatherRadiusPixels * Mathf.Max(0.5f, scale);
            float bestSqr = radius * radius;

            Gatherable best = null;
            var all = GatherableRegistry.All;

            for (int i = 0; i < all.Count; i++)
            {
                var node = all[i];
                if (node == null || node.IsEmpty) continue;
                if (!TryScreenDistance(node.transform.position, node.AimHeight, screen, out float sqr)) continue;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = node;
            }

            return best;
        }

        // ---- инструменты ----

        /// <summary>
        /// Применить инструмент, который сейчас в руке, к тому, что под точкой экрана.
        /// <para>
        /// Единственная точка применения — сюда приходит и обычное нажатие мышью, и сброс
        /// перетащенной с панели иконки. Два пути ввода, ведущие в разный код, разошлись бы
        /// на первой же правке; здесь они сходятся до того, как что-то произойдёт.
        /// </para>
        /// <para>
        /// Правило 1 не страдает: сбор по-прежнему делает игрок и только игрок — инструмент
        /// лишь называет, какой из его собственных жестов сейчас в руке.
        /// </para>
        /// </summary>
        public void ApplyToolAt(Vector2 screen)
        {
            if (GuestMode.IsGuest) return;   // в гостях своя грамматика жестов, см. GuestPress

            if (!TryFindNearest(screen, _pickRadiusPixels, null, out Transform found, out Growable growable))
            {
                // Мимо всего — снять выделение постройки, как в любом редакторе.
                BuildingSelection.Clear();

                // С инструментом в руке промах обязан звучать: игрок держит ведро и должен
                // понять, что оно НЕ вылилось. Пустая рука по пустой земле молчит, как и раньше.
                if (FarmTool.Current != FarmToolKind.Hand) Sfx.Play(b => b.UiClose);
                return;
            }

            // Уход требует цели, у которой есть что растить: по постройке ведром — не отказ
            // системы, а промах игрока, и назвать его должен тот, кто промахнулся.
            if (growable == null && FarmTool.Current != FarmToolKind.Hand)
            {
                FarmingEvents.RaiseNotice("сюда не льют", found.position + Vector3.up * _plotAimHeight);
                Sfx.Play(b => b.UiClose);
                return;
            }

            // Клеймо игрока раньше ставилось побочно, внутри DragFocus.Set при захвате в руку.
            // Рука больше ничего не поднимает — значит метку «этого я только что трогал»
            // ставим сами, иначе жители начнут переставлять грядки под курсором игрока.
            DragFocus.Claim(found);

            Vector3 at = found.position;
            switch (FarmTool.Current)
            {
                case FarmToolKind.Water:
                    Care(FarmWater.Pour(growable), at);
                    return;

                case FarmToolKind.Feed:
                    Care(FarmFeed.Feed(growable), at);
                    return;

                case FarmToolKind.Fertilizer:
                    Care(FarmFertilizer.Apply(growable), at);
                    return;

                default:
                    HandAt(found, growable, at);
                    return;
            }
        }

        /// <summary>Пустая рука: собрать спелое, открыть постройку, а по растущему — сказать срок.</summary>
        private void HandAt(Transform found, Growable growable, Vector3 at)
        {
            if (growable == null)
            {
                BuildingSelection.Select(found.GetComponent<Building>());
                return;
            }

            if (growable.IsReady)
            {
                if (growable.TryHarvest())
                {
                    Effects.Play(l => l.Harvest, at + Vector3.up * _plotAimHeight);
                    Sfx.Play(b => b.Harvest);
                }
                return;
            }

            // Ткнули в растущее. Это не отказ, а вопрос «когда?» — и молчать на него нельзя
            // (правило заметности), поэтому грядка отвечает сроком, а не отговоркой.
            FarmingEvents.RaiseNotice(RipeNote(growable), at + Vector3.up * _plotAimHeight);
        }

        /// <summary>«поспеет через 2 ч» — человеческим языком, без секунд и процентов.</summary>
        private static string RipeNote(Growable plot)
        {
            double left = plot.TimeUntilReady;
            if (left < 0.0) return "тут пусто";
            if (left <= 0.0) return "вот-вот поспеет";

            if (left >= 3600.0) return "поспеет через " + Mathf.RoundToInt((float)(left / 3600.0)) + " ч";
            if (left >= 60.0) return "поспеет через " + Mathf.RoundToInt((float)(left / 60.0)) + " мин";
            return "поспеет вот-вот";
        }

        // ---- этапы ----

        private void TryPick(Vector2 screen)
        {
            if (!TryFindNearest(screen, _pickRadiusPixels, null, out Transform found, out Growable growable))
            {
                // Клик по пустой земле — снять выделение постройки, как в любом редакторе.
                BuildingSelection.Clear();
                return;
            }

            _dragged = found;
            _draggedGrowable = growable;
            _groundY = found.position.y;
            _pressScreen = screen;

            // Животное, пока его несут, не должно брести по своим делам из-под курсора.
            DragFocus.Set(found);

            Tween.Punch(found, 0.12f, 0.18f);
            Sfx.Play(b => b.PickUp);

            DragTo(screen);
        }

        private void DragTo(Vector2 screen)
        {
            if (_dragged == null) return;
            if (!TryGroundPoint(screen, _groundY, out Vector3 point)) return;

            // Над рельефом, а не над своей исходной высотой: иначе на пологом склоне предмет
            // в руке то ныряет в землю, то взлетает.
            float ground = FarmingRuntime.Ground.SampleHeight(point);
            _dragged.position = new Vector3(point.x, ground + _liftHeight, point.z);

            Highlight(FindMergeTarget(screen, _draggedGrowable));
        }

        private void Drop(Vector2 screen)
        {
            var dragged = _dragged;
            var growable = _draggedGrowable;
            if (dragged == null)
            {
                _dragged = null;
                _draggedGrowable = null;
                DragFocus.Clear();
                ClearHighlight();
                return;
            }

            // Цель ищем до того, как отпустить ссылки: она нужна и для проверки совместимости.
            var target = FindMergeTarget(screen, growable);

            // Курсор почти не сдвинулся — это был клик, а не перенос. Клик по спелой грядке —
            // сбор: главный жест онлайн-фермы, посадил-подождал-собрал. По растущей — полив:
            // фазы Ready и Growing взаимоисключающие, поэтому один жест никогда не значит
            // двух вещей сразу, и решать, что имел в виду игрок, не приходится. Клик по
            // постройке открывает её панель; по остальному — просто снимает выделение.
            // Отдельная кнопка «осмотреть» не нужна, а правая кнопка мыши не переживёт
            // переезда на тач.
            // Тап с инструментом переноса — это «поднял и положил обратно»: он ничего не
            // делает с грядкой, потому что сбор и уход живут на своих инструментах
            // (ApplyToolAt, вызывается прямо на нажатии). Раньше здесь стояла лестница
            // «сбор → полив → подкормка → панель», и она же была источником путаницы.
            bool wasTap = (screen - _pressScreen).sqrMagnitude <= TapSlopSqr();

            _dragged = null;
            _draggedGrowable = null;
            DragFocus.Clear();
            ClearHighlight();

            // Слияние принадлежит жесту ПЕРЕНОСА (правило 1: «сливает — игрок сбросом мыши»).
            // Тап исчерпывается сбором/поливом/подкормкой/выбором выше и до слияния не доходит:
            // с правилом «суммой» целью стал бы почти любой сосед того же вида, и клик-сбор
            // молча сливал бы грядки — а тап по 20-ке запускал бы необратимый переход ступени.
            if (!wasTap)
            {
                if (target != null && growable != null && target.TryMergeWith(growable)) return;   // growable уничтожен внутри

                // Рядом лежит пара того же вида, но слиться нельзя (потолок 20, вершина линии) —
                // причину говорим вслух: молчание при дропе на «почти пару» читается как поломка.
                if (target == null && growable != null &&
                    TryFindNearest(screen, _dropRadiusPixels, growable.transform, out _, out Growable refusedBy) &&
                    refusedBy != null)
                    refusedBy.RefuseMergeAloud(growable);
            }

            Vector3 point = dragged.position;
            if (TryGroundPoint(screen, _groundY, out Vector3 hit)) point = hit;

            // Дуга вниз и приземление со сплющиванием — предмет должен ощущаться тяжёлым,
            // а не телепортироваться на землю. И только внутрь фермы: земля бесконечна,
            // а забор — нет, иначе грядку можно закинуть за горизонт и не достать.
            var landing = FarmBounds.ClampToFarm(new Vector3(point.x, 0f, point.z));
            landing.y = FarmingRuntime.Ground.SampleHeight(landing);
            Tween.HopTo(dragged, landing, 0.22f, 0.18f, () =>
            {
                if (dragged == null) return;
                Tween.Squash(dragged, 0.22f, 0.22f);
                Effects.Play(l => l.Place, landing);
            });

            Sfx.Play(b => b.Drop);
        }

        // ---- поиск ----

        /// <summary>
        /// Грядка, в которую <paramref name="dragged"/> мог бы слиться, или null.
        /// Перетаскиваемое передаётся параметром, а не читается из поля, чтобы вызывающий не мог
        /// перепутать порядок — обнуление поля до поиска однажды молча убило все слияния.
        /// </summary>
        private Growable FindMergeTarget(Vector2 screen, Growable dragged)
        {
            if (dragged == null) return null;

            if (!TryFindNearest(screen, _dropRadiusPixels, dragged.transform, out _, out Growable candidate))
                return null;

            return candidate != null && candidate.CanMergeWith(dragged) ? candidate : null;
        }

        /// <summary>
        /// Ближайшее к курсору перетаскиваемое в экранных координатах, по обоим реестрам.
        /// <paramref name="growable"/> возвращается null, когда победил обычный movable.
        /// </summary>
        private bool TryFindNearest(Vector2 screen, float radiusPixels, Transform skip,
                                    out Transform found, out Growable growable)
        {
            found = null;
            growable = null;

            // Радиус задан для высоты окна 1080; на других разрешениях сохраняем ту же точность наведения.
            float scale = Screen.height > 0 ? Screen.height / 1080f : 1f;
            float radius = radiusPixels * Mathf.Max(0.5f, scale);
            float bestSqr = radius * radius;

            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
            {
                var g = plots[i];
                if (g == null || g.transform == skip) continue;
                if (g.Phase == GrowthPhase.Empty) continue;   // пустую грядку хватать не за что
                if (!TryScreenDistance(g.transform.position, _plotAimHeight, screen, out float sqr)) continue;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                found = g.transform;
                growable = g;
            }

            var movables = MovableRegistry.All;
            for (int i = 0; i < movables.Count; i++)
            {
                var m = movables[i];
                if (m == null || !m.CanMove || m.transform == skip) continue;
                if (!TryScreenDistance(m.transform.position, m.AimHeight, screen, out float sqr)) continue;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                found = m.transform;
                growable = null;   // постройки не сливаются
            }

            return found != null;
        }

        /// <summary>Квадрат порога «тапа», масштабированный под окно так же, как радиусы захвата.</summary>
        private float TapSlopSqr()
        {
            float scale = Screen.height > 0 ? Screen.height / 1080f : 1f;
            float slop = _tapSlopPixels * Mathf.Max(0.5f, scale);
            return slop * slop;
        }

        private bool TryScreenDistance(Vector3 worldPosition, float aimHeight, Vector2 screen, out float sqrDistance)
        {
            Vector3 sp = _camera.WorldToScreenPoint(worldPosition + Vector3.up * aimHeight);
            if (sp.z <= 0f) { sqrDistance = float.MaxValue; return false; }   // позади камеры

            sqrDistance = ((Vector2)sp - screen).sqrMagnitude;
            return true;
        }

        private bool TryGroundPoint(Vector2 screen, float height, out Vector3 point)
        {
            var ray = _camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));

            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = default;
            return false;
        }

        /// <summary>
        /// Истина, когда курсор над элементом UI Toolkit. Без этого первый клик по кнопке
        /// магазина заодно хватал бы то, что оказалось позади окна.
        /// </summary>
        private bool IsPointerOverUI(Vector2 screen)
        {
            var panel = _ui != null && _ui.rootVisualElement != null ? _ui.rootVisualElement.panel : null;
            if (panel == null) return false;

            // Панель считает Y сверху вниз, экран — снизу вверх.
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(panelPoint) != null;
        }

        // ---- уход ----

        /// <summary>
        /// Отклик на уход за грядкой. Он одинаков и для полива, и для подкормки: что именно
        /// случилось, скажет надпись над грядкой, а руке важно лишь, приняли жест или нет.
        /// </summary>
        private void Care(bool done, Vector3 at)
        {
            if (done)
            {
                Effects.Play(l => l.Plant, at + Vector3.up * _plotAimHeight);
                Sfx.Play(b => b.Plant);
            }
            else
            {
                Sfx.Play(b => b.UiClose);
            }
        }

        // ---- подсветка ----

        private void Highlight(Growable target)
        {
            if (_highlighted == target) return;

            ClearHighlight();
            if (target == null) return;

            _highlighted = target;
            _highlightScale = target.transform.localScale;

            // Плавно, а не рывком: подсветка должна читаться как «сюда можно», а не как глитч.
            Tween.ScaleTo(target.transform, _highlightScale * _targetHighlightScale, 0.12f);
            Sfx.Play(b => b.UiClick);
        }

        private void ClearHighlight()
        {
            if (_highlighted != null)
            {
                // Мгновенно и точно: следом может прилететь punch слияния, и он должен
                // взять за основу настоящий размер, а не промежуточный кадр анимации.
                Tween.Kill(_highlighted.transform);
                _highlighted.transform.localScale = _highlightScale;
            }
            _highlighted = null;
        }

        private void OnDisable()
        {
            FarmTool.ApplyRequested -= OnToolDropped;
            ClearHighlight();
            SetGathering(null);
            DragFocus.Clear();
            _dragged = null;
            _draggedGrowable = null;
            _guestPlot = null;
        }
    }
}

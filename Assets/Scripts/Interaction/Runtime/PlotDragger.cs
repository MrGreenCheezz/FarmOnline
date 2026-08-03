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

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _camera == null) return;

            Vector2 screen = mouse.position.ReadValue();

            // Сбор проверяем раньше перетаскивания: узлы мельче грядок, и если сначала
            // хватать грядку, до светлячка над ней клик не дойдёт никогда.
            if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI(screen))
            {
                if (!TryStartGather(screen)) TryPick(screen);
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

            _dragged.position = new Vector3(point.x, _groundY + _liftHeight, point.z);
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

            // Курсор почти не сдвинулся — это был клик, а не перенос. Клик по постройке открывает
            // её панель; по грядке — просто снимает выделение. Отдельная кнопка «осмотреть» не нужна,
            // а правая кнопка мыши не переживёт переезда на тач.
            if ((screen - _pressScreen).sqrMagnitude <= TapSlopSqr())
                BuildingSelection.Select(dragged.GetComponent<Building>());

            _dragged = null;
            _draggedGrowable = null;
            DragFocus.Clear();
            ClearHighlight();

            if (target != null && growable != null && target.TryMergeWith(growable)) return;   // growable уничтожен внутри

            Vector3 point = dragged.position;
            if (TryGroundPoint(screen, _groundY, out Vector3 hit)) point = hit;

            // Дуга вниз и приземление со сплющиванием — предмет должен ощущаться тяжёлым,
            // а не телепортироваться на землю. И только внутрь фермы: земля бесконечна,
            // а забор — нет, иначе грядку можно закинуть за горизонт и не достать.
            var landing = FarmBounds.ClampToFarm(new Vector3(point.x, _groundY, point.z));
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
            ClearHighlight();
            SetGathering(null);
            DragFocus.Clear();
            _dragged = null;
            _draggedGrowable = null;
        }
    }
}

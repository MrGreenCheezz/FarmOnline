using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Первый урок: показать пару, которую можно свести, и назвать ход.
    /// <para>
    /// Слияние — единственное решение, ради которого игру открывают, и единственное, о котором
    /// игра нигде не говорила ни слова. Урок живёт ровно до первого слияния: подсказка тому, кто
    /// уже умеет, — шум, а от шума перестают читать и нужные подсказки.
    /// </para>
    /// <para>
    /// В мире не трогает ничего: и метки, и строка — элементы UI. Подсветить грядку размером
    /// нельзя, размер уже занят подсветкой цели переноса (<c>PlotDragger.Highlight</c>): две
    /// анимации на одном трансформе разойдутся, и грядка останется раздутой.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Merge Hint")]
    public sealed class MergeHint : MonoBehaviour
    {
        // Строка объясняет ход целиком: что сливается, что получается и чем это делают.
        // Без последней части урок читается как описание правила, а не как приглашение.
        private const string Lesson = "две одинаковые грядки сводятся в одну, но старше — потяни одну на другую";
        private const string Learned = "вот так. чем выше уровень, тем щедрее грядка";

        [SerializeField] private Camera _camera;

        [Tooltip("На какой высоте над грядкой висит метка. Выше плашки уровня, чтобы не спорить с ней.")]
        [SerializeField] private float _worldHeight = 1.8f;

        [Tooltip("Сколько молчать в начале партии. Урок в первую же секунду читается как окно-затычка.")]
        [SerializeField, Min(0f)] private float _delay = 6f;

        [Tooltip("Как часто искать пару, секунд.")]
        [SerializeField, Min(0.1f)] private float _searchInterval = 0.4f;

        [Tooltip("Сколько держать похвалу после первого слияния.")]
        [SerializeField, Min(0.5f)] private float _praiseDuration = 4f;

        private VisualElement _layer;
        private VisualElement _markA;
        private VisualElement _markB;
        private Label _line;

        private Growable _a;
        private Growable _b;

        private float _sinceStart;
        private float _searchTimer;
        private float _praiseLeft;
        private bool _taught;
        private bool _done;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            _layer = root?.Q<VisualElement>("merge-hint-layer");
            _line = root?.Q<Label>("merge-hint");

            if (_camera == null) _camera = Camera.main;
            if (_layer == null || _line == null) { enabled = false; return; }

            _markA = CreateMark();
            _markB = CreateMark();
            Hide();

            FarmingEvents.Merged += OnMerged;
        }

        private void OnDisable()
        {
            FarmingEvents.Merged -= OnMerged;
            Hide();
        }

        private VisualElement CreateMark()
        {
            var mark = new VisualElement();
            mark.AddToClassList("merge-mark");
            mark.pickingMode = PickingMode.Ignore;
            mark.style.display = DisplayStyle.None;
            _layer.Add(mark);
            return mark;
        }

        // LateUpdate: грядку могли протащить в этом же кадре, метка не должна отставать.
        private void LateUpdate()
        {
            if (_praiseLeft > 0f)
            {
                _praiseLeft -= Time.unscaledDeltaTime;
                if (_praiseLeft <= 0f) { _done = true; Hide(); }
                return;
            }

            // Сверка состояния каждый кадр, а не проверка на включении: сохранение приезжает
            // позже HUD, и «этот игрок уже умеет» узнаётся только после загрузки.
            if (_done || FarmProgress.TotalMerges > 0) { Hide(); return; }

            _sinceStart += Time.unscaledDeltaTime;
            if (_sinceStart < _delay) return;

            _searchTimer -= Time.unscaledDeltaTime;
            if (_searchTimer <= 0f) { _searchTimer = _searchInterval; FindPair(); }

            if (_a == null || _b == null || !_a.CanMergeWith(_b)) { Hide(); return; }

            _taught = true;
            ShowLine(Lesson);
            PlaceMark(_markA, _a);
            PlaceMark(_markB, _b);
        }

        /// <summary>
        /// Ближайшая пара, а не первая попавшаяся: урок должен показывать ход, который рука
        /// повторит одним движением, а не переноску через всю ферму.
        /// </summary>
        private void FindPair()
        {
            _a = null;
            _b = null;

            var plots = GrowableRegistry.All;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < plots.Count; i++)
            {
                var first = plots[i];
                if (first == null || first.Phase == GrowthPhase.Empty) continue;

                for (int j = i + 1; j < plots.Count; j++)
                {
                    var second = plots[j];
                    if (second == null || !first.CanMergeWith(second)) continue;

                    float sqr = (first.transform.position - second.transform.position).sqrMagnitude;
                    if (sqr >= bestSqr) continue;

                    bestSqr = sqr;
                    _a = first;
                    _b = second;
                }
            }
        }

        private void PlaceMark(VisualElement mark, Growable plot)
        {
            var panel = _layer.panel;
            if (panel == null || _camera == null) { mark.style.display = DisplayStyle.None; return; }

            Vector3 world = plot.transform.position + Vector3.up * _worldHeight;

            // Позади камеры метка «отразилась» бы на другую сторону экрана.
            if (_camera.WorldToViewportPoint(world).z <= 0f) { mark.style.display = DisplayStyle.None; return; }

            Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, _camera);

            // Немасштабируемое время: пульс — свойство интерфейса, а не скорости игры.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.4f);

            mark.style.display = DisplayStyle.Flex;
            mark.style.left = point.x - mark.resolvedStyle.width * 0.5f;
            mark.style.top = point.y - mark.resolvedStyle.height * 0.5f;
            mark.style.opacity = 0.35f + 0.65f * pulse;
            mark.style.scale = new StyleScale(new Scale(Vector2.one * (1f + 0.12f * pulse)));
        }

        /// <summary>
        /// Хвалим только того, кого учили. Игрок, который слил сам, объяснений не просил, а
        /// вернувшемуся из сохранения «вот так» вместо игры — насмешка.
        /// </summary>
        private void OnMerged(Growable survivor, Growable absorbed)
        {
            if (_done || !_taught) { _done = true; Hide(); return; }

            _a = null;
            _b = null;
            HideMarks();

            ShowLine(Learned);
            _praiseLeft = _praiseDuration;
        }

        private void ShowLine(string text)
        {
            _line.text = text;
            _line.style.display = DisplayStyle.Flex;
        }

        private void Hide()
        {
            if (_line != null) _line.style.display = DisplayStyle.None;
            HideMarks();
        }

        private void HideMarks()
        {
            if (_markA != null) _markA.style.display = DisplayStyle.None;
            if (_markB != null) _markB.style.display = DisplayStyle.None;
        }
    }
}

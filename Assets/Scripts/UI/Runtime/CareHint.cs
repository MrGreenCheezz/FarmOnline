using UnityEngine;
using UnityEngine.UIElements;
using Farm.Farming;

namespace Farm.UI
{
    /// <summary>
    /// Урок ухода: строка внизу экрана, пока игрок не полил первую грядку, и вторая —
    /// пока не потратил первую подкормку. По образцу урока слияния (<see cref="MergeHint"/>):
    /// ждёт момента, когда действие возможно, называет его словами, хвалит за первый успех
    /// и больше не возвращается.
    /// <para>
    /// «Уже умеет» выводится из партии, а не хранится отдельно: полита или ухожена хоть одна
    /// грядка — про полив молчим; подкормлена — молчим совсем. Отдельный флажок в сейве
    /// разошёлся бы с правдой при первом же переносе партии.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/UI/Care Hint")]
    public sealed class CareHint : MonoBehaviour
    {
        private const string WaterLesson = "растущую грядку можно полить — тапни по ней, и она созреет раньше";
        private const string WaterLearned = "полито! вода набегает в колодец сама — ведро за четыре часа";
        private const string FeedLesson = "за заказ дали подкормку — тапни по политой грядке ещё раз, урожай удвоится";
        private const string FeedLearned = "подкормлено! урожай с этой грядки будет двойным";

        [Tooltip("Сколько молчать в начале партии. Урок в первую секунду читается как окно-затычка.")]
        [SerializeField, Min(0f)] private float _delay = 14f;

        [Tooltip("Сколько держать похвалу.")]
        [SerializeField, Min(0.5f)] private float _praiseDuration = 4f;

        private Label _line;
        private Label _mergeLine;

        private float _sinceStart;
        private float _praiseLeft;
        private bool _taughtWater;
        private bool _taughtFeed;
        private bool _doneWater;
        private bool _doneFeed;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            _line = root?.Q<Label>("care-hint");
            _mergeLine = root?.Q<Label>("merge-hint");

            if (_line == null) { enabled = false; return; }

            FarmWater.Poured += OnPoured;
            FarmFertilizer.Applied += OnFed;
            Hide();
        }

        private void OnDisable()
        {
            FarmWater.Poured -= OnPoured;
            FarmFertilizer.Applied -= OnFed;
            Hide();
        }

        private void LateUpdate()
        {
            if (_praiseLeft > 0f)
            {
                _praiseLeft -= Time.unscaledDeltaTime;
                if (_praiseLeft <= 0f) Hide();
                return;
            }

            // Гостю не учат ухаживать за чужим, а два урока разом — каша: пока строка
            // слияния на экране, эта молчит. Слияние — первый жест игры, уступаем ему.
            if (GuestMode.IsGuest || MergeLineVisible()) { Hide(); return; }

            _sinceStart += Time.unscaledDeltaTime;
            if (_sinceStart < _delay) { Hide(); return; }

            if (!_doneWater)
            {
                // Партия, где уже ухаживали, объяснений не просила.
                if (AnyCared()) { _doneWater = true; return; }

                if (FarmWater.Charges > 0 && AnyWaterable())
                {
                    _taughtWater = true;
                    Show(WaterLesson);
                    return;
                }

                Hide();
                return;
            }

            if (!_doneFeed)
            {
                if (FarmFertilizer.Charges > 0 && AnyFeedable())
                {
                    _taughtFeed = true;
                    Show(FeedLesson);
                    return;
                }

                Hide();
            }
        }

        private static bool AnyCared()
        {
            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                if (plot != null && (plot.Watered || plot.CareStreak > 0)) return true;
            }
            return false;
        }

        private static bool AnyWaterable()
        {
            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
                if (plots[i] != null && plots[i].CanWater) return true;
            return false;
        }

        private static bool AnyFeedable()
        {
            // Подкормку жест предлагает только по политой грядке (см. PlotDragger) — урок
            // обязан звать ровно к тому жесту, который сработает.
            var plots = GrowableRegistry.All;
            for (int i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                if (plot != null && plot.Watered && plot.CanFertilize) return true;
            }
            return false;
        }

        /// <summary>Хвалим только того, кого учили, — как и урок слияния.</summary>
        private void OnPoured(Growable plot)
        {
            bool praise = _taughtWater && !_doneWater;
            _doneWater = true;
            if (!praise) return;

            Show(WaterLearned);
            _praiseLeft = _praiseDuration;
        }

        private void OnFed(Growable plot)
        {
            bool praise = _taughtFeed && !_doneFeed;
            _doneFeed = true;
            if (!praise) { Hide(); return; }

            Show(FeedLearned);
            _praiseLeft = _praiseDuration;
        }

        private bool MergeLineVisible() =>
            _mergeLine != null && _mergeLine.resolvedStyle.display != DisplayStyle.None;

        private void Show(string text)
        {
            _line.text = text;
            _line.style.display = DisplayStyle.Flex;
        }

        private void Hide()
        {
            if (_line != null) _line.style.display = DisplayStyle.None;
        }
    }
}

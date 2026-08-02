using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Juice
{
    /// <summary>
    /// Горстка твинов, которые нужны этой игре, и ничего сверх.
    /// <para>
    /// В Unity твинов нет, а тащить целую библиотеку ради четырёх кривых — крупная зависимость
    /// под мелкую нужду. Всё на корутинах и с ключом-transform: старт твина отменяет прежний твин
    /// объекта, поэтому панч слияния и снятие подсветки никогда не дерутся за один масштаб.
    /// </para>
    /// <para>
    /// Отмена — то место, где наивные твинеры ошибаются. Каждый публичный вызов здесь идёт в одном
    /// порядке: остановить старый твин <i>и вернуть то, что тот обещал вернуть</i>, и лишь потом
    /// прочитать текущий масштаб как новую базу. Сделай наоборот — и объекты «плывут»: схвати и
    /// брось грядку достаточно быстро, и она навсегда останется в 1.08×.
    /// </para>
    /// </summary>
    public static class Tween
    {
        private static readonly Dictionary<Transform, Coroutine> _running = new Dictionary<Transform, Coroutine>();

        /// <summary>Масштаб, который возвращающийся твин должен объекту, если его оборвут.</summary>
        private static readonly Dictionary<Transform, Vector3> _restoreScale = new Dictionary<Transform, Vector3>();

        // ---- кривые ----

        /// <summary>Перелетает и оседает. Причина, по которой «поп» читается как поп, а не как рост.</summary>
        public static float OutBack(float t, float overshoot = 1.7f)
        {
            t -= 1f;
            return t * t * ((overshoot + 1f) * t + overshoot) + 1f;
        }

        public static float OutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;

        // ---- жизненный цикл ----

        /// <summary>Остановить твин объекта и вернуть масштаб, к которому тот собирался прийти.</summary>
        public static void Kill(Transform target)
        {
            if (target == null) return;

            if (_running.TryGetValue(target, out var running) && running != null)
            {
                var runner = TweenRunner.Instance;
                if (runner != null) runner.StopCoroutine(running);

                if (_restoreScale.TryGetValue(target, out var scale)) target.localScale = scale;
            }

            _running.Remove(target);
            _restoreScale.Remove(target);
        }

        private static void Run(Transform target, IEnumerator routine, Vector3? restore, Action onComplete)
        {
            if (target == null || !Application.isPlaying) return;

            var runner = TweenRunner.Instance;
            if (runner == null) return;

            if (restore.HasValue) _restoreScale[target] = restore.Value;
            _running[target] = runner.StartCoroutine(Wrap(target, routine, onComplete));
        }

        /// <summary>
        /// Прогоняет твин и передаёт эстафету. Колбэк срабатывает только после снятия записи из
        /// реестра — иначе цепочка из второго твина убила бы корутину, которая его вызывает.
        /// </summary>
        private static IEnumerator Wrap(Transform target, IEnumerator routine, Action onComplete)
        {
            yield return routine;
            _running.Remove(target);
            _restoreScale.Remove(target);
            onComplete?.Invoke();
        }

        // ---- твины ----

        /// <summary>Раздуться выше базы и осесть обратно. Рабочая лошадка сигнала «это было важно».</summary>
        public static void Punch(Transform target, float strength = 0.35f, float duration = 0.32f)
        {
            if (target == null) return;

            Kill(target);
            Vector3 baseScale = target.localScale;
            Run(target, PunchRoutine(target, baseScale, strength, duration), baseScale, null);
        }

        private static IEnumerator PunchRoutine(Transform target, Vector3 baseScale, float strength, float duration)
        {
            float t = 0f;
            while (t < 1f && target != null)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, duration);
                // Полсинуса: быстро вырастает, мягко возвращается ровно к исходному размеру.
                float bump = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * strength;
                target.localScale = baseScale * (1f + bump);
                yield return null;
            }

            if (target != null) target.localScale = baseScale;
        }

        /// <summary>Сплющиться и спружинить обратно — удар, приземление, сбор.</summary>
        public static void Squash(Transform target, float strength = 0.3f, float duration = 0.28f)
        {
            if (target == null) return;

            Kill(target);
            Vector3 baseScale = target.localScale;
            Run(target, SquashRoutine(target, baseScale, strength, duration), baseScale, null);
        }

        private static IEnumerator SquashRoutine(Transform target, Vector3 baseScale, float strength, float duration)
        {
            float t = 0f;
            while (t < 1f && target != null)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, duration);
                float bump = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * strength;

                // Объём сохраняем: сплющили по высоте — раздули по ширине.
                target.localScale = new Vector3(
                    baseScale.x * (1f + bump * 0.6f),
                    baseScale.y * (1f - bump),
                    baseScale.z * (1f + bump * 0.6f));
                yield return null;
            }

            if (target != null) target.localScale = baseScale;
        }

        /// <summary>Вырасти из ничего с перелётом. Для того, что появляется.</summary>
        public static void PopIn(Transform target, float duration = 0.4f, float delay = 0f)
        {
            if (target == null) return;

            Kill(target);
            Vector3 baseScale = target.localScale;
            Run(target, PopInRoutine(target, baseScale, duration, delay), baseScale, null);
        }

        private static IEnumerator PopInRoutine(Transform target, Vector3 baseScale, float duration, float delay)
        {
            if (target != null) target.localScale = Vector3.zero;
            if (delay > 0f) yield return new WaitForSeconds(delay);

            float t = 0f;
            while (t < 1f && target != null)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, duration);
                target.localScale = baseScale * OutBack(Mathf.Clamp01(t));
                yield return null;
            }

            if (target != null) target.localScale = baseScale;
        }

        /// <summary>
        /// Плавно дойти до масштаба и остаться — подсветка наведения. Намеренно не записывает
        /// восстановление: этот твин должен закончиться в другом месте, и отмена не должна его откатывать.
        /// </summary>
        public static void ScaleTo(Transform target, Vector3 to, float duration = 0.15f)
        {
            if (target == null) return;

            Kill(target);
            Run(target, ScaleToRoutine(target, target.localScale, to, duration), null, null);
        }

        private static IEnumerator ScaleToRoutine(Transform target, Vector3 from, Vector3 to, float duration)
        {
            float t = 0f;
            while (t < 1f && target != null)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, duration);
                target.localScale = Vector3.LerpUnclamped(from, to, OutCubic(Mathf.Clamp01(t)));
                yield return null;
            }

            if (target != null) target.localScale = to;
        }

        /// <summary>Дугой к точке — бросок предмета на землю.</summary>
        public static void HopTo(Transform target, Vector3 to, float height = 0.35f, float duration = 0.22f,
                                 Action onComplete = null)
        {
            if (target == null) return;

            Kill(target);
            Run(target, HopRoutine(target, target.position, to, height, duration), null, onComplete);
        }

        private static IEnumerator HopRoutine(Transform target, Vector3 from, Vector3 to, float height, float duration)
        {
            float t = 0f;
            while (t < 1f && target != null)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, duration);
                float k = Mathf.Clamp01(t);

                Vector3 p = Vector3.Lerp(from, to, OutQuad(k));
                p.y += Mathf.Sin(k * Mathf.PI) * height;
                target.position = p;
                yield return null;
            }

            if (target != null) target.position = to;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _running.Clear();
            _restoreScale.Clear();
        }
    }
}

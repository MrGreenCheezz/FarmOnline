using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Farm.Juice
{
    /// <summary>
    /// The handful of tweens this game needs, and nothing more.
    /// <para>
    /// Unity ships no tweening, and pulling in a whole library for four curves would be a large
    /// dependency for a small need. Everything is coroutine-based and keyed by transform: starting
    /// a tween cancels the object's previous one, so a merge punch and a highlight release can
    /// never fight over the same scale.
    /// </para>
    /// <para>
    /// Cancelling is where naive tweeners go wrong. Every public call here follows the same order —
    /// stop the old tween <i>and restore what it promised to return to</i>, only then read the
    /// current scale as the new baseline. Do it the other way round and objects drift: grab and drop
    /// a plot fast enough and it stays permanently at 1.08×.
    /// </para>
    /// </summary>
    public static class Tween
    {
        private static readonly Dictionary<Transform, Coroutine> _running = new Dictionary<Transform, Coroutine>();

        /// <summary>Scale that a returning tween owes the object if it is cut short.</summary>
        private static readonly Dictionary<Transform, Vector3> _restoreScale = new Dictionary<Transform, Vector3>();

        // ---- easing ----

        /// <summary>Overshoots then settles. The reason a pop reads as "pop" and not as "grow".</summary>
        public static float OutBack(float t, float overshoot = 1.7f)
        {
            t -= 1f;
            return t * t * ((overshoot + 1f) * t + overshoot) + 1f;
        }

        public static float OutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;

        // ---- жизненный цикл ----

        /// <summary>Stop the object's tween and put back the scale it was going to return to.</summary>
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
        /// Runs the tween, then hands over. The callback fires only after the registry entry is
        /// dropped — otherwise chaining a second tween would kill the coroutine calling it.
        /// </summary>
        private static IEnumerator Wrap(Transform target, IEnumerator routine, Action onComplete)
        {
            yield return routine;
            _running.Remove(target);
            _restoreScale.Remove(target);
            onComplete?.Invoke();
        }

        // ---- твины ----

        /// <summary>Scale up past the base and settle back. The workhorse "that mattered" cue.</summary>
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

        /// <summary>Squash down and spring back — impact, landing, harvesting.</summary>
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

        /// <summary>Grow from nothing with an overshoot. For things that appear.</summary>
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
        /// Smoothly reach a scale and stay there — hover highlight. Registers no restore value on
        /// purpose: this tween is meant to end somewhere else, so cancelling it must not undo it.
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

        /// <summary>Arc to a position — dropping something onto the ground.</summary>
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

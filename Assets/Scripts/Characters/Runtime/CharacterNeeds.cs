using System;
using UnityEngine;

namespace Farm.Characters
{
    /// <summary>
    /// Голод и жажда как две убывающие шкалы.
    /// <para>
    /// Хранятся как <i>сытость</i> и <i>вода</i> — насколько персонаж полон, а не насколько пуст, —
    /// чтобы «больше — лучше» работало для обеих и полоскам UI не нужна была инверсия.
    /// </para>
    /// <para>
    /// Нужды — рычаг, а не налог. Пустая шкала никогда не убивает и не останавливает фермера —
    /// только замедляет, и не ниже <see cref="_minProductivity"/>. Голодающий фермер, который
    /// вовсе не может работать, не может и дойти до еды, которая бы это исправила, — партия
    /// закончена, и сделать с этим нечего.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Character Needs")]
    public sealed class CharacterNeeds : MonoBehaviour
    {
        [Header("Голод")]
        [SerializeField, Min(1f)] private float _maxSatiety = 100f;
        [Tooltip("Сколько сытости теряется за секунду.")]
        [SerializeField, Min(0f)] private float _satietyDrainPerSecond = 1f;

        [Header("Жажда")]
        [SerializeField, Min(1f)] private float _maxHydration = 100f;
        [Tooltip("Сколько воды теряется за секунду. Обычно быстрее голода.")]
        [SerializeField, Min(0f)] private float _hydrationDrainPerSecond = 1.5f;

        [Header("Бодрость")]
        [SerializeField, Min(1f)] private float _maxEnergy = 100f;

        [Tooltip("Сколько бодрости уходит за секунду работы.")]
        [SerializeField, Min(0f)] private float _energyDrainWorking = 0.8f;

        [Tooltip("Сколько уходит за секунду безделья. Меньше, но не ноль — день утомляет сам по себе.")]
        [SerializeField, Min(0f)] private float _energyDrainResting = 0.2f;

        [Tooltip("Сколько возвращается за секунду сна.")]
        [SerializeField, Min(0.1f)] private float _energyRecoveryPerSecond = 14f;

        [Tooltip("Ниже этой доли фермер бросает работу и идёт спать.")]
        [SerializeField, Range(0f, 1f)] private float _tiredThreshold = 0.18f;

        [Tooltip("Во сколько раз медленнее тают голод и жажда во сне.\n" +
                 "Единица превращает каждое утро в кризис: за ночь при полной скорости уходит " +
                 "половина воды, а колодец столько за визит не возвращает.")]
        [SerializeField, Range(0f, 1f)] private float _sleepNeedsDrainScale = 0.25f;

        [Header("Пороги")]
        [Tooltip("Ниже этой доли считается, что персонаж голоден / хочет пить.")]
        [SerializeField, Range(0f, 1f)] private float _lowThreshold = 0.25f;

        [Tooltip("Выше этой доли штрафа нет вообще — чтобы игрок не бегал кормить каждую минуту.")]
        [SerializeField, Range(0f, 1f)] private float _comfortThreshold = 0.5f;

        [Tooltip("Насколько медленно работает совсем истощённый персонаж. Ноль ставить нельзя: " +
                 "он не сможет дойти до еды, и партия зайдёт в тупик.")]
        [SerializeField, Range(0.1f, 1f)] private float _minProductivity = 0.45f;

        private float _satiety;
        private float _hydration;
        private float _energy;
        private bool _wasHungry;
        private bool _wasThirsty;
        private bool _wasTired;

        /// <summary>
        /// Насколько тяжело персонаж работает прямо сейчас, 0..1. Агент выставляет каждый кадр.
        /// <para>
        /// Бодрость уходит от активности, а не по часам, в отличие от голода и жажды: фермер,
        /// простоявший всё утро, не должен нуждаться в дрёме. В покое она всё же тает — стоять
        /// столбом не способ пропустить ночь.
        /// </para>
        /// </summary>
        public float Exertion { get; set; }

        /// <summary>
        /// Спит ли персонаж — выставляет агент, тем же жестом, что <see cref="Exertion"/>.
        /// Во сне голод и жажда тают медленнее (<see cref="_sleepNeedsDrainScale"/>):
        /// тело в покое, и «проголодаться сильнее, чем колодец способен напоить» —
        /// ровно тот кризис, который ломал каждое утро.
        /// </summary>
        public bool IsSleeping { get; set; }

        /// <summary>Опустился ниже нижнего порога.</summary>
        public event Action<CharacterNeeds> BecameHungry;
        public event Action<CharacterNeeds> BecameThirsty;

        /// <summary>Поднялся обратно выше нижнего порога.</summary>
        public event Action<CharacterNeeds> Sated;
        public event Action<CharacterNeeds> Quenched;

        /// <summary>Кончилась бодрость, нужен сон.</summary>
        public event Action<CharacterNeeds> BecameTired;
        public event Action<CharacterNeeds> Rested;

        /// <summary>Дошло до нуля. Пока никто не реагирует — крючок для будущих последствий.</summary>
        public event Action<CharacterNeeds> Starving;
        public event Action<CharacterNeeds> Dehydrated;

        public float Satiety => _satiety;
        public float Hydration => _hydration;
        public float Energy => _energy;
        public float MaxSatiety => _maxSatiety;
        public float MaxHydration => _maxHydration;
        public float MaxEnergy => _maxEnergy;

        public float Satiety01 => _maxSatiety > 0f ? _satiety / _maxSatiety : 0f;
        public float Hydration01 => _maxHydration > 0f ? _hydration / _maxHydration : 0f;
        public float Energy01 => _maxEnergy > 0f ? _energy / _maxEnergy : 0f;

        public bool IsHungry => Satiety01 <= _lowThreshold;
        public bool IsThirsty => Hydration01 <= _lowThreshold;
        public bool IsTired => Energy01 <= _tiredThreshold;

        /// <summary>Выспался достаточно, чтобы встать. Нарочно выше порога усталости — чтобы не
        /// просыпался, делал два шага и снова падал.</summary>
        public bool IsRested => Energy01 >= 0.95f;

        /// <summary>Худшая из трёх шкал, 0..1. Удобно как одно число «насколько всё плохо».</summary>
        public float Wellbeing01 => Mathf.Min(Satiety01, Mathf.Min(Hydration01, Energy01));

        /// <summary>
        /// Насколько хорошо фермер сейчас работает, от <see cref="_minProductivity"/> до 1.
        /// <para>
        /// Ведётся по худшей из нужд — сытость до отвала не компенсирует пересохшее горло.
        /// Выше порога комфорта штрафа нет вовсе: игра, требующая внимания каждую минуту,
        /// перестаёт быть idle и становится повинностью.
        /// </para>
        /// </summary>
        public float Productivity01
        {
            get
            {
                float worst = Wellbeing01;
                if (worst >= _comfortThreshold) return 1f;

                float t = _comfortThreshold > 0f ? Mathf.Clamp01(worst / _comfortThreshold) : 1f;
                return Mathf.Lerp(_minProductivity, 1f, t);
            }
        }

        private void Awake()
        {
            _satiety = _maxSatiety;
            _hydration = _maxHydration;
            _energy = _maxEnergy;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float drain = Mathf.Lerp(_energyDrainResting, _energyDrainWorking, Mathf.Clamp01(Exertion));

            // Постройки-усилители замедляют все три шкалы разом: смысл такой постройки в том,
            // что фермер реже уходит с поля, а уходит он по худшей из нужд. По месту: уют
            // курятника действует рядом с курятником, и его ставят туда, где фермер живёт.
            dt *= Farm.Farming.FarmBuffs.NeedsDrainAt(transform.position);

            // Спящий тратит меньше — только на голод и жажду: бодростью и так управляет
            // сон через Sleep(), и трогать её здесь значило бы считать её дважды.
            float bodyDt = IsSleeping ? dt * _sleepNeedsDrainScale : dt;

            Set(_satiety - _satietyDrainPerSecond * bodyDt,
                _hydration - _hydrationDrainPerSecond * bodyDt,
                _energy - drain * dt);
        }

        public void Eat(float amount) => Set(_satiety + Mathf.Max(0f, amount), _hydration, _energy);
        public void Drink(float amount) => Set(_satiety, _hydration + Mathf.Max(0f, amount), _energy);

        /// <summary>Поспать: восстановление за <paramref name="seconds"/> секунд сна.</summary>
        public void Sleep(float seconds) =>
            Set(_satiety, _hydration, _energy + _energyRecoveryPerSecond * Mathf.Max(0f, seconds));

        /// <summary>Наполнить все шкалы доверху.</summary>
        public void Restore() => Set(_maxSatiety, _maxHydration, _maxEnergy);

        /// <summary>
        /// Состояние шкал долями, а не единицами: сохранение переживёт правку максимумов
        /// в инспекторе — «был сыт наполовину» осмысленно при любой шкале.
        /// </summary>
        public void CaptureState(out float satiety01, out float hydration01, out float energy01)
        {
            satiety01 = Satiety01;
            hydration01 = Hydration01;
            energy01 = Energy01;
        }

        public void RestoreState(float satiety01, float hydration01, float energy01) =>
            Set(Mathf.Clamp01(satiety01) * _maxSatiety,
                Mathf.Clamp01(hydration01) * _maxHydration,
                Mathf.Clamp01(energy01) * _maxEnergy);

        private void Set(float satiety, float hydration, float energy)
        {
            float prevSatiety = _satiety;
            float prevHydration = _hydration;

            _satiety = Mathf.Clamp(satiety, 0f, _maxSatiety);
            _hydration = Mathf.Clamp(hydration, 0f, _maxHydration);
            _energy = Mathf.Clamp(energy, 0f, _maxEnergy);

            bool tired = IsTired;
            if (tired != _wasTired)
            {
                _wasTired = tired;
                Raise(tired ? BecameTired : Rested);
            }

            bool hungry = IsHungry;
            if (hungry != _wasHungry)
            {
                _wasHungry = hungry;
                Raise(hungry ? BecameHungry : Sated);
            }

            bool thirsty = IsThirsty;
            if (thirsty != _wasThirsty)
            {
                _wasThirsty = thirsty;
                Raise(thirsty ? BecameThirsty : Quenched);
            }

            if (prevSatiety > 0f && _satiety <= 0f) Raise(Starving);
            if (prevHydration > 0f && _hydration <= 0f) Raise(Dehydrated);
        }

        private void Raise(Action<CharacterNeeds> handler)
        {
            if (handler == null) return;
            try { handler(this); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }
}

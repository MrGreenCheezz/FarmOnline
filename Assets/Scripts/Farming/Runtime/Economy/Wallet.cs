using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Золото игрока. Нарочно отделено от <see cref="IInventory"/>: у золота нет стека,
    /// иконки и лимита хранения, и затащить его в систему ресурсов значило бы плодить
    /// особые случаи везде, где инвентарь рисуется или ограничивается.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    [AddComponentMenu("Farm/Wallet")]
    public sealed class Wallet : MonoBehaviour
    {
        [SerializeField, Min(0)] private int _startingGold = 50;

        private int _gold;
        private bool _initialised;

        public static Wallet Instance { get; private set; }

        /// <summary>Поднимается при каждом изменении; int — дельта (отрицательная при трате).</summary>
        public event Action<Wallet, int> Changed;

        public int Gold
        {
            get
            {
                // Ленивая инициализация нужна тем, кто спрашивает золото раньше нашего Awake.
                if (!_initialised) { _gold = _startingGold; _initialised = true; }
                return _gold;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Economy] В сцене больше одного Wallet — лишний отключён", this);
                enabled = false;
                return;
            }

            Instance = this;
            if (!_initialised) { _gold = _startingGold; _initialised = true; }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Add(int amount)
        {
            if (amount <= 0) return;
            _gold = Gold + amount;
            Raise(amount);
        }

        public bool CanAfford(int amount) => Gold >= amount;

        /// <summary>Потратить, если хватает. Иначе вернёт false и ничего не изменит.</summary>
        public bool TrySpend(int amount)
        {
            if (amount <= 0) return true;
            if (Gold < amount) return false;

            _gold = Gold - amount;
            Raise(-amount);
            return true;
        }

        private void Raise(int delta)
        {
            var handler = Changed;
            if (handler == null) return;
            try { handler(this, delta); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}

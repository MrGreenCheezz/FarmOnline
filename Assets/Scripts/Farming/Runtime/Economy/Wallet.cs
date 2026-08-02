using System;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// The player's gold. Deliberately separate from <see cref="IInventory"/>: gold has no stack,
    /// no icon and no storage limit, and folding it into the resource system would mean special
    /// cases everywhere the inventory is displayed or capped.
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

        /// <summary>Fired on every change; the int is the delta (negative when spent).</summary>
        public event Action<Wallet, int> Changed;

        public int Gold
        {
            get
            {
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

        /// <summary>Spend if there is enough. Returns false and changes nothing otherwise.</summary>
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

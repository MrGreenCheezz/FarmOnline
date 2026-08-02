using UnityEngine;

namespace Farm.Characters
{
    /// <summary>
    /// How a character gets from A to B. <see cref="FarmerAgent"/> only ever talks to this base
    /// class, so switching to NavMesh pathfinding later means adding one subclass and swapping the
    /// component — the behaviour code does not change.
    /// </summary>
    public abstract class AgentMover : MonoBehaviour
    {
        public abstract float Speed { get; set; }

        /// <summary>Units per second actually being covered right now. 0 when standing still.</summary>
        public abstract float CurrentSpeed { get; }

        /// <summary>True when there is no destination, or the destination has been reached.</summary>
        public abstract bool HasArrived { get; }

        public abstract void SetDestination(Vector3 worldPosition);
        public abstract void Stop();
    }
}

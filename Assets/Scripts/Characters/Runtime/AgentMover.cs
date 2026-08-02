using UnityEngine;

namespace Farm.Characters
{
    /// <summary>
    /// Как персонаж добирается из точки А в точку Б. <see cref="FarmerAgent"/> общается только
    /// с этим базовым классом, поэтому переход на NavMesh позже — это один новый подкласс
    /// и замена компонента; код поведения не меняется.
    /// </summary>
    public abstract class AgentMover : MonoBehaviour
    {
        public abstract float Speed { get; set; }

        /// <summary>Сколько единиц в секунду реально проходит прямо сейчас. 0, когда стоит.</summary>
        public abstract float CurrentSpeed { get; }

        /// <summary>Истина, когда цели нет или она достигнута.</summary>
        public abstract bool HasArrived { get; }

        public abstract void SetDestination(Vector3 worldPosition);
        public abstract void Stop();
    }
}

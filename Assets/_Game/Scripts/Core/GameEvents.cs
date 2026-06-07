// GameEvents.cs — ScriptableObject-based event bus
using UnityEngine;
using UnityEngine.Events;

namespace OuterRim
{
    [CreateAssetMenu(menuName = "Outer Rim/Events/Game Event")]
    public class GameEvent : ScriptableObject
    {
        private readonly UnityEvent onRaise = new UnityEvent();

        public void Raise()
        {
            onRaise.Invoke();
        }

        public void AddListener(UnityAction action)
        {
            onRaise.AddListener(action);
        }

        public void RemoveListener(UnityAction action)
        {
            onRaise.RemoveListener(action);
        }
    }

    [CreateAssetMenu(menuName = "Outer Rim/Events/Phase Change Event")]
    public class PhaseChangeEvent : ScriptableObject
    {
        private readonly UnityEvent<GamePhase> onRaise = new UnityEvent<GamePhase>();

        public void Raise(GamePhase phase)
        {
            onRaise.Invoke(phase);
        }

        public void AddListener(UnityAction<GamePhase> action)
        {
            onRaise.AddListener(action);
        }

        public void RemoveListener(UnityAction<GamePhase> action)
        {
            onRaise.RemoveListener(action);
        }
    }
}

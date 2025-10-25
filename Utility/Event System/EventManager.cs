using System;
using System.Collections.Generic;
using Godot;

public static class EventManager
{
    private static readonly Dictionary<EventKeys, List<Action<EventParameters>>> eventListeners = new();
    public static void Subscribe(EventKeys eventKey, Action<EventParameters> listener)
    {
        if (!eventListeners.ContainsKey(eventKey))
        {
            eventListeners[eventKey] = new List<Action<EventParameters>>();
        }
        eventListeners[eventKey].Add(listener);
    }
    public static void Unsubscribe(EventKeys eventKey, Action<EventParameters> listener)
    {
        if (eventListeners.ContainsKey(eventKey))
        {
            eventListeners[eventKey].Remove(listener);
        }
    }
    public static void Invoke(EventKeys eventKey, EventParameters parameters = null)
    {
        if (eventListeners.ContainsKey(eventKey))
        {
            foreach (var listener in eventListeners[eventKey])
            {
                listener?.Invoke(parameters);
            }
        }
    }
}
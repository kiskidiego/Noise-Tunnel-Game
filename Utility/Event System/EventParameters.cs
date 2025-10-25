using System;
using System.Collections.Generic;

public class EventParameters
{
    Dictionary<EventParameterKeys, object> parameters = new Dictionary<EventParameterKeys, object>();
    public EventParameters Add(EventParameterKeys key, object value)
    {
        parameters[key] = value;
        return this;
    }
    public T Get<T>(EventParameterKeys key)
    {
        if (parameters.TryGetValue(key, out object value))
        {
            if (value is T t)
            {
                return t;
            }
            throw new InvalidCastException($"Parameter with key {key} is not of type {typeof(T)}.");
        }
        throw new KeyNotFoundException($"Parameter with key {key} not found.");
    }
    public T TryGet<T>(EventParameterKeys key)
    {
        if (parameters.TryGetValue(key, out object value))
        {
            if (value is T t)
            {
                return t;
            }
            throw new InvalidCastException($"Parameter with key {key} is not of type {typeof(T)}.");
        }
        return default;
    }
    public bool Has(EventParameterKeys key)
    {
        return parameters.ContainsKey(key);
    }
}
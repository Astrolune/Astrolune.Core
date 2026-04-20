using System.Collections.Concurrent;
using System.Text.Json;
using Astrolune.Runtime.Core.Server;

namespace Astrolune.Runtime.Core.Storage;

public sealed class EventBus : IEventBus
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, List<Action<object>>> _handlers = new();

    public void Publish<T>(string eventName, T data)
    {
        if (!_handlers.TryGetValue(eventName, out var handlers) || handlers.Count == 0)
        {
            return;
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        var payload = JsonSerializer.Deserialize<object>(json, JsonOptions) ?? new object();

        foreach (var handler in handlers.ToList())
        {
            try
            {
                handler(payload);
            }
            catch
            {
                // Ignore handler exceptions
            }
        }
    }

    public void Subscribe<T>(string eventName, Action<T> handler)
    {
        Action<object> wrappedHandler = payload =>
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var typedPayload = JsonSerializer.Deserialize<T>(json, JsonOptions);
            handler(typedPayload!);
        };

        _handlers.AddOrUpdate(
            eventName,
            _ => new List<Action<object>> { wrappedHandler },
            (_, list) =>
            {
                list.Add(wrappedHandler);
                return list;
            });
    }

    public void Unsubscribe(string eventName)
    {
        _handlers.TryRemove(eventName, out _);
    }
}

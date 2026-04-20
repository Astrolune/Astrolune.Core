using System.Text.Json;
using Astrolune.Runtime.Core.Models;

namespace Astrolune.Runtime.Core.Server;

public interface IModule
{
    string Name { get; }
    Task InitializeAsync(IRuntimeContext context, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    bool CanHandle(string method);
    Task<JsonElement> HandleAsync(string method, JsonElement? parameters, CancellationToken cancellationToken = default);
}

public interface IRuntimeContext
{
    IEventBus EventBus { get; }
    IStateManager StateManager { get; }
    ISecureStorage SecureStorage { get; }
    void SendUpdate(string type, object payload);
}

public interface IEventBus
{
    void Publish<T>(string eventName, T data);
    void Subscribe<T>(string eventName, Action<T> handler);
    void Unsubscribe(string eventName);
}

public interface IStateManager
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public interface ISecureStorage
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

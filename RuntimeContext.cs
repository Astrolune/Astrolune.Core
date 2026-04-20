using Astrolune.Runtime.Core.Server;
using Astrolune.Runtime.Core.Storage;

namespace Astrolune.Runtime.Core;

public sealed class RuntimeContext : IRuntimeContext
{
    private readonly UpdateDispatcher _dispatcher;

    public RuntimeContext(
        IEventBus eventBus,
        IStateManager stateManager,
        ISecureStorage secureStorage,
        UpdateDispatcher dispatcher)
    {
        EventBus = eventBus;
        StateManager = stateManager;
        SecureStorage = secureStorage;
        _dispatcher = dispatcher;
    }

    public IEventBus EventBus { get; }
    public IStateManager StateManager { get; }
    public ISecureStorage SecureStorage { get; }

    public void SendUpdate(string type, object payload)
    {
        _dispatcher.SendUpdate(type, payload);
    }
}

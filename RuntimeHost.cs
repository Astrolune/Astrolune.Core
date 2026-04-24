using Astrolune.Runtime.Core.Server;
using Astrolune.Runtime.Core.Modules;
using Astrolune.Runtime.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core;

public sealed class RuntimeHost : IAsyncDisposable
{
    private readonly RuntimeServer _server;
    private readonly CommandRouter _router;
    private readonly UpdateDispatcher _dispatcher;
    private readonly RuntimeContext _context;
    private readonly List<IModule> _modules = new();
    private readonly ILogger<RuntimeHost> _logger;

    public RuntimeHost(string pipeName, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RuntimeHost>();
        _dispatcher = new UpdateDispatcher(loggerFactory.CreateLogger<UpdateDispatcher>());
        _router = new CommandRouter(loggerFactory.CreateLogger<CommandRouter>());

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Astrolune");

        var stateManager = new StateManager(appDataPath, loggerFactory.CreateLogger<StateManager>());
        var secureStorage = new SecureStorage(appDataPath, loggerFactory.CreateLogger<SecureStorage>());
        var eventBus = new EventBus();

        _context = new RuntimeContext(eventBus, stateManager, secureStorage, _dispatcher);

        _server = new RuntimeServer(pipeName, _router, _dispatcher, loggerFactory.CreateLogger<RuntimeServer>());
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Initialize storage
        await ((StateManager)_context.StateManager).InitializeAsync(cancellationToken);
        await ((SecureStorage)_context.SecureStorage).InitializeAsync(cancellationToken);

        // Register modules
        var authModule = new AuthModule();
        await RegisterModuleAsync(authModule, cancellationToken);

        var chatModule = new ChatModule();
        await RegisterModuleAsync(chatModule, cancellationToken);

        var voiceModule = new VoiceModule();
        await RegisterModuleAsync(voiceModule, cancellationToken);

        var mediaModule = new MediaModule();
        await RegisterModuleAsync(mediaModule, cancellationToken);

        // Start server
        _server.Start();
        _logger.LogInformation("Runtime host started with {Count} modules", _modules.Count);
    }

    private async Task RegisterModuleAsync(IModule module, CancellationToken cancellationToken)
    {
        await module.InitializeAsync(_context, cancellationToken);
        _router.RegisterModule(module);
        _modules.Add(module);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var module in _modules)
        {
            await module.ShutdownAsync();
        }

        await _server.DisposeAsync();
        _logger.LogInformation("Runtime host stopped");
    }
}

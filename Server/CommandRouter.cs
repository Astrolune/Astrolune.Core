using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core.Server;

public sealed class CommandRouter
{
    private readonly List<IModule> _modules = new();
    private readonly ILogger<CommandRouter> _logger;

    public CommandRouter(ILogger<CommandRouter> logger)
    {
        _logger = logger;
    }

    public void RegisterModule(IModule module)
    {
        _modules.Add(module);
        _logger.LogInformation("Registered module: {ModuleName}", module.Name);
    }

    public async Task<JsonElement> RouteAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        var module = _modules.FirstOrDefault(m => m.CanHandle(method));
        if (module is null)
        {
            throw new InvalidOperationException($"No module can handle method: {method}");
        }

        return await module.HandleAsync(method, parameters, cancellationToken);
    }
}

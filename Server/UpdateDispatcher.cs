using System.Collections.Concurrent;
using System.Text.Json;
using Astrolune.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core.Server;

public sealed class UpdateDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentBag<StreamWriter> _clients = new();
    private readonly ILogger<UpdateDispatcher> _logger;

    public UpdateDispatcher(ILogger<UpdateDispatcher> logger)
    {
        _logger = logger;
    }

    public void RegisterClient(StreamWriter writer)
    {
        _clients.Add(writer);
        _logger.LogDebug("Client registered for updates");
    }

    public void UnregisterClient(StreamWriter writer)
    {
        _logger.LogDebug("Client unregistered from updates");
    }

    public void SendUpdate(string type, object payload)
    {
        var update = new Update(type, JsonSerializer.SerializeToElement(payload, JsonOptions), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var json = JsonSerializer.Serialize(update, JsonOptions);

        foreach (var client in _clients)
        {
            try
            {
                client.WriteLine(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send update to client");
            }
        }
    }
}

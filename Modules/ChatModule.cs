using System.Net.Http.Json;
using System.Text.Json;
using Astrolune.Runtime.Core.Server;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core.Modules;

public sealed class ChatModule : IModule
{
    private static readonly HttpClient Http = new();
    private IRuntimeContext _context = null!;
    private ILogger<ChatModule> _logger = null!;
    private string _messageBaseUrl = "http://localhost:5004";

    public string Name => "chat";

    public Task InitializeAsync(IRuntimeContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ChatModule>();
        _logger.LogInformation("Chat module initialized");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Chat module shutdown");
        return Task.CompletedTask;
    }

    public bool CanHandle(string method)
    {
        return method.StartsWith("chat.", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<JsonElement> HandleAsync(string method, JsonElement? parameters, CancellationToken cancellationToken = default)
    {
        return method.ToLowerInvariant() switch
        {
            "chat.sendmessage" => await SendMessageAsync(parameters, cancellationToken),
            "chat.getmessages" => await GetMessagesAsync(parameters, cancellationToken),
            "chat.deletemessage" => await DeleteMessageAsync(parameters, cancellationToken),
            "chat.addreaction" => await AddReactionAsync(parameters, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown chat method: {method}")
        };
    }

    private async Task<JsonElement> SendMessageAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new ArgumentException("Parameters required");

        var channelId = parameters.Value.GetProperty("channelId").GetString();
        var content = parameters.Value.GetProperty("content").GetString();

        var accessToken = await _context.SecureStorage.GetAsync("auth:access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Not authenticated");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_messageBaseUrl}/api/messages");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Content = JsonContent.Create(new { channelId, content });

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Failed to send message");

        var message = await response.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken);

        _context.SendUpdate("chat.message_sent", message!);

        return JsonSerializer.SerializeToElement(message);
    }

    private async Task<JsonElement> GetMessagesAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new ArgumentException("Parameters required");

        var channelId = parameters.Value.GetProperty("channelId").GetString();
        var limit = parameters.Value.TryGetProperty("limit", out var l) ? l.GetInt32() : 50;

        var accessToken = await _context.SecureStorage.GetAsync("auth:access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Not authenticated");

        var request = new HttpRequestMessage(HttpMethod.Get, $"{_messageBaseUrl}/api/messages/{channelId}?limit={limit}");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Failed to get messages");

        var messages = await response.Content.ReadFromJsonAsync<List<MessageResponse>>(cancellationToken);
        return JsonSerializer.SerializeToElement(messages);
    }

    private async Task<JsonElement> DeleteMessageAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new ArgumentException("Parameters required");

        var messageId = parameters.Value.GetProperty("messageId").GetString();

        var accessToken = await _context.SecureStorage.GetAsync("auth:access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Not authenticated");

        var request = new HttpRequestMessage(HttpMethod.Delete, $"{_messageBaseUrl}/api/messages/{messageId}");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Failed to delete message");

        _context.SendUpdate("chat.message_deleted", new { messageId });

        return JsonSerializer.SerializeToElement(new { ok = true });
    }

    private async Task<JsonElement> AddReactionAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new ArgumentException("Parameters required");

        var messageId = parameters.Value.GetProperty("messageId").GetString();
        var emoji = parameters.Value.GetProperty("emoji").GetString();

        var accessToken = await _context.SecureStorage.GetAsync("auth:access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Not authenticated");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_messageBaseUrl}/api/messages/{messageId}/reactions");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Content = JsonContent.Create(new { emoji });

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Failed to add reaction");

        _context.SendUpdate("chat.reaction_added", new { messageId, emoji });

        return JsonSerializer.SerializeToElement(new { ok = true });
    }

    private sealed record MessageResponse(string Id, string ChannelId, string AuthorId, string Content, DateTime CreatedAt);
}

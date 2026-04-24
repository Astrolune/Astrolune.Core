using System.Net.Http.Json;
using System.Text.Json;
using Astrolune.Runtime.Core.Server;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core.Modules;

public sealed class VoiceModule : IModule
{
    private static readonly HttpClient Http = new();
    private IRuntimeContext _context = null!;
    private ILogger<VoiceModule> _logger = null!;
    private string _voiceBaseUrl = "http://localhost:5006";
    private string _mediaBaseUrl = "http://localhost:5005";

    public string Name => "voice";

    public Task InitializeAsync(IRuntimeContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<VoiceModule>();
        _logger.LogInformation("Voice module initialized");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Voice module shutdown");
        return Task.CompletedTask;
    }

    public bool CanHandle(string method)
    {
        return method.StartsWith("voice.", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<JsonElement> HandleAsync(string method, JsonElement? parameters, CancellationToken cancellationToken = default)
    {
        return method.ToLowerInvariant() switch
        {
            "voice.join" => await JoinChannelAsync(parameters, cancellationToken),
            "voice.leave" => await LeaveChannelAsync(parameters, cancellationToken),
            "voice.gettoken" => await GetLiveKitTokenAsync(parameters, cancellationToken),
            "voice.mute" => await SetMuteAsync(parameters, true, cancellationToken),
            "voice.unmute" => await SetMuteAsync(parameters, false, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown voice method: {method}")
        };
    }

    private async Task<JsonElement> JoinChannelAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new ArgumentException("Parameters required");

        var channelId = parameters.Value.GetProperty("channelId").GetString();

        var accessToken = await _context.SecureStorage.GetAsync("auth:access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Not authenticated");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_voiceBaseUrl}/api/voice/join");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Content = JsonContent.Create(new { channelId });

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Failed to join voice channel");

        var result = await response.Content.ReadFromJsonAsync<VoiceJoinResponse>(cancellationToken);

        _context.SendUpdate("voice.joined", new { channelId });

        return JsonSerializer.SerializeToElement(result);
    }

    private async Task<JsonElement> LeaveChannelAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new ArgumentException("Parameters required");

        var channelId = parameters.Value.GetProperty("channelId").GetString();

        var accessToken = await _context.SecureStorage.GetAsync("auth:access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Not authenticated");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_voiceBaseUrl}/api/voice/leave");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Content = JsonContent.Create(new { channelId });

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Failed to leave voice channel");

        _context.SendUpdate("voice.left", new { channelId });

        return JsonSerializer.SerializeToElement(new { ok = true });
    }

    private async Task<JsonElement> GetLiveKitTokenAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new ArgumentException("Parameters required");

        var roomName = parameters.Value.GetProperty("roomName").GetString();

        var accessToken = await _context.SecureStorage.GetAsync("auth:access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Not authenticated");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_mediaBaseUrl}/api/livekit/token");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Content = JsonContent.Create(new { roomName });

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Failed to get LiveKit token");

        var result = await response.Content.ReadFromJsonAsync<LiveKitTokenResponse>(cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private async Task<JsonElement> SetMuteAsync(JsonElement? parameters, bool muted, CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new ArgumentException("Parameters required");

        var channelId = parameters.Value.GetProperty("channelId").GetString();

        var accessToken = await _context.SecureStorage.GetAsync("auth:access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Not authenticated");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_voiceBaseUrl}/api/voice/mute");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Content = JsonContent.Create(new { channelId, muted });

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Failed to set mute state");

        _context.SendUpdate("voice.mute_changed", new { channelId, muted });

        return JsonSerializer.SerializeToElement(new { ok = true, muted });
    }

    private sealed record VoiceJoinResponse(string SessionId, string ChannelId);
    private sealed record LiveKitTokenResponse(string Token, string Url);
}

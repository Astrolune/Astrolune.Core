using System.Net.Http.Json;
using System.Text.Json;
using Astrolune.Runtime.Core.Server;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core.Modules;

public sealed class AuthModule : IModule
{
    private static readonly HttpClient Http = new();
    private IRuntimeContext _context = null!;
    private ILogger<AuthModule> _logger = null!;
    private string _authBaseUrl = "http://localhost:5001";
    private Timer? _refreshTimer;

    public string Name => "auth";

    public Task InitializeAsync(IRuntimeContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AuthModule>();

        _ = RestoreSessionAsync(cancellationToken);

        _logger.LogInformation("Auth module initialized");
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _refreshTimer?.Dispose();
        _logger.LogInformation("Auth module shutdown");
        await Task.CompletedTask;
    }

    public bool CanHandle(string method)
    {
        return method.StartsWith("auth.", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<JsonElement> HandleAsync(string method, JsonElement? parameters, CancellationToken cancellationToken = default)
    {
        return method.ToLowerInvariant() switch
        {
            "auth.login" => await LoginAsync(parameters, cancellationToken),
            "auth.logout" => await LogoutAsync(cancellationToken),
            "auth.refresh" => await RefreshAsync(cancellationToken),
            "auth.getstate" => await GetStateAsync(cancellationToken),
            _ => throw new InvalidOperationException($"Unknown auth method: {method}")
        };
    }

    private async Task<JsonElement> LoginAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null)
        {
            throw new ArgumentException("Parameters required for login");
        }

        var username = parameters.Value.GetProperty("username").GetString();
        var password = parameters.Value.GetProperty("password").GetString();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Username and password required");
        }

        var response = await Http.PostAsJsonAsync(
            $"{_authBaseUrl}/api/auth/login",
            new { login = username, password },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Login failed");
        }

        var authData = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken);
        if (authData is null)
        {
            throw new InvalidOperationException("Invalid auth response");
        }

        await SaveAuthDataAsync(authData, cancellationToken);
        StartRefreshTimer();

        _context.SendUpdate("auth.state", new
        {
            authenticated = true,
            userId = authData.User.Id,
            username = authData.User.Username,
            role = authData.User.PlatformRole
        });

        return JsonSerializer.SerializeToElement(new
        {
            user = authData.User,
            accessToken = authData.AccessToken
        });
    }

    private async Task<JsonElement> LogoutAsync(CancellationToken cancellationToken)
    {
        await _context.SecureStorage.DeleteAsync("auth:access_token", cancellationToken);
        await _context.SecureStorage.DeleteAsync("auth:refresh_token", cancellationToken);
        await _context.StateManager.DeleteAsync("auth:user", cancellationToken);

        _refreshTimer?.Dispose();
        _refreshTimer = null;

        _context.SendUpdate("auth.state", new { authenticated = false });

        return JsonSerializer.SerializeToElement(new { ok = true });
    }

    private async Task<JsonElement> RefreshAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await _context.SecureStorage.GetAsync("auth:refresh_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("No refresh token available");
        }

        var response = await Http.PostAsJsonAsync(
            $"{_authBaseUrl}/api/auth/refresh",
            new { refreshToken },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await LogoutAsync(cancellationToken);
            throw new InvalidOperationException("Refresh failed");
        }

        var authData = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken);
        if (authData is null)
        {
            throw new InvalidOperationException("Invalid refresh response");
        }

        await SaveAuthDataAsync(authData, cancellationToken);

        return JsonSerializer.SerializeToElement(new { ok = true });
    }

    private async Task<JsonElement> GetStateAsync(CancellationToken cancellationToken)
    {
        var user = await _context.StateManager.GetAsync<UserInfo>("auth:user", cancellationToken);
        if (user is null)
        {
            return JsonSerializer.SerializeToElement(new { authenticated = false });
        }

        return JsonSerializer.SerializeToElement(new
        {
            authenticated = true,
            userId = user.Id,
            username = user.Username,
            role = user.PlatformRole
        });
    }

    private async Task SaveAuthDataAsync(AuthResponse authData, CancellationToken cancellationToken)
    {
        await _context.SecureStorage.SetAsync("auth:access_token", authData.AccessToken, cancellationToken);
        await _context.SecureStorage.SetAsync("auth:refresh_token", authData.RefreshToken, cancellationToken);
        await _context.StateManager.SetAsync("auth:user", authData.User, cancellationToken: cancellationToken);
    }

    private async Task RestoreSessionAsync(CancellationToken cancellationToken)
    {
        var user = await _context.StateManager.GetAsync<UserInfo>("auth:user", cancellationToken);
        if (user is not null)
        {
            StartRefreshTimer();
            _context.SendUpdate("auth.state", new
            {
                authenticated = true,
                userId = user.Id,
                username = user.Username,
                role = user.PlatformRole
            });
        }
    }

    private void StartRefreshTimer()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = new Timer(
            async _ => await RefreshAsync(CancellationToken.None),
            null,
            TimeSpan.FromMinutes(12),
            TimeSpan.FromMinutes(12));
    }

    private sealed record AuthResponse(string AccessToken, string RefreshToken, UserInfo User);
    private sealed record UserInfo(string Id, string Username, string PlatformRole);
}

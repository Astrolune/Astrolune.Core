using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Astrolune.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core.Client;

public sealed class RuntimeClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _pipeName;
    private readonly ILogger<RuntimeClient> _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private Task? _readerTask;
    private readonly CancellationTokenSource _cts = new();

    public event Action<Update>? OnUpdate;

    public RuntimeClient(string pipeName, ILogger<RuntimeClient> logger)
    {
        _pipeName = pipeName;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(5000, cancellationToken);

        _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };
        _readerTask = Task.Run(ReadLoopAsync, _cts.Token);

        _logger.LogInformation("Connected to runtime server: {PipeName}", _pipeName);
    }

    public async Task<JsonElement> SendAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Client not connected");
        }

        var requestId = Guid.NewGuid().ToString();
        var request = new Request(
            requestId,
            method,
            parameters is not null ? JsonSerializer.SerializeToElement(parameters, JsonOptions) : null);

        var tcs = new TaskCompletionSource<JsonElement>();
        _pendingRequests[requestId] = tcs;

        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            await _writer.WriteLineAsync(json);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            return await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        if (_pipe is null)
        {
            return;
        }

        using var reader = new StreamReader(_pipe, Encoding.UTF8, leaveOpen: true);

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                ProcessMessage(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from runtime server");
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out _))
            {
                var update = JsonSerializer.Deserialize<Update>(json, JsonOptions);
                if (update is not null)
                {
                    OnUpdate?.Invoke(update);
                }
                return;
            }

            var response = JsonSerializer.Deserialize<Response>(json, JsonOptions);
            if (response is null || !_pendingRequests.TryRemove(response.Id, out var tcs))
            {
                return;
            }

            if (response.Error is not null)
            {
                tcs.SetException(new RuntimeException(response.Error.Code, response.Error.Message));
            }
            else if (response.Result.HasValue)
            {
                tcs.SetResult(response.Result.Value);
            }
            else
            {
                tcs.SetResult(JsonSerializer.SerializeToElement(new { ok = true }, JsonOptions));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process message from runtime");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        if (_readerTask is not null)
        {
            await _readerTask;
        }

        _writer?.Dispose();
        _pipe?.Dispose();
        _cts.Dispose();
    }
}

public sealed class RuntimeException : Exception
{
    public string Code { get; }

    public RuntimeException(string code, string message) : base(message)
    {
        Code = code;
    }
}

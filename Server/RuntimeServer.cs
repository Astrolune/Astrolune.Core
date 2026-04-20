using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Astrolune.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core.Server;

public sealed class RuntimeServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _pipeName;
    private readonly CommandRouter _router;
    private readonly UpdateDispatcher _dispatcher;
    private readonly ILogger<RuntimeServer> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    public RuntimeServer(
        string pipeName,
        CommandRouter router,
        UpdateDispatcher dispatcher,
        ILogger<RuntimeServer> logger)
    {
        _pipeName = pipeName;
        _router = router;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public void Start()
    {
        _serverTask = Task.Run(RunServerAsync);
        _logger.LogInformation("Runtime server started on pipe: {PipeName}", _pipeName);
    }

    private async Task RunServerAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(_cts.Token);
                _logger.LogDebug("Client connected to runtime server");

                _ = Task.Run(() => HandleClientAsync(pipe), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client connection");
                await Task.Delay(1000, _cts.Token);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            _dispatcher.RegisterClient(writer);

            while (pipe.IsConnected && !_cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                _ = Task.Run(() => ProcessRequestAsync(line, writer), _cts.Token);
            }

            _dispatcher.UnregisterClient(writer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client");
        }
    }

    private async Task ProcessRequestAsync(string requestJson, StreamWriter writer)
    {
        try
        {
            var request = JsonSerializer.Deserialize<Request>(requestJson, JsonOptions);
            if (request is null)
            {
                await SendErrorAsync(writer, "unknown", "INVALID_REQUEST", "Failed to parse request");
                return;
            }

            var result = await _router.RouteAsync(request.Method, request.Params, _cts.Token);
            var response = new Response(request.Id, Result: result);
            await SendResponseAsync(writer, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request");
            await SendErrorAsync(writer, "unknown", "INTERNAL_ERROR", ex.Message);
        }
    }

    private static async Task SendResponseAsync(StreamWriter writer, Response response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        await writer.WriteLineAsync(json);
    }

    private static async Task SendErrorAsync(StreamWriter writer, string id, string code, string message)
    {
        var response = new Response(id, Error: new ErrorInfo(code, message));
        await SendResponseAsync(writer, response);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_serverTask is not null)
        {
            await _serverTask;
        }
        _cts.Dispose();
    }
}

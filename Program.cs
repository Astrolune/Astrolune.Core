using Astrolune.Runtime.Core;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

var appDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Astrolune");

Directory.CreateDirectory(appDataPath);
Directory.CreateDirectory(Path.Combine(appDataPath, "logs"));

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(appDataPath, "logs", "runtime-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

var loggerFactory = new SerilogLoggerFactory(Log.Logger, dispose: false);
var logger = loggerFactory.CreateLogger("Runtime");

logger.LogInformation("Astrolune Runtime starting...");

var host = new RuntimeHost("astrolune-runtime", loggerFactory);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await host.StartAsync(cts.Token);
    logger.LogInformation("Runtime is running. Press Ctrl+C to exit.");

    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    logger.LogInformation("Shutdown requested");
}
catch (Exception ex)
{
    logger.LogError(ex, "Runtime error");
}
finally
{
    await host.DisposeAsync();
    Log.CloseAndFlush();
}

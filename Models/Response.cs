using System.Text.Json;

namespace Astrolune.Runtime.Core.Models;

public sealed record Response(
    string Id,
    JsonElement? Result = null,
    ErrorInfo? Error = null
);

public sealed record ErrorInfo(
    string Code,
    string Message
);

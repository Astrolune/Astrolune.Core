using System.Text.Json;

namespace Astrolune.Runtime.Core.Models;

public sealed record Request(
    string Id,
    string Method,
    JsonElement? Params = null
);

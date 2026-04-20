using System.Text.Json;

namespace Astrolune.Runtime.Core.Models;

public sealed record Update(
    string Type,
    JsonElement Payload,
    long Timestamp
);

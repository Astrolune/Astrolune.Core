# Astrolune.Core

Core runtime library for the Astrolune platform with TDLib-inspired architecture.

## Overview

Astrolune.Core is an isolated runtime process that handles all business logic for the Astrolune desktop client. It communicates with client applications via IPC (Named Pipes on Windows, Unix Domain Sockets on Linux/macOS).

## Architecture

The runtime provides process-isolated core functionality:

- **Process Isolation**: Runtime runs separately from UI, crashes don't affect the app
- **IPC Communication**: Fast, secure inter-process communication via Named Pipes/UDS
- **Async API**: All operations use request/response pattern with unique IDs
- **Event-Driven**: Real-time updates via event subscriptions
- **Encrypted Storage**: Credentials protected with DPAPI/Keychain
- **Modular Design**: Clean separation of concerns (Auth, Chat, Voice, Realtime, Media)

## Modules

### Auth Module
- Login/logout
- Token refresh
- Session management
- Credential storage

### Chat Module
- Message CRUD operations
- Reactions and threads
- Message history

### Voice Module
- Channel join/leave
- Mute/deafen state
- Audio device management

### Realtime Module
- SignalR connection management
- Event subscriptions
- Real-time updates

### Media Module
- Screen capture
- Camera access
- Media streaming

## Usage

This library is typically not used directly. Instead, use the SDK:
- **C# SDK**: `Astrolune.SDK` (NuGet)
- **TypeScript SDK**: `@astrolune/core` (npm)

### Direct Usage (Advanced)

```csharp
using Astrolune.Core.Server;

// Start runtime server
var server = new RuntimeServer();
await server.StartAsync("astrolune-runtime");

// Server listens on named pipe: \\.\pipe\astrolune-runtime
```

## Building

### Prerequisites
- .NET 10.0 SDK or later

### Build Commands
```bash
# Build
dotnet build

# Run tests
dotnet test

# Create NuGet package
dotnet pack
```

## Configuration

Runtime configuration via `appsettings.json`:

```json
{
  "Runtime": {
    "PipeName": "astrolune-runtime",
    "DatabasePath": "%AppData%/Astrolune",
    "LogLevel": "Information"
  },
  "Auth": {
    "BaseUrl": "http://localhost:5001"
  },
  "Chat": {
    "MessageServiceUrl": "http://localhost:5004"
  },
  "Voice": {
    "VoiceServiceUrl": "http://localhost:5003"
  }
}
```

## Integration

This repository is designed to be used as a submodule in Astrolune.Desktop:

```bash
git submodule add https://github.com/Astrolune/Astrolune.Core.git core
```

## License

MIT

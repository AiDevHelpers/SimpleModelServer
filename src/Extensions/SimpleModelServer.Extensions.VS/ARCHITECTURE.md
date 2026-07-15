# SimpleModelServer VS Extension - Architecture

## Overview

The SimpleModelServer Visual Studio extension provides seamless IDE integration with the SimpleModelServer AI model router. It enables developers to leverage AI-powered code assistance directly within Visual Studio.

## Project Structure

```
SimpleModelServer.Extensions.VS/
├── Core/
│   ├── SettingsManager.cs          # Configuration management
│   └── ChatService.cs              # API communication with SimpleModelServer
├── UI/
│   ├── Chat/
│   │   ├── ChatToolWindow.xaml     # Chat tool window UI
│   │   └── ChatToolWindow.xaml.cs  # Code-behind
│   └── Options/
│       ├── OptionsPage.xaml        # Settings page UI
│       └── OptionsPage.xaml.cs     # Code-behind
├── Commands/
│   └── CommandHandlers.cs          # VS command implementations
├── ViewModels/
│   └── ChatViewModel.cs            # MVVM view model for chat
├── Utilities/
│   └── DiagnosticLogger.cs         # Logging infrastructure
├── ExtensionEntrypoint.cs          # Extension initialization and DI
├── source.extension.vsixmanifest   # VSIX manifest
└── SimpleModelServer.Extensions.VS.csproj
```

## Key Components

### 1. Core Services

#### SettingsManager.cs
- Manages all extension settings
- Stores configuration in Visual Studio's settings store
- Key settings:
  - Server host/port
  - API key
  - Default agent type
  - Feature toggles
  - Chat history preferences

#### ChatService.cs
- Handles HTTP communication with SimpleModelServer
- Implements streaming response handling
- Key methods:
  - `SendMessageAsync()` - Send messages to the model
  - `ExplainCodeAsync()` - Explain code snippets
  - `GenerateTestsAsync()` - Generate unit tests
  - `OptimizeCodeAsync()` - Suggest code optimizations
  - `GetAvailableModelsAsync()` - Fetch available models

### 2. User Interface

#### Chat Tool Window
- Docked window within Visual Studio
- Real-time message display with streaming
- Input field with Ctrl+Enter to send
- Agent type selector
- Clear chat history button
- Connection status indicator

Features:
- MVVM architecture with ChatViewModel
- WPF-based UI for VS 2022+
- Theme-aware styling using VS system colors
- Responsive layout

#### Options Page
- Server connection configuration
  - Host and port settings
  - API key input (password box)
  - Test connection button
- Agent configuration
  - Default agent selection
  - Feature toggles
- Settings are persisted to VS settings store

### 3. Commands

The extension registers multiple commands that integrate with VS:

- **ShowChatWindowCommand** - Opens the chat tool window
- **ExplainCodeCommand** - Explains selected code (context menu)
- **GenerateTestsCommand** - Generates tests for selected code (context menu)
- **OptimizeCodeCommand** - Suggests code optimizations (context menu)
- **OpenSettingsCommand** - Opens extension settings

All commands support:
- Proper error handling
- Logging via DiagnosticLogger
- Cancellation tokens for async operations
- User feedback via status messages

### 4. ViewModels

#### ChatViewModel.cs
- Implements INotifyPropertyChanged for WPF binding
- Key properties:
  - `CurrentMessage` - User input
  - `ResponseText` - AI response
  - `SelectedAgent` - Current agent type
  - `IsLoading` - Request in progress
  - `IsConnected` - Server connection status
  - `Messages` - Chat history

Key methods:
- `SendMessageAsync()` - Send and process response
- `ExplainCodeAsync()` - Send code for explanation
- `GenerateTestsAsync()` - Send code for test generation
- `CancelMessage()` - Cancel ongoing request
- `ClearChat()` - Clear chat history

### 5. Utilities

#### DiagnosticLogger.cs
- Centralized logging for the extension
- Methods: Info, Warning, Error, Debug
- Logs to:
  - VS debug output
  - Trace sources
  - System.Diagnostics.Trace

## API Integration

The extension communicates with SimpleModelServer via HTTP:

### Endpoints Used

1. **Chat/Generate**
   - `POST /api/generate`
   - Sends prompts and receives streamed responses
   - Supports different agent types

2. **Models**
   - `GET /v1/models`
   - Fetches list of available models

3. **Health Check**
   - `GET /health`
   - Verifies server connectivity

### Request/Response Format

**Request:**
```json
{
  "prompt": "user message",
  "model": "model-name",
  "stream": true,
  "agent_type": "Claude"
}
```

**Response (streaming):**
```json
{"response": "token1"}
{"response": "token2"}
...
```

## Dependency Injection

The ExtensionEntrypoint sets up dependency injection:

```csharp
serviceCollection.AddSingleton<SettingsManager>();
serviceCollection.AddSingleton<ChatService>();
serviceCollection.AddSingleton<DiagnosticLogger>();
serviceCollection.AddScoped<ShowChatWindowCommand>();
// ... other commands
```

## Extension Lifecycle

1. **Initialization**
   - VS loads the VSIX
   - ExtensionEntrypoint initializes services
   - SettingsManager loads persisted settings
   - Commands are registered with VS

2. **First Run**
   - Options page guides configuration
   - User enters server host/port
   - Connection test validates setup

3. **Runtime**
   - Chat tool window available on demand
   - Context menu commands available when editor has focus
   - All requests honor cancellation tokens
   - Settings persist across sessions

## Technology Stack

- **Framework**: .NET Framework 4.7.2 (.NET 4.72 target)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **VS SDK**: VisualStudio.Extensibility SDK 17.14+
- **Pattern**: MVVM (Model-View-ViewModel)
- **JSON**: System.Text.Json
- **Async**: C# async/await with IAsyncEnumerable for streaming

## Configuration

Settings are stored in:
`HKEY_CURRENT_USER\Software\Microsoft\VisualStudio\<version>\SimpleModelServer.Extensions.VS`

Key settings:
- `ServerHost`: Default "localhost"
- `ServerPort`: Default 11434
- `DefaultAgentType`: Default "Claude"
- `EnabledFeatures`: Comma-separated feature list
- `RememberLastChat`: Boolean flag

## Error Handling

All components implement comprehensive error handling:
- Try-catch blocks with logging
- User-facing error messages
- Graceful degradation on service unavailability
- Cancellation token support for long-running operations

## Future Enhancements

Planned features for future phases:

1. **Advanced Chat Features**
   - Multi-turn conversations with context
   - Code snippet highlighting with syntax coloring
   - Response history with search

2. **Editor Integration**
   - Code lens integration
   - Inline completion support
   - Documentation generation

3. **Model Management**
   - Model selection UI in chat
   - Download/manage models from VS

4. **Customization**
   - Custom system prompts
   - Temperature/parameter tuning
   - Custom agent types

5. **Performance**
   - Request queueing for parallel operations
   - Response caching
   - Lazy loading of models list

## Security Considerations

- API keys stored in password-protected VS settings
- No credentials logged to output
- HTTPS support for remote servers (future)
- Input validation on all user inputs
- Authorization header support for API key authentication

## Testing

Testing strategy:
1. Unit tests for services (SettingsManager, ChatService)
2. Integration tests with mock SimpleModelServer
3. UI tests for command execution
4. End-to-end tests with real server

## Debugging

Enable detailed logging:
1. Set DiagnosticLogger.EnableDebugOutput = true
2. View output in VS Debug window
3. Check Event Viewer for trace logs

## References

- [VS Extensibility Documentation](https://learn.microsoft.com/en-us/visualstudio/extensibility/)
- [VisualStudio.Extensibility SDK](https://github.com/microsoft/VSExtensibility)
- [WPF Documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- [SimpleModelServer API](../../../Server/SimpleModelServer.Server/README.md)

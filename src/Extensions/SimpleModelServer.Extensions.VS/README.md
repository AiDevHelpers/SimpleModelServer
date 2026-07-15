# SimpleModelServer Visual Studio Extension

A comprehensive Visual Studio 2025/2026 extension that brings AI-powered code assistance directly into your IDE. Integrates with SimpleModelServer to provide chat, code explanation, test generation, and optimization features.

## Features

### Core Capabilities

- **Interactive Chat Window** - Real-time chat with streaming responses from SimpleModelServer
- **Code Explanation** - Right-click on code → "Explain with SimpleModelServer"
- **Test Generation** - Automatically generate unit tests for selected code
- **Code Optimization** - Get optimization suggestions for your code
- **Connection Management** - Configure server settings with connection validation
- **Status Monitoring** - Real-time connection status in VS

### Advanced Features

- **Multi-Agent Support** - Choose different agent types (Claude, CodeAnalyzer, TestGenerator, CodeOptimizer)
- **Streaming Responses** - Real-time response display as the model generates text
- **Settings Persistence** - All settings saved in VS user store
- **Chat History** - Optional chat history with clear option
- **Cancellation Support** - Cancel long-running requests with a button click

## Installation

### From VSIX File

1. Download `SimpleModelServer.Extensions.VS.vsix`
2. Double-click to open in Visual Studio
3. Click "Install" in the installer dialog

### Manual Installation

1. Build the project: `dotnet build SimpleModelServer.Extensions.VS.csproj -c Release`
2. Find the VSIX in `bin/Release/`
3. Double-click the `.vsix` file to install

### From Source

1. Clone the repository
2. Open `SimpleModelServer.slnx` in Visual Studio
3. Build the Extensions project
4. Debug or package the VSIX

## Quick Start

### 1. Configure Connection

1. Open Visual Studio
2. Go to **Tools → SimpleModelServer Settings**
3. Enter:
   - **Server Host**: `localhost` (or your server IP)
   - **Server Port**: `11434` (or your configured port)
   - **API Key**: Leave empty unless required
4. Click **Test Connection** to verify

### 2. Open Chat Window

1. Press `Ctrl+Alt+M` (default shortcut) or go to **View → SimpleModelServer Chat**
2. The chat window docks at the bottom of the editor
3. Select your agent type (Claude, CodeAnalyzer, etc.)

### 3. Chat with AI

1. Type your message in the input field
2. Press **Send** or `Ctrl+Enter`
3. Watch responses stream in real-time
4. Clear chat history with the **Clear** button

### 4. Code Enhancement Features

#### Explain Code
1. Select code in the editor
2. Right-click → **Explain with SimpleModelServer**
3. Explanation appears in the chat window

#### Generate Tests
1. Select a class or method
2. Right-click → **Generate Tests with SimpleModelServer**
3. Review generated test code in chat

#### Optimize Code
1. Select code to optimize
2. Right-click → **Optimize Code with SimpleModelServer**
3. View optimization suggestions

## Settings

Access extension settings via **Tools → SimpleModelServer Settings**

### Server Configuration
- **Server Host**: SimpleModelServer address (default: `localhost`)
- **Server Port**: SimpleModelServer port (default: `11434`)
- **API Key**: Optional authentication key

### Agent Configuration
- **Default Agent**: Select default agent type for new chats
  - Claude: General purpose AI assistant
  - CodeAnalyzer: Specialized code analysis
  - TestGenerator: Focused on test generation
  - CodeOptimizer: Code optimization suggestions

### Features
- **Explain Code**: Enable/disable code explanation feature
- **Generate Tests**: Enable/disable test generation feature
- **Optimize Code**: Enable/disable code optimization feature
- **Remember Last Chat**: Restore chat history on startup

## Architecture

The extension uses a modern MVVM architecture:

```
UI Layer (WPF)
    ↓
ViewModel (ChatViewModel)
    ↓
Services (ChatService, SettingsManager)
    ↓
SimpleModelServer API (HTTP)
```

### Key Components

- **ChatService**: Handles all HTTP communication with SimpleModelServer
- **SettingsManager**: Manages configuration persistence
- **ChatViewModel**: MVVM view model for the chat UI
- **CommandHandlers**: VS command implementations
- **DiagnosticLogger**: Logging infrastructure

See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed documentation.

## Building from Source

### Prerequisites

- Visual Studio 2025 or later
- .NET Framework 4.7.2 SDK
- Visual Studio SDK (installed with VS)

### Build Steps

```powershell
# Restore NuGet packages
dotnet restore SimpleModelServer.Extensions.VS.csproj

# Build the extension
dotnet build SimpleModelServer.Extensions.VS.csproj -c Release

# Package as VSIX (optional)
dotnet pack SimpleModelServer.Extensions.VS.csproj -c Release
```

The compiled extension will be in `bin/Release/`

## Troubleshooting

### Connection Failed

**Problem**: "Connection failed" message when testing connection

**Solution**:
1. Verify SimpleModelServer is running on the configured host/port
2. Check firewall rules allow the connection
3. Verify the host and port in settings are correct
4. Try `curl http://localhost:11434/health` from command line

### Chat Window Not Appearing

**Problem**: Chat window doesn't open

**Solution**:
1. Try **View → Reset Window Layout**
2. Reinstall the extension
3. Check Event Viewer for error details

### Responses Not Streaming

**Problem**: Response appears all at once instead of streaming

**Solution**:
1. Verify streaming is enabled on SimpleModelServer
2. Check network connection speed
3. Try a different model

### Commands Not Appearing

**Problem**: Right-click commands missing from context menu

**Solution**:
1. Verify extension is installed: **Extensions → Manage Extensions** (search "SimpleModelServer")
2. Reload VS: **Tools → Import and Export Settings → Reset all settings**
3. Reinstall extension

## Performance

### Optimization Tips

- **Lazy Load Models**: Models list loads on first chat
- **Stream Responses**: Responses display as they arrive, not waiting for completion
- **Cancel Long Requests**: Use Cancel button if a response takes too long
- **Clear Old Chats**: Large chat histories may affect performance

### Performance Metrics

- Initial connection test: ~3 seconds
- Model list fetch: ~2-3 seconds
- Typical response streaming: Real-time (varies by model)
- Cancel response: Immediate

## Security

### Best Practices

- Store API keys securely in VS password settings
- Don't share chat history containing sensitive code
- Use HTTPS for remote servers (configure in settings)
- Credentials are never logged to output

### Data Privacy

- Chat history is stored locally in VS settings
- No data sent to external services (only to your SimpleModelServer)
- Clear chat history to remove local logs

## Advanced Usage

### Custom Agent Types

To use custom agent types:

1. Go to **Tools → SimpleModelServer Settings**
2. Update agent selection in chat window
3. Custom agents must be registered on SimpleModelServer

### Multiple Server Instances

Configure connection to different SimpleModelServer instances:

1. Edit settings before connecting
2. Test connection validates new host/port
3. Switch servers by updating settings

### Batch Operations

Generate tests for multiple files:

1. Open chat window
2. Paste code from file
3. Generate tests
4. Repeat for each file

## Contributing

Contributions welcome! See the main repository's CONTRIBUTING.md

### Build Your Own Feature

1. Create a new command class in `Commands/`
2. Implement the `ExecuteCommandAsync` method
3. Register in `ExtensionEntrypoint.cs`
4. Add menu item in manifest if needed

## Roadmap

Planned enhancements:

- [ ] Advanced chat context (multi-turn conversations)
- [ ] Syntax highlighted code display
- [ ] Inline code completion
- [ ] Documentation generation
- [ ] Code refactoring suggestions
- [ ] Performance analysis
- [ ] Custom prompt templates
- [ ] Model parameter tuning UI

## Known Limitations

- Requires .NET Framework 4.7.2+
- Works with Visual Studio 2025+
- Code context limited to current selection
- No real-time collaboration yet

## FAQ

**Q: Does this extension send data to external services?**
A: No, all requests go only to your local SimpleModelServer instance.

**Q: Can I use this with remote servers?**
A: Yes, configure the host and port in settings to your remote server.

**Q: Is there a cost?**
A: No, the extension is free and open-source.

**Q: Can I contribute to development?**
A: Yes! See CONTRIBUTING.md in the main repository.

**Q: What versions of Visual Studio are supported?**
A: VS 2025 and 2026 (includes Community, Professional, and Enterprise editions).

## Support

- **Issues**: Report bugs on [GitHub Issues](https://github.com/rsperry79/SimpleModelServer/issues)
- **Documentation**: See [ARCHITECTURE.md](ARCHITECTURE.md) for technical details
- **Server Setup**: See SimpleModelServer main [README.md](../../../README.md)

## License

This extension is part of SimpleModelServer. See LICENSE.txt for details.

## Credits

Developed as part of the SimpleModelServer project.

## Version History

### 1.0.0 (Current)
- Initial release
- Chat window
- Code explanation, test generation, optimization
- Settings management
- Multi-agent support

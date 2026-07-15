# SimpleModelServer VS Extension - Quick Start Guide

## Installation

1. **Get the Extension**
   - Build from source: `dotnet build -c Release`
   - Output: `bin/Release/net472/SimpleModelServer.Extensions.VS.vsix`

2. **Install in Visual Studio**
   - Double-click the `.vsix` file, OR
   - Extensions → Manage Extensions → Search "SimpleModelServer" → Install

3. **Restart Visual Studio**

## Initial Setup

1. **Open Settings**
   - Tools → Options → SimpleModelServer

2. **Configure Server**
   - Server Host: `localhost` (or your server IP)
   - Server Port: `11434` (default Ollama)
   - API Key: Leave blank unless required

3. **Test Connection**
   - Click "Test Connection" button
   - Verify "Connection successful" message

## Using the Extension

### Open Chat Window
- Command Palette: `Ctrl+Shift+P` → "Show SimpleModelServer Chat"
- Chat interface opens with agent selector

### Explain Code
1. Select code in editor
2. Right-click → "Explain with SimpleModelServer"
3. AI analyzes and explains the code

### Generate Tests
1. Select method/class
2. Right-click → "Generate Tests with SimpleModelServer"
3. AI creates unit tests

### Optimize Code
1. Select code snippet
2. Right-click → "Optimize Code with SimpleModelServer"
3. Get optimization suggestions

## Configuration Options

| Setting | Default | Description |
|---------|---------|-------------|
| Server Host | localhost | Address of SimpleModelServer |
| Server Port | 11434 | Port number for API |
| API Key | (empty) | Authentication token if required |
| Default Agent | Claude | AI agent to use |
| Explain Code | ✓ | Enable code explanation |
| Generate Tests | ✓ | Enable test generation |
| Optimize Code | ✓ | Enable code optimization |
| Remember Chat | ✓ | Save chat history |

## Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| Send Message | Ctrl+Enter |
| Open Chat | Ctrl+Shift+P → Show Chat |
| Open Settings | Tools → Options |

## Troubleshooting

### "Connection Failed"
- Check SimpleModelServer is running
- Verify host and port in settings
- Check firewall rules

### "Extension Not Loading"
- Extensions → Manage Extensions → Check for errors
- Restart Visual Studio
- Check Debug Output for logs

### Commands Not Appearing
- Command Palette: `Ctrl+Shift+P`
- Type "SimpleModelServer" to see all commands
- Ensure extension is installed and enabled

## Supported Languages

C#, Python, JavaScript, TypeScript, Java, C++, Go, Rust, PHP, Ruby, SQL, HTML, CSS, JSON, XML, YAML, Bash, PowerShell

## Getting Help

1. Check Debug Output window for diagnostic logs
2. Review extension settings
3. Test server connection
4. Consult IMPLEMENTATION_GUIDE.md for technical details

## Tips & Tricks

- Use different agents for different tasks:
  - **Claude**: General code questions
  - **CodeAnalyzer**: Deep code analysis
  - **TestGenerator**: Unit test creation
  - **CodeOptimizer**: Performance improvements

- Save time by selecting specific methods/functions before requesting explanations

- Test connection before working to ensure server is ready

- Check status bar for connection state

---

**Happy Coding with SimpleModelServer AI!**

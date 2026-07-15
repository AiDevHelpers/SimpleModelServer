# SimpleModelServer VS Extension - Implementation Guide

This guide provides detailed instructions for implementing the remaining features and extending the extension functionality.

## Current Status

### Completed Phase 1-2: Foundation & Infrastructure

✅ Project structure created
✅ VSIX manifest configured for VS 2025+
✅ Core services implemented:
- SettingsManager
- ChatService
- DiagnosticLogger

✅ MVVM infrastructure:
- ChatViewModel with complete binding support
- Observable collections for messages

✅ UI components (partial):
- Chat tool window XAML structure
- Options page XAML structure
- Code-behind implementations

✅ Commands framework:
- Command handlers for all main operations
- Extension entry point with DI

## Next Steps: Phase 3-6 Implementation

### Phase 3: Complete Chat Tool Window

#### Current State
- XAML defined with proper layout
- Code-behind implemented
- ViewModel ready

#### What's Needed

1. **Tool Window Registration**
```csharp
// In ExtensionEntrypoint.cs
private static class ToolWindows
{
    [VisualStudioContribution]
    public static readonly EditorGroupDefinition ChatToolWindowGroup = new(
        id: new("SimpleModelServer.ChatToolWindow"),
        title: "SimpleModelServer Chat"
    );
}
```

2. **Tool Window Implementation**
- Derive from `ToolWindowPane`
- Set content to ChatToolWindowControl
- Handle window lifecycle events

3. **Command for Opening Tool Window**
```csharp
public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
{
    var shell = context.Shell();
    var toolWindow = await shell.CreateToolWindowAsync<ChatToolWindow>(cancellationToken);
    await shell.ShowToolWindowAsync(toolWindow, activate: true, cancellationToken);
}
```

### Phase 4: Options Page Implementation

#### Current State
- Options XAML defined
- Settings binding implemented
- Test connection functionality exists

#### What's Needed

1. **Create OptionsPageProvider**
```csharp
[VisualStudioContribution]
internal class SimpleModelServerOptionsProvider : OptionsPageProvider
{
    private readonly SettingsManager _settings;
    
    public override Task<FrameworkElement> GetPageAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<FrameworkElement>(
            new OptionsPageControl() { DataContext = new OptionsPageViewModel(_settings) }
        );
    }
}
```

2. **Settings Persistence Hook**
- Implement ISaveableSettings interface
- Hook into Visual Studio settings store
- Use SettingsManager to load/save

3. **Category Registration**
- Register in extension manifest
- Set category path for settings dialog

### Phase 5: Context Menu Commands

#### Current State
- Command classes created
- Placeholder implementations present
- DiagnosticLogger integrated

#### What's Needed

1. **Editor Content Selection Access**
```csharp
public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
{
    // Get the active document editor
    var activeDocument = context.GetActiveTextDocumentView();
    if (activeDocument?.Selection == null)
    {
        await context.Shell().ShowPromptAsync(
            "No code selected. Please select code first.",
            PromptOptions.OK,
            cancellationToken
        );
        return;
    }
    
    // Get selected text
    var selectedText = activeDocument.Selection.GetSelectedTextAsync(cancellationToken);
    
    // Process with service
    await _chatService.ExplainCodeAsync(selectedText, language: "csharp");
}
```

2. **Context Menu Registration**
```xml
<!-- In manifest or code -->
<CommandPlacement guid="..." id="ExplainCodeCommand" 
    priority="0x0100">
    <Parent guid="..." id="EditorContextMenuId" />
</CommandPlacement>
```

3. **Language Detection**
```csharp
private string DetectLanguageFromDocument(ITextDocumentSnapshot document)
{
    var extension = Path.GetExtension(document.FilePath);
    return extension switch
    {
        ".cs" => "csharp",
        ".cpp" or ".cc" or ".h" => "cpp",
        ".py" => "python",
        ".js" => "javascript",
        ".ts" => "typescript",
        ".java" => "java",
        _ => "plaintext"
    };
}
```

### Phase 6: Advanced Features

#### 6.1 Inline Completion

```csharp
// Create a completion source
internal class AICompletionSource : IAsyncCompletionSource
{
    public Task<CompletionStartData> GetCompletionStartAsync(
        IAsyncCompletionSession session, CancellationToken cancellationToken)
    {
        // Trigger on Ctrl+Alt+Space
        return Task.FromResult(new CompletionStartData(
            applicableTo: session.ApplicableToSpan,
            items: await GetCompletionsAsync(cancellationToken)
        ));
    }
    
    public Task<CompletionContext> GetCompletionContextAsync(
        IAsyncCompletionSession session, CancellationToken cancellationToken)
    {
        // Return AI-generated completion items
    }
}
```

#### 6.2 Code Lens Integration

```csharp
[Export(typeof(ICodeLensSourceProvider))]
internal class AICodeLensSourceProvider : ICodeLensSourceProvider
{
    public ICodeLensSource Create(ICodeLensSnapshot snapshot)
    {
        return new AICodeLensSource(_chatService, snapshot);
    }
}
```

#### 6.3 Status Bar Integration

```csharp
private void UpdateStatusBar(bool isConnected, string agentType)
{
    var statusBar = _context?.Shell()?.StatusBar;
    var message = isConnected 
        ? $"SimpleModelServer: {agentType} (Ready)"
        : "SimpleModelServer: Disconnected";
    
    statusBar?.Message = message;
}
```

## Testing Strategy

### Unit Tests

```csharp
[TestFixture]
public class SettingsManagerTests
{
    [Test]
    public void ServerHost_SetAndGet_PersistsValue()
    {
        var manager = new SettingsManager();
        manager.ServerHost = "example.com";
        Assert.AreEqual("example.com", manager.ServerHost);
    }
}

[TestFixture]
public class ChatServiceTests
{
    private ChatService _service;
    private Mock<HttpClient> _httpClient;
    
    [SetUp]
    public void Setup()
    {
        _httpClient = new Mock<HttpClient>();
        _service = new ChatService(new SettingsManager(), _httpClient.Object);
    }
    
    [Test]
    public async Task SendMessageAsync_WithValidInput_ReturnsResponse()
    {
        // Arrange
        var mockResponse = "Test response";
        
        // Act
        var result = await _service.SendMessageAsync("Test", "Claude", "default");
        
        // Assert
        Assert.IsNotNull(result);
    }
}
```

### Integration Tests

```csharp
[TestFixture]
public class ChatIntegrationTests
{
    private ChatService _service;
    private const string TestServerUrl = "http://localhost:11434";
    
    [SetUp]
    public void Setup()
    {
        var settings = new SettingsManager();
        _service = new ChatService(settings);
    }
    
    [Test]
    public async Task FullChatFlow_SendAndReceive_Works()
    {
        // Test with actual server (requires server running)
        var isConnected = await _service.TestConnectionAsync();
        if (!isConnected) Assert.Inconclusive("Test server not running");
        
        var response = new StringBuilder();
        await foreach (var chunk in _service.SendMessageAsync("Hello", "Claude", "default"))
        {
            response.Append(chunk);
        }
        
        Assert.IsNotEmpty(response.ToString());
    }
}
```

## Extension Points

### Adding New Commands

1. Create class deriving from `CommandHandlerBase`
2. Implement `ExecuteCommandAsync`
3. Register in `ExtensionEntrypoint.cs`
4. Add string resource entry

### Adding New Features

1. Extend `ChatService` with new method
2. Create ViewModel method calling the service
3. Bind UI control to ViewModel command
4. Add settings if needed

### Custom Agents

Extend the agent types in SettingsManager:

```csharp
public static readonly string[] AvailableAgents = new[]
{
    "Claude",
    "CodeAnalyzer",
    "TestGenerator",
    "CodeOptimizer",
    "CustomAgent" // Add here
};
```

## Performance Optimization

### Async Operations

All long-running operations should use async:

```csharp
public async Task<string> ProcessAsync()
{
    // Use ConfigureAwait for library code
    var result = await _service.SendAsync();
    
    // Proper cancellation support
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    return await _service.GetResultAsync(cts.Token);
}
```

### Lazy Loading

Load models and agent lists on demand:

```csharp
private Lazy<Task<List<string>>> _models;

public async Task<List<string>> GetModelsAsync()
{
    return await _models.Value;
}
```

### Caching

Cache frequently accessed data:

```csharp
private Dictionary<string, string> _responseCache = new();

public async Task<string> GetCachedResponseAsync(string key)
{
    if (_responseCache.TryGetValue(key, out var cached))
        return cached;
    
    var result = await FetchAsync(key);
    _responseCache[key] = result;
    return result;
}
```

## Debugging Tips

### Enable Trace Output

```csharp
var traceListener = new System.Diagnostics.TextWriterTraceListener(
    System.Diagnostics.Debug.WriteLine);
System.Diagnostics.Trace.Listeners.Add(traceListener);
```

### Debug with Remote Server

```csharp
// Add to diagnostic logger
var isRemote = !_settings.ServerHost.StartsWith("localhost");
if (isRemote)
{
    _logger.Info($"Connecting to remote server: {_settings.GetServerUrl()}");
}
```

### Mock Testing

```csharp
var mockService = new Mock<ChatService>();
mockService
    .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
    .ReturnsAsync("Mocked response");
```

## Common Issues & Solutions

### Issue: Settings Not Persisting

**Solution**: Ensure SettingsManager is singleton-scoped:
```csharp
serviceCollection.AddSingleton<SettingsManager>();
```

### Issue: UI Not Updating

**Solution**: Verify ViewModel implements INotifyPropertyChanged:
```csharp
public string Message
{
    get => _message;
    set => SetProperty(ref _message, value);
}
```

### Issue: Commands Not Executing

**Solution**: Check command is registered in extension:
```csharp
serviceCollection.AddScoped<YourCommand>();
```

## Building for Distribution

### Create VSIX Package

```powershell
dotnet build -c Release
# Output: bin/Release/SimpleModelServer.Extensions.VS.vsix
```

### Sign the VSIX (Optional)

```powershell
signtool sign /f cert.pfx /p password /t http://timestamp.server.com output.vsix
```

### Validate VSIX

```powershell
# Use VS Extension Verification Tool
VSIXSignTool verify /v output.vsix
```

## Documentation

Always document:
- Complex algorithms
- Public APIs
- Configuration options
- Breaking changes

Use XML doc comments:

```csharp
/// <summary>
/// Sends a message to the AI model and streams the response.
/// </summary>
/// <param name="message">The user message to send</param>
/// <param name="agentType">Type of agent to use</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>Async enumerable of response chunks</returns>
public async IAsyncEnumerable<string> SendMessageAsync(
    string message,
    string agentType,
    CancellationToken cancellationToken = default)
```

## Release Checklist

Before releasing a new version:

- [ ] All unit tests pass
- [ ] Integration tests pass with sample server
- [ ] Code reviewed
- [ ] Documentation updated
- [ ] CHANGELOG.md updated
- [ ] Version number bumped
- [ ] VSIX manifest updated
- [ ] VSIX package built and tested
- [ ] Published to marketplace

## Resources

- [VS Extensibility Samples](https://github.com/microsoft/VSExtensibility)
- [VS SDK Documentation](https://learn.microsoft.com/en-us/visualstudio/extensibility/)
- [WPF MVVM Patterns](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/model-view-viewmodel-mvvm-pattern)
- [C# Async Best Practices](https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)

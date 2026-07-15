using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SimpleModelServer.Chat.Maui.Services;
using Microsoft.Maui.Storage;

namespace SimpleModelServer.Chat.Maui.Pages;

/// <summary>
/// Chat page that displays the conversation and allows users to send messages.
/// </summary>
public partial class ChatPage : ContentPage
{
    private readonly ChatPageViewModel _viewModel;

    public ChatPage(ChatService chatService)
    {
        InitializeComponent();
        _viewModel = new ChatPageViewModel(chatService);
        BindingContext = _viewModel;
    }
}

/// <summary>
/// ViewModel for the chat page.
/// Handles message management, sending, and file attachment.
/// </summary>
public class ChatPageViewModel : INotifyPropertyChanged
{
    private readonly ChatService _chatService;
    private ObservableCollection<ChatMessageDisplay> _messages = [];
    private string _inputText = string.Empty;
    private bool _isSending = false;
    private CancellationTokenSource? _sendCts;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChatMessageDisplay> Messages
    {
        get => _messages;
        set
        {
            if (_messages != value)
            {
                _messages = value;
                OnPropertyChanged();
            }
        }
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (_inputText != value)
            {
                _inputText = value;
                OnPropertyChanged();
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSending
    {
        get => _isSending;
        set
        {
            if (_isSending != value)
            {
                _isSending = value;
                OnPropertyChanged();
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
                ((RelayCommand)AttachFileCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand SendCommand { get; }
    public ICommand AttachFileCommand { get; }

    public ChatPageViewModel(ChatService chatService)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));

        SendCommand = new RelayCommand(
            execute: _ => SendAsync().FireAndForget(),
            canExecute: _ => !string.IsNullOrWhiteSpace(InputText) && !IsSending
        );

        AttachFileCommand = new RelayCommand(
            execute: _ => AttachFileAsync().FireAndForget(),
            canExecute: _ => !IsSending
        );

        // Add initial system message
        Messages.Add(new ChatMessageDisplay
        {
            Role = ChatMessageRole.Assistant,
            Text = "Hello! I'm your AI assistant. How can I help you today?",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsSending)
        {
            return;
        }

        try
        {
            IsSending = true;
            _sendCts = new CancellationTokenSource();

            var userMessage = InputText.Trim();
            InputText = string.Empty;

            // Add user message to the conversation
            Messages.Add(new ChatMessageDisplay
            {
                Role = ChatMessageRole.User,
                Text = userMessage,
                Timestamp = DateTimeOffset.UtcNow
            });

            // Create an empty assistant message that will be filled in as tokens arrive
            var assistantMessage = new ChatMessageDisplay
            {
                Role = ChatMessageRole.Assistant,
                Text = string.Empty,
                Timestamp = DateTimeOffset.UtcNow
            };
            Messages.Add(assistantMessage);

            // Build chat history for context
            var history = Messages
                .Where(m => m.Role == ChatMessageRole.User || m.Role == ChatMessageRole.Assistant)
                .Select(m => new ChatMessage { Role = m.Role, Text = m.Text, Timestamp = m.Timestamp })
                .ToList();

            // Send the message and stream the response
            var tokenCount = 0;
            await _chatService.SendAsync(
                history,
                userMessage,
                token =>
                {
                    assistantMessage.Text += token;
                    tokenCount++;
                    OnPropertyChanged(nameof(Messages)); // Trigger UI update
                },
                _sendCts.Token
            );
        }
        catch (OperationCanceledException)
        {
            // User cancelled the operation
        }
        catch (Exception ex)
        {
            var page = Application.Current?.Windows[0]?.Page;
            if (page != null)
            {
                await page.DisplayAlert(
                    "Error",
                    $"Failed to send message: {ex.Message}",
                    "OK"
                );
            }
        }
        finally
        {
            IsSending = false;
            _sendCts?.Dispose();
            _sendCts = null;
        }
    }

    private async Task AttachFileAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle = "Select a file"
                }
            );

            if (result != null)
            {
                // Append the filename to the input text
                var fileName = Path.GetFileName(result.FullPath);
                InputText += (string.IsNullOrWhiteSpace(InputText) ? "" : " ") + $"[Attached: {fileName}]";
            }
        }
        catch (Exception ex)
        {
            var page = Application.Current?.Windows[0]?.Page;
            if (page != null)
            {
                await page.DisplayAlert(
                    "Error",
                    $"Failed to pick file: {ex.Message}",
                    "OK"
                );
            }
        }
    }

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// Display model for a chat message in the UI.
/// </summary>
public class ChatMessageDisplay : INotifyPropertyChanged
{
    private string _text = string.Empty;

    public ChatMessageRole Role { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTimeOffset Timestamp { get; set; }

    public string TimestampDisplay => Timestamp.LocalDateTime.ToString("HH:mm");

    public bool IsUserMessage => Role == ChatMessageRole.User;
    public bool IsAssistantMessage => Role == ChatMessageRole.Assistant;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// Simple command implementation for MAUI.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool> _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute ?? (_ => true);
    }

    public bool CanExecute(object? parameter) => _canExecute(parameter);

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty)
        );
    }
}

/// <summary>
/// Extension method to fire and forget an async task.
/// </summary>
internal static class TaskExtensions
{
    public static void FireAndForget(this Task task)
    {
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                System.Diagnostics.Debug.WriteLine($"FireAndForget task failed: {t.Exception}");
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}

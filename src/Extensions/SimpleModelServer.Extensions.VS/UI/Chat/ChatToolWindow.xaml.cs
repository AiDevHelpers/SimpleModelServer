using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SimpleModelServer.Extensions.VS.ViewModels;
using SimpleModelServer.UI.Core;

// Use WPF UserControl, not Windows Forms
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace SimpleModelServer.Extensions.VS.UI.Chat
{
    /// <summary>
    /// Chat Tool Window Control
    /// </summary>
    public partial class ChatToolWindowControl : UserControl
    {
        private ChatViewModel? _viewModel;
        private readonly SettingsManager _settingsManager;
        private readonly ChatService _chatService;

        public ChatToolWindowControl()
        {
            InitializeComponent();

            // Initialize services
            _settingsManager = new SettingsManager();
            _chatService = new ChatService(_settingsManager);

            // Initialize view model
            _viewModel = new ChatViewModel(_chatService, _settingsManager);
            DataContext = _viewModel;

            // Agent types are already populated by ViewModel constructor
            // No need to add them again

            // Initialize asynchronously
            Loaded += async (s, e) =>
            {
                if (_viewModel != null)
                {
                    await _viewModel.InitializeAsync();
                }
            };
        }

        private void SendButtonClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.SendMessageAsync().FireAndForget();
            }
        }

        private void CancelButtonClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.CancelMessage();
            }
        }

        private void ClearButtonClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.ClearChat();
            }
        }

        private void SettingsButtonClick(object sender, RoutedEventArgs e)
        {
            // This will be handled by the extension infrastructure
            // to show the options page
        }

        private void MessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            // Send on Ctrl+Enter
            if (e.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                SendButtonClick(sender, e);
            }
        }
    }

    /// <summary>
    /// Extension methods for async void handling
    /// </summary>
    internal static class TaskExtensions
    {
        public static void FireAndForget(this System.Threading.Tasks.Task task)
        {
            // This is a fire-and-forget pattern - not ideal but works for UI events
            task.ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Task failed: {t.Exception}");
                }
            });
        }
    }
}

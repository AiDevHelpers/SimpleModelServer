using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SimpleModelServer.UI.Core;

namespace SimpleModelServer.Extensions.VS.ViewModels
{
	/// <summary>
	/// ViewModel for the chat window.
	/// </summary>
	public class ChatViewModel : INotifyPropertyChanged
	{
		private readonly ChatService _chatService;
		private readonly SettingsManager _settingsManager;
		private readonly DiagnosticLogger _logger;

		private string _currentMessage = string.Empty;
		private string _responseText = string.Empty;
		private string _selectedAgent = "Claude";
		private string _selectedModel = "default";
		private bool _isLoading = false;
		private bool _isConnected = false;
		private CancellationTokenSource? _currentCancellationTokenSource;

		public event PropertyChangedEventHandler? PropertyChanged;

		public ObservableCollection<ChatMessage> Messages { get; }
		public ObservableCollection<string> AgentTypes { get; }

		public ChatViewModel(ChatService chatService, SettingsManager settingsManager)
		{
			_chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
			_settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
			_logger = new DiagnosticLogger("ChatViewModel");
			Messages = new ObservableCollection<ChatMessage>();
			AgentTypes = new ObservableCollection<string> { "Claude", "CodeAnalyzer", "TestGenerator", "CodeOptimizer" };
		}

		public string CurrentMessage
		{
			get => _currentMessage;
			set => SetProperty(ref _currentMessage, value);
		}

		public string ResponseText
		{
			get => _responseText;
			set => SetProperty(ref _responseText, value);
		}

		public string SelectedAgent
		{
			get => _selectedAgent;
			set
			{
				if (SetProperty(ref _selectedAgent, value))
				{
					_settingsManager.DefaultAgentType = value;
				}
			}
		}

		public string SelectedModel
		{
			get => _selectedModel;
			set => SetProperty(ref _selectedModel, value);
		}

		public bool IsLoading
		{
			get => _isLoading;
			set => SetProperty(ref _isLoading, value);
		}

		public bool IsConnected
		{
			get => _isConnected;
			set => SetProperty(ref _isConnected, value);
		}

		/// <summary>
		/// Initializes the view model asynchronously.
		/// </summary>
		public async Task InitializeAsync()
		{
			try
			{
				IsConnected = await _chatService.TestConnectionAsync();
				_logger.Info($"Connection test: {(IsConnected ? "Connected" : "Disconnected")}");

				if (IsConnected)
				{
					// Load available models
					var models = await _chatService.GetAvailableModelsAsync();
					_logger.Info($"Loaded {models.Count} models");
				}
			}
			catch (Exception ex)
			{
				_logger.Error("Failed to initialize", ex);
			}
		}

		/// <summary>
		/// Sends a message to the chat service.
		/// </summary>
		public async Task SendMessageAsync()
		{
			if (string.IsNullOrWhiteSpace(CurrentMessage) || IsLoading || !IsConnected)
			{
				return;
			}

			var message = CurrentMessage;
			CurrentMessage = string.Empty;
			ResponseText = string.Empty;

			// Add user message to history
			Messages.Add(new ChatMessage
			{
				Content = message,
				IsFromUser = true,
				Timestamp = DateTime.Now
			});

			IsLoading = true;
			_currentCancellationTokenSource = new CancellationTokenSource();

			try
			{
				var response = new System.Text.StringBuilder();

				await foreach (var chunk in _chatService.SendMessageAsync(
					message,
					SelectedAgent,
					SelectedModel,
					_currentCancellationTokenSource.Token))
				{
					response.Append(chunk);
					ResponseText = response.ToString();
				}

				// Add assistant message to history
				Messages.Add(new ChatMessage
				{
					Content = response.ToString(),
					IsFromUser = false,
					Timestamp = DateTime.Now
				});
			}
			catch (OperationCanceledException)
			{
				ResponseText += "\n[Message cancelled by user]";
				_logger.Info("Message sending cancelled");
			}
			catch (Exception ex)
			{
				ResponseText = $"Error: {ex.Message}";
				_logger.Error("Failed to send message", ex);
			}
			finally
			{
				IsLoading = false;
				_currentCancellationTokenSource?.Dispose();
				_currentCancellationTokenSource = null;
			}
		}

		/// <summary>
		/// Cancels the current message sending.
		/// </summary>
		public void CancelMessage()
		{
			_currentCancellationTokenSource?.Cancel();
		}

		/// <summary>
		/// Clears the chat history.
		/// </summary>
		public void ClearChat()
		{
			Messages.Clear();
			ResponseText = string.Empty;
			CurrentMessage = string.Empty;
		}

		/// <summary>
		/// Explains the given code.
		/// </summary>
		public async Task ExplainCodeAsync(string code, string language)
		{
			ResponseText = string.Empty;
			IsLoading = true;
			_currentCancellationTokenSource = new CancellationTokenSource();

			try
			{
				var response = new System.Text.StringBuilder();

				await foreach (var chunk in _chatService.ExplainCodeAsync(
					code,
					language,
					"detailed",
					_currentCancellationTokenSource.Token))
				{
					response.Append(chunk);
					ResponseText = response.ToString();
				}

				Messages.Add(new ChatMessage
				{
					Content = $"Explain ({language}):\n{response}",
					IsFromUser = false,
					Timestamp = DateTime.Now
				});
			}
			catch (Exception ex)
			{
				ResponseText = $"Error: {ex.Message}";
				_logger.Error("Failed to explain code", ex);
			}
			finally
			{
				IsLoading = false;
				_currentCancellationTokenSource?.Dispose();
			}
		}

		/// <summary>
		/// Generates tests for the given code.
		/// </summary>
		public async Task GenerateTestsAsync(string code, string language)
		{
			ResponseText = string.Empty;
			IsLoading = true;
			_currentCancellationTokenSource = new CancellationTokenSource();

			try
			{
				var response = new System.Text.StringBuilder();

				await foreach (var chunk in _chatService.GenerateTestsAsync(
					code,
					language,
					_currentCancellationTokenSource.Token))
				{
					response.Append(chunk);
					ResponseText = response.ToString();
				}

				Messages.Add(new ChatMessage
				{
					Content = $"Tests for ({language}):\n{response}",
					IsFromUser = false,
					Timestamp = DateTime.Now
				});
			}
			catch (Exception ex)
			{
				ResponseText = $"Error: {ex.Message}";
				_logger.Error("Failed to generate tests", ex);
			}
			finally
			{
				IsLoading = false;
				_currentCancellationTokenSource?.Dispose();
			}
		}

		protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
		{
			if (Equals(field, value)) return false;
			field = value;
			OnPropertyChanged(propertyName);
			return true;
		}
	}

	/// <summary>
	/// Represents a message in the chat.
	/// </summary>
	public class ChatMessage
	{
		public string Content { get; set; } = string.Empty;
		public bool IsFromUser { get; set; }
		public DateTime Timestamp { get; set; }
	}
}

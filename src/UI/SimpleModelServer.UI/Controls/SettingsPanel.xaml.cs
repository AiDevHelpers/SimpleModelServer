using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SimpleModelServer.UI.Core;

namespace SimpleModelServer.UI.Controls
{
	/// <summary>
	/// Reusable Settings Panel Control for Simple Model Server configuration.
	/// Can be used in any WPF application that needs to configure Simple Model Server settings.
	/// </summary>
#pragma warning disable CA1063 // Sealed UI controls don't need Dispose(bool) pattern
	public partial class SettingsPanel : UserControl, IDisposable
#pragma warning restore CA1063
	{
		private SettingsManager? _settingsManager;
		private ChatService? _chatService;
		private AgentConfigService? _agentConfigService;
		private GoldenSetRunner? _goldenSetRunner;

		/// <summary>Initializes the settings panel.</summary>
		public SettingsPanel()
		{
			InitializeComponent();
			InitializeSettings();
			Unloaded += (s, e) => Dispose();
		}

#pragma warning disable CA1063 // Dispose implementation for UI control is sufficient
		/// <summary>Releases the resources held by the panel.</summary>
		public void Dispose()
		{
			_chatService?.Dispose();
			_agentConfigService?.Dispose();
			GC.SuppressFinalize(this);
		}
#pragma warning restore CA1063

		private void InitializeSettings()
		{
			_settingsManager = new SettingsManager();
			_chatService = new ChatService(_settingsManager);
			_agentConfigService = new AgentConfigService(_settingsManager);
			_goldenSetRunner = new GoldenSetRunner(_settingsManager);

			// Load current settings into controls
			ServerHostTextBox.Text = _settingsManager.ServerHost;
			ServerPortTextBox.Text = _settingsManager.ServerPort.ToString();
			DefaultAgentComboBox.SelectedValue = _settingsManager.DefaultAgentType;
			RememberChatCheckBox.IsChecked = _settingsManager.RememberLastChat;

			// Wire up change handlers
			ServerHostTextBox.TextChanged += (s, e) =>
			{
				if (_settingsManager != null)
					_settingsManager.ServerHost = ServerHostTextBox.Text;
			};

			ServerPortTextBox.TextChanged += (s, e) =>
			{
				if (_settingsManager != null && int.TryParse(ServerPortTextBox.Text, out var port))
					_settingsManager.ServerPort = port;
			};

			DefaultAgentComboBox.SelectionChanged += (s, e) =>
			{
				if (_settingsManager != null && DefaultAgentComboBox.SelectedValue is string agent)
					_settingsManager.DefaultAgentType = agent;
			};

			RememberChatCheckBox.Checked += (s, e) =>
			{
				if (_settingsManager != null)
					_settingsManager.RememberLastChat = RememberChatCheckBox.IsChecked ?? false;
			};

			RememberChatCheckBox.Unchecked += (s, e) =>
			{
				if (_settingsManager != null)
					_settingsManager.RememberLastChat = RememberChatCheckBox.IsChecked ?? false;
			};

			_ = LoadAgentPromptsAsync();
		}

		private async Task LoadAgentPromptsAsync()
		{
			if (_agentConfigService == null)
				return;

			var agents = await _agentConfigService.GetAgentsAsync();

			AgentPromptComboBox.ItemsSource = agents;
			if (agents.Count > 0)
				AgentPromptComboBox.SelectedIndex = 0;
		}

		private async void RefreshAgentPromptsClick(object sender, RoutedEventArgs e)
		{
			RefreshAgentPromptsButton.IsEnabled = false;
			try
			{
				await LoadAgentPromptsAsync();
			}
			finally
			{
				RefreshAgentPromptsButton.IsEnabled = true;
			}
		}

		private void AgentPromptComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			AgentPromptTextBox.Text = AgentPromptComboBox.SelectedItem is AgentPromptInfo agent
				? agent.SystemPrompt
				: string.Empty;
		}

		private async void SaveAgentPromptClick(object sender, RoutedEventArgs e)
		{
			if (_agentConfigService == null || AgentPromptComboBox.SelectedItem is not AgentPromptInfo agent)
				return;

			SaveAgentPromptButton.IsEnabled = false;
			StatusMessage.Text = $"Saving prompt for '{agent.Name}'...";
			StatusMessage.Foreground = System.Windows.Media.Brushes.Blue;

			try
			{
				var result = await _agentConfigService.UpdateAgentPromptAsync(agent.Name, AgentPromptTextBox.Text);

				if (!result.Success)
				{
					StatusMessage.Text = result.Message;
					StatusMessage.Foreground = System.Windows.Media.Brushes.Red;
					return;
				}

				StatusMessage.Text = result.Message;
				StatusMessage.Foreground = System.Windows.Media.Brushes.Green;

				await LoadAgentPromptsAsync();
				SelectAgentByName(agent.Name);

				if (result.SuggestGoldenSet)
				{
					await OfferGoldenSetRunAsync(agent.Name);
				}
			}
			finally
			{
				SaveAgentPromptButton.IsEnabled = true;
			}
		}

		private void SelectAgentByName(string agentName)
		{
			if (AgentPromptComboBox.ItemsSource is System.Collections.Generic.IEnumerable<AgentPromptInfo> agents)
			{
				foreach (var agent in agents)
				{
					if (string.Equals(agent.Name, agentName, StringComparison.OrdinalIgnoreCase))
					{
						AgentPromptComboBox.SelectedItem = agent;
						break;
					}
				}
			}
		}

		private async Task OfferGoldenSetRunAsync(string agentName)
		{
			var choice = MessageBox.Show(
				$"The '{agentName}' prompt changed. Run the golden set now to validate the new prompt's routing behavior?",
				"Run golden set?",
				MessageBoxButton.YesNo,
				MessageBoxImage.Question);

			if (choice != MessageBoxResult.Yes || _goldenSetRunner == null)
				return;

			StatusMessage.Text = "Running golden set...";
			StatusMessage.Foreground = System.Windows.Media.Brushes.Blue;

			var result = await _goldenSetRunner.RunRouterEvalAsync();

			if (!result.Started)
			{
				StatusMessage.Text = result.Output;
				StatusMessage.Foreground = System.Windows.Media.Brushes.Red;
				return;
			}

			bool passed = result.ExitCode == 0;
			StatusMessage.Text = passed
				? "Golden set passed."
				: $"Golden set finished with exit code {result.ExitCode}. See output below.";
			StatusMessage.Foreground = passed
				? System.Windows.Media.Brushes.Green
				: System.Windows.Media.Brushes.OrangeRed;

			MessageBox.Show(result.Output, "Golden set output", MessageBoxButton.OK,
				passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
		}

		private async void TestConnectionClick(object sender, RoutedEventArgs e)
		{
			if (_chatService == null)
			{
				StatusMessage.Text = "Error: Service not initialized";
				StatusMessage.Foreground = System.Windows.Media.Brushes.Red;
				return;
			}

			TestConnectionButton.IsEnabled = false;
			StatusMessage.Text = "Testing connection...";
			StatusMessage.Foreground = System.Windows.Media.Brushes.Blue;

			try
			{
				var isConnected = await _chatService.TestConnectionAsync();
				if (isConnected)
				{
					StatusMessage.Text = "Connection successful! Extension is ready to use.";
					StatusMessage.Foreground = System.Windows.Media.Brushes.Green;
				}
				else
				{
					StatusMessage.Text = "Connection failed. Please check server host and port.";
					StatusMessage.Foreground = System.Windows.Media.Brushes.Red;
				}
			}
			catch (Exception ex)
			{
				StatusMessage.Text = $"Error: {ex.Message}";
				StatusMessage.Foreground = System.Windows.Media.Brushes.Red;
			}
			finally
			{
				TestConnectionButton.IsEnabled = true;
			}
		}

		/// <summary>
		/// Gets the current settings manager instance.
		/// </summary>
		public SettingsManager? SettingsManager => _settingsManager;

		/// <summary>
		/// Gets the current chat service instance.
		/// </summary>
		public ChatService? ChatService => _chatService;
	}
}

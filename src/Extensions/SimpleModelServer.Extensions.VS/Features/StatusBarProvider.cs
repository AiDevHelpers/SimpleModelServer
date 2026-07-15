using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using SimpleModelServer.UI.Core;

namespace SimpleModelServer.Extensions.VS.Features
{
    /// <summary>
    /// Manages the status bar display for SimpleModelServer connection status and operations.
    /// </summary>
    [VisualStudioContribution]
    internal class StatusBarProvider : INotifyPropertyChanged
    {
        private readonly ChatService _chatService;
        private readonly SettingsManager _settingsManager;
        private readonly DiagnosticLogger _logger;
        private readonly IClientContext? _clientContext;

        private static readonly Dictionary<string, string> ProviderLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ollama"] = "Ollama",
            ["lmstudio"] = "LM Studio"
        };

        private string _connectionStatus = "Checking...";
        private string _currentAgent = "Ready";
        private bool _isOperationInProgress = false;
        private string? _provider;

        public event PropertyChangedEventHandler? PropertyChanged;

        public StatusBarProvider(ChatService chatService, SettingsManager settingsManager, IClientContext? clientContext = null)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _logger = new DiagnosticLogger("StatusBarProvider");
            _clientContext = clientContext;

            // Start monitoring connection in the background
            _ = InitializeStatusAsync();
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            private set => SetProperty(ref _connectionStatus, value);
        }

        public string CurrentAgent
        {
            get => _currentAgent;
            set => SetProperty(ref _currentAgent, value);
        }

        public bool IsOperationInProgress
        {
            get => _isOperationInProgress;
            set => SetProperty(ref _isOperationInProgress, value);
        }

        public string StatusBarText
        {
            get
            {
                if (IsOperationInProgress)
                {
                    return $"SimpleModelServer: {CurrentAgent} (Processing...)";
                }

                string? providerLabel = _provider is not null && ProviderLabels.TryGetValue(_provider, out string? label)
                    ? label
                    : _provider;

                return ConnectionStatus == "Connected" && providerLabel is not null
                    ? $"SimpleModelServer: {ConnectionStatus} ({providerLabel})"
                    : $"SimpleModelServer: {ConnectionStatus}";
            }
        }

        /// <summary>
        /// Initializes the status bar and starts monitoring connection.
        /// </summary>
        private async Task InitializeStatusAsync()
        {
            try
            {
                // Test connection
                var isConnected = await _chatService.TestConnectionAsync();
                ConnectionStatus = isConnected ? "Connected" : "Disconnected";
                CurrentAgent = _settingsManager.DefaultAgentType;
                // Only worth asking for the provider when the server is actually reachable —
                // avoids a second doomed request on every failed connection check.
                _provider = isConnected ? await _chatService.GetProviderAsync() : null;
                OnPropertyChanged(nameof(StatusBarText));

                _logger.Info($"Status bar initialized: {ConnectionStatus}");

                // Start periodic connection checks
                _ = MonitorConnectionAsync();
            }
            catch (Exception ex)
            {
                _logger.Error("Error initializing status bar", ex);
                ConnectionStatus = "Error";
            }
        }

        /// <summary>
        /// Periodically monitors the connection status.
        /// </summary>
        private async Task MonitorConnectionAsync()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30)); // Check every 30 seconds

                    var isConnected = await _chatService.TestConnectionAsync();
                    ConnectionStatus = isConnected ? "Connected" : "Disconnected";
                    _provider = isConnected ? await _chatService.GetProviderAsync() : null;

                    OnPropertyChanged(nameof(StatusBarText));
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Connection check failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Notifies that an operation has started.
        /// </summary>
        public void OperationStarted(string operationName)
        {
            CurrentAgent = operationName;
            IsOperationInProgress = true;
            OnPropertyChanged(nameof(StatusBarText));
            _logger.Info($"Operation started: {operationName}");
        }

        /// <summary>
        /// Notifies that an operation has completed.
        /// </summary>
        public void OperationCompleted()
        {
            CurrentAgent = _settingsManager.DefaultAgentType;
            IsOperationInProgress = false;
            OnPropertyChanged(nameof(StatusBarText));
            _logger.Info("Operation completed");
        }

        /// <summary>
        /// Notifies that an operation has failed.
        /// </summary>
        public void OperationFailed(string errorMessage)
        {
            CurrentAgent = $"Error: {errorMessage}";
            IsOperationInProgress = false;
            OnPropertyChanged(nameof(StatusBarText));
            _logger.Error($"Operation failed: {errorMessage}");
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}

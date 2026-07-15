using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.UI;
using SimpleModelServer.UI.Core;

namespace SimpleModelServer.Extensions.VS.Commands
{
    /// <summary>
    /// Base class for command handlers in the extension.
    /// </summary>
    public abstract class CommandHandlerBase : Command
    {
        protected readonly ChatService ChatService;
        protected readonly SettingsManager SettingsManager;
        protected readonly DiagnosticLogger Logger;

        protected CommandHandlerBase(
            ChatService chatService,
            SettingsManager settingsManager,
            string displayName)
        {
            ChatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            SettingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            Logger = new DiagnosticLogger(GetType().Name);
        }

        public override CommandConfiguration CommandConfiguration => new(GetDisplayName());

        protected abstract string GetDisplayName();
    }

    /// <summary>
    /// Command to show the chat tool window.
    /// </summary>
    [VisualStudioContribution]
    public class ShowChatWindowCommand : CommandHandlerBase
    {
        public ShowChatWindowCommand(
            ChatService chatService,
            SettingsManager settingsManager)
            : base(chatService, settingsManager, "Simple Model Chat")
        {
        }

        protected override string GetDisplayName() => "Simple Model Chat";

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                Logger.Info("Show Chat Window command executed");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show chat window", ex);
            }
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Command to explain selected code.
    /// </summary>
    [VisualStudioContribution]
    public class ExplainCodeCommand : CommandHandlerBase
    {
        public ExplainCodeCommand(
            ChatService chatService,
            SettingsManager settingsManager)
            : base(chatService, settingsManager, "Explain Code")
        {
        }

        protected override string GetDisplayName() => "Explain with SimpleModelServer";

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                Logger.Info("Explain code command executed");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to explain code", ex);
            }
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Command to generate tests for selected code.
    /// </summary>
    [VisualStudioContribution]
    public class GenerateTestsCommand : CommandHandlerBase
    {
        public GenerateTestsCommand(
            ChatService chatService,
            SettingsManager settingsManager)
            : base(chatService, settingsManager, "Generate Tests")
        {
        }

        protected override string GetDisplayName() => "Generate Tests with SimpleModelServer";

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                Logger.Info("Generate tests command executed");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to generate tests", ex);
            }
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Command to optimize selected code.
    /// </summary>
    [VisualStudioContribution]
    public class OptimizeCodeCommand : CommandHandlerBase
    {
        public OptimizeCodeCommand(
            ChatService chatService,
            SettingsManager settingsManager)
            : base(chatService, settingsManager, "Optimize Code")
        {
        }

        protected override string GetDisplayName() => "Optimize Code with SimpleModelServer";

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                Logger.Info("Optimize code command executed");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to optimize code", ex);
            }
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Command to open extension settings.
    /// </summary>
    [VisualStudioContribution]
    public class OpenSettingsCommand : Command
    {
        private readonly DiagnosticLogger _logger = new DiagnosticLogger("OpenSettingsCommand");

        public override CommandConfiguration CommandConfiguration => new("Open SimpleModelServer Settings")
        {
            Icon = new(ImageMoniker.KnownValues.Settings, IconSettings.IconAndText),
        };

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info("Open Settings command executed");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to open settings", ex);
            }
            await Task.CompletedTask;
        }
    }
}

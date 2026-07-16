using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using SimpleModelServer.UI.Core;
using SimpleModelServer.Discovery;
using SimpleModelServer.Extensions.VS.Commands;
using SimpleModelServer.Extensions.VS.UI.Chat;
using SimpleModelServer.Extensions.VS.Features;

namespace SimpleModelServer.Extensions.VS
{
    /// <summary>
    /// Extension entrypoint for the VisualStudio.Extensibility extension.
    /// Manages dependency injection and initialization of all extension services.
    /// </summary>
    [VisualStudioContribution]
    internal class ExtensionEntrypoint : Extension
    {
        /// <inheritdoc />
        public override ExtensionConfiguration ExtensionConfiguration => new()
        {
            RequiresInProcessHosting = true,
        };

        /// <inheritdoc />
        protected override void InitializeServices(IServiceCollection serviceCollection)
        {
            base.InitializeServices(serviceCollection);

            // Register core services
            serviceCollection.AddSingleton<SettingsManager>();
            serviceCollection.AddSingleton<ChatService>();
            serviceCollection.AddSingleton<DiagnosticLogger>();
            serviceCollection.AddSingleton<HttpClient>();

            // Register discovery services for server connection management
            serviceCollection.AddSingleton<IServerConnectionStore>(sp =>
                new SettingsManagerConnectionStore(sp.GetRequiredService<SettingsManager>()));
            serviceCollection.AddSingleton<IServerLocator>(sp =>
                new ServerLocator(sp.GetRequiredService<IServerConnectionStore>(), sp.GetService<HttpClient>()));

            // Register tool windows
            serviceCollection.AddScoped<ChatToolWindow>();

            // Register commands
            serviceCollection.AddScoped<ShowChatWindowCommand>();
            serviceCollection.AddScoped<ExplainCodeCommand>();
            serviceCollection.AddScoped<GenerateTestsCommand>();
            serviceCollection.AddScoped<OptimizeCodeCommand>();
            serviceCollection.AddScoped<OpenSettingsCommand>();

            // Register advanced features
            serviceCollection.AddSingleton<StatusBarProvider>();
        }
    }
}

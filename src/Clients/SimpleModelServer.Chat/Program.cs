using SimpleModelServer.Config;
using SimpleModelServer.Common.Infrastructure;
using SimpleModelServer.Common.Interfaces;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Providers.Common.Utilities;

ChatOptions options = ChatOptions.Parse(args);

if (options.Help)
{
    Console.WriteLine("""

                      USAGE
                        SimpleModelServer.Chat [options]

                      DESCRIPTION
                        Interactive chat interface using the draft-pair local model setup.
                        Requires SimpleModelServer.Loader to already be running (--service or --router).
                        All settings (agent type, context size, model names) come from the config file.

                      OPTIONS
                        --config <path>    Config JSON path (default: alongside the exe)
                        --help             Show this help

                      CONFIG FILE (SimpleModelServer.config.json)
                        global.defaultAgentType    Active agent: code, debug, planning
                        global.contextSize         num_ctx sent to the router
                        global.draftModel          Draft model name
                        global.mainModel           Main model name
                        global.routerPort          SimpleModelServer.Loader router port to connect to (default 11435)

                      """);
    return 0;
}

string configPath = options.ConfigPath ?? DefaultConfig.ChatConfigPath();
DefaultConfig.EnsureConfig(configPath);
IAppConfig config = AppConfig.Load(configPath);

IOllamaHelpers ollamaHelpers = new OllamaHelpers();
IConsoleHelper consoleHelper = new ConsoleHelper();
using ChatApp app = new(config, ollamaHelpers, consoleHelper);
return await app.RunAsync();

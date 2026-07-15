import * as vscode from "vscode";
import { RouterClient } from "./routerClient";
import { SimpleModelServerChatProvider } from "./chatModelProvider";
import { SettingsManager, RouterSettings } from "./settings";
import { StatusBarManager } from "./statusBar";
import { CommandsManager } from "./commands";

let chatProvider: SimpleModelServerChatProvider | null = null;
let statusBar: StatusBarManager | null = null;
let commands: CommandsManager | null = null;
let client: RouterClient | null = null;
let settingsManager: SettingsManager | null = null;

export async function activate(context: vscode.ExtensionContext) {
  console.log("SimpleModelServer VS Code extension activating...");

  settingsManager = new SettingsManager();
  const settings = settingsManager.getSettings();

  // Initialize router client
  client = new RouterClient(settings.host, settings.port, settings.apiKey);

  // Create and register the chat model provider
  try {
    chatProvider = new SimpleModelServerChatProvider(client);

    // Register with vscode.lm
    const disposable = vscode.lm.registerLanguageModelChatProvider(
      "simplemodelserver",
      chatProvider
    );

    context.subscriptions.push(disposable);
    console.log("Language model chat provider registered");
  } catch (err) {
    console.error("Failed to register chat provider:", err);
    vscode.window.showErrorMessage(
      `Failed to register SimpleModelServer as a chat provider: ${err instanceof Error ? err.message : String(err)}`
    );
  }

  // Initialize status bar
  statusBar = new StatusBarManager(client, settings.healthCheckInterval);
  statusBar.show();
  context.subscriptions.push(statusBar);

  // Initialize commands
  commands = new CommandsManager(client);
  context.subscriptions.push(commands);

  // Register commands
  context.subscriptions.push(
    vscode.commands.registerCommand("simplemodelserver.explainCode", async () => {
      const editor = vscode.window.activeTextEditor;
      if (editor) {
        await commands?.explainCode(editor);
      } else {
        vscode.window.showErrorMessage("No active editor");
      }
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      "simplemodelserver.generateTests",
      async () => {
        const editor = vscode.window.activeTextEditor;
        if (editor) {
          await commands?.generateTests(editor);
        } else {
          vscode.window.showErrorMessage("No active editor");
        }
      }
    )
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("simplemodelserver.optimizeCode", async () => {
      const editor = vscode.window.activeTextEditor;
      if (editor) {
        await commands?.optimizeCode(editor);
      } else {
        vscode.window.showErrorMessage("No active editor");
      }
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("simplemodelserver.openSettings", async () => {
      await commands?.openSettings();
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      "simplemodelserver.testConnection",
      async () => {
        await commands?.testConnection();
      }
    )
  );

  // Listen for settings changes
  const settingsChangeListener = settingsManager.onDidChangeSettings(
    async (newSettings: RouterSettings) => {
      console.log("Settings changed, updating configuration");

      // Update router client with new settings
      if (client) {
        client.setConnection(newSettings.host, newSettings.port, newSettings.apiKey);
      }

      // Update status bar interval
      if (statusBar && client) {
        statusBar.updateClient(client, newSettings.healthCheckInterval);
      }

      // Update chat provider with new client
      if (chatProvider && client) {
        chatProvider.updateClient(client);
      }

      // Update commands with new client
      if (commands && client) {
        commands.updateClient(client);
      }
    }
  );

  context.subscriptions.push(settingsChangeListener);

  // Verify connection on activation (optional warning if not connected)
  try {
    const isHealthy = await client.checkHealth();
    if (!isHealthy) {
      console.warn(
        "SimpleModelServer router not responding. Check settings and ensure the server is running."
      );
    }
  } catch (err) {
    console.warn("Could not verify SimpleModelServer connection:", err);
  }

  console.log("SimpleModelServer VS Code extension activated");
}

export function deactivate() {
  console.log("SimpleModelServer VS Code extension deactivating...");

  if (statusBar) {
    statusBar.dispose();
  }

  if (commands) {
    commands.dispose();
  }

  chatProvider = null;
  statusBar = null;
  commands = null;
  client = null;
  settingsManager = null;

  console.log("SimpleModelServer VS Code extension deactivated");
}

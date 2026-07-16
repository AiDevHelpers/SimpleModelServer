import * as vscode from "vscode";

export type ConnectionMode = "local" | "manual" | "mdns";

export interface RouterSettings {
  connectionMode: ConnectionMode;
  host: string;
  port: number;
  apiKey: string;
  defaultAgent: string;
  healthCheckInterval: number;
}

export class SettingsManager {
  private readonly configSection = "simplemodelserver";

  getSettings(): RouterSettings {
    const config = vscode.workspace.getConfiguration(this.configSection);

    return {
      connectionMode: (config.get<ConnectionMode>("connectionMode") || "local") as ConnectionMode,
      host: config.get<string>("host") || "localhost",
      port: config.get<number>("port") || 11435,
      apiKey: config.get<string>("apiKey") || "",
      defaultAgent: config.get<string>("defaultAgent") || "auto",
      healthCheckInterval: config.get<number>("healthCheckInterval") || 30,
    };
  }

  async setConnectionMode(mode: ConnectionMode): Promise<void> {
    await vscode.workspace
      .getConfiguration(this.configSection)
      .update("connectionMode", mode, vscode.ConfigurationTarget.Global);
  }

  async setHost(host: string): Promise<void> {
    await vscode.workspace
      .getConfiguration(this.configSection)
      .update("host", host, vscode.ConfigurationTarget.Global);
  }

  async setPort(port: number): Promise<void> {
    await vscode.workspace
      .getConfiguration(this.configSection)
      .update("port", port, vscode.ConfigurationTarget.Global);
  }

  async setApiKey(apiKey: string): Promise<void> {
    await vscode.workspace
      .getConfiguration(this.configSection)
      .update("apiKey", apiKey, vscode.ConfigurationTarget.Global);
  }

  async setDefaultAgent(agent: string): Promise<void> {
    await vscode.workspace
      .getConfiguration(this.configSection)
      .update("defaultAgent", agent, vscode.ConfigurationTarget.Global);
  }

  async setHealthCheckInterval(interval: number): Promise<void> {
    await vscode.workspace
      .getConfiguration(this.configSection)
      .update("healthCheckInterval", interval, vscode.ConfigurationTarget.Global);
  }

  onDidChangeSettings(
    callback: (settings: RouterSettings) => void
  ): vscode.Disposable {
    return vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration(this.configSection)) {
        callback(this.getSettings());
      }
    });
  }
}

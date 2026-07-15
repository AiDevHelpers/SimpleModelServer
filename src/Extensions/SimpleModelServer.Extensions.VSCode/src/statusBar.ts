import * as vscode from "vscode";
import { RouterClient } from "./routerClient";

export class StatusBarManager {
  private statusBar: vscode.StatusBarItem;
  private healthCheckTimer: NodeJS.Timeout | null = null;
  private isHealthy: boolean = false;
  private client: RouterClient;
  private updateInterval: number;

  constructor(client: RouterClient, updateInterval: number = 30) {
    this.client = client;
    this.updateInterval = updateInterval;

    this.statusBar = vscode.window.createStatusBarItem(
      vscode.StatusBarAlignment.Right,
      100
    );
    this.statusBar.command = "simplemodelserver.openSettings";
    this.statusBar.tooltip = "Click to open SimpleModelServer settings";

    this.updateStatusBar();
  }

  show(): void {
    this.statusBar.show();
    this.startHealthCheck();
  }

  hide(): void {
    this.statusBar.hide();
    this.stopHealthCheck();
  }

  dispose(): void {
    this.stopHealthCheck();
    this.statusBar.dispose();
  }

  updateClient(client: RouterClient, updateInterval: number): void {
    this.client = client;
    this.updateInterval = updateInterval;
    this.updateStatusBar();
  }

  private startHealthCheck(): void {
    if (this.healthCheckTimer) {
      return; // Already running
    }

    // Initial check
    this.checkHealth();

    // Periodic checks
    this.healthCheckTimer = setInterval(() => {
      this.checkHealth();
    }, this.updateInterval * 1000);
  }

  private stopHealthCheck(): void {
    if (this.healthCheckTimer) {
      clearInterval(this.healthCheckTimer);
      this.healthCheckTimer = null;
    }
  }

  private async checkHealth(): Promise<void> {
    try {
      const healthy = await this.client.checkHealth();
      this.isHealthy = healthy;
      this.updateStatusBar();
    } catch (err) {
      this.isHealthy = false;
      this.updateStatusBar();
    }
  }

  private updateStatusBar(): void {
    if (this.isHealthy) {
      this.statusBar.text = "$(circle-filled) SimpleModelServer";
      this.statusBar.backgroundColor = undefined;
      this.statusBar.tooltip = "SimpleModelServer: Connected";
    } else {
      this.statusBar.text = "$(circle-outline) SimpleModelServer";
      this.statusBar.backgroundColor = new vscode.ThemeColor(
        "statusBarItem.warningBackground"
      );
      this.statusBar.tooltip =
        "SimpleModelServer: Disconnected. Click to open settings.";
    }
  }
}

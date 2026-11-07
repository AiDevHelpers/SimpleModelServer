import * as vscode from "vscode";
import { RouterClient, ChatMessage } from "./routerClient";

export class CommandsManager {
  private client: RouterClient;
  private outputChannel: vscode.OutputChannel;

  constructor(client: RouterClient) {
    this.client = client;
    this.outputChannel = vscode.window.createOutputChannel("SimpleModelServer");
  }

  async explainCode(editor: vscode.TextEditor): Promise<void> {
    const selection = editor.selection;
    if (selection.isEmpty) {
      vscode.window.showErrorMessage("Please select code to explain");
      return;
    }

    const code = editor.document.getText(selection);
    const language = editor.document.languageId;

    await this.runCommand(
      `Explain the following ${language} code:\n\n${code}`,
      "Code Explanation"
    );
  }

  async generateTests(editor: vscode.TextEditor): Promise<void> {
    const selection = editor.selection;
    if (selection.isEmpty) {
      vscode.window.showErrorMessage("Please select code to generate tests for");
      return;
    }

    const code = editor.document.getText(selection);
    const language = editor.document.languageId;

    await this.runCommand(
      `Generate unit tests for the following ${language} code:\n\n${code}`,
      "Test Generation"
    );
  }

  async optimizeCode(editor: vscode.TextEditor): Promise<void> {
    const selection = editor.selection;
    if (selection.isEmpty) {
      vscode.window.showErrorMessage("Please select code to optimize");
      return;
    }

    const code = editor.document.getText(selection);
    const language = editor.document.languageId;

    await this.runCommand(
      `Provide optimization suggestions for the following ${language} code:\n\n${code}`,
      "Code Optimization"
    );
  }

  async openSettings(): Promise<void> {
    await vscode.commands.executeCommand(
      "workbench.action.openSettings",
      "simplemodelserver"
    );
  }

  async testConnection(): Promise<void> {
    try {
      const isHealthy = await this.client.checkHealth();

      if (isHealthy) {
        // Try to list models to verify full connectivity
        try {
          const models = await this.client.listModels();
          vscode.window.showInformationMessage(
            `SimpleModelServer connection OK. Found ${models.length} model(s).`
          );
        } catch (err) {
          vscode.window.showErrorMessage(
            `Connection test failed: Could not list models. ${err instanceof Error ? err.message : String(err)}`
          );
        }
      } else {
        vscode.window.showErrorMessage(
          "SimpleModelServer connection failed. Check that the server is running and settings are correct."
        );
      }
    } catch (err) {
      vscode.window.showErrorMessage(
        `Connection test failed: ${err instanceof Error ? err.message : String(err)}`
      );
    }
  }

  async rediscoverServer(): Promise<void> {
    try {
      await this.client.rediscover();
      vscode.window.showInformationMessage(
        "SimpleModelServer rediscovered successfully."
      );
    } catch (err) {
      vscode.window.showErrorMessage(
        `Failed to rediscover server: ${err instanceof Error ? err.message : String(err)}`
      );
    }
  }

  updateClient(client: RouterClient): void {
    this.client = client;
  }

  dispose(): void {
    this.outputChannel.dispose();
  }

  private async runCommand(prompt: string, title: string): Promise<void> {
    await vscode.window.withProgress(
      {
        location: vscode.ProgressLocation.Notification,
        title: `SimpleModelServer: ${title}`,
        cancellable: true,
      },
      async (progress, token) => {
        try {
          progress.report({ increment: 0 });

          this.outputChannel.clear();
          this.outputChannel.show(true);
          this.outputChannel.appendLine(`=== ${title} ===\n`);
          this.outputChannel.appendLine(`Prompt: ${prompt}\n`);
          this.outputChannel.appendLine("Response:");
          this.outputChannel.appendLine("");

          const messages: ChatMessage[] = [
            { role: "user", content: prompt },
          ];

          // Convert CancellationToken to AbortSignal
          const abortSignal = this.createAbortSignal(token);

          try {
            for await (const chunk of this.client.streamChatCompletion(
              messages,
              "default", // Use first available model
              abortSignal
            )) {
              if (token.isCancellationRequested) {
                this.outputChannel.appendLine("\n[Cancelled by user]");
                break;
              }

              this.outputChannel.append(chunk);
            }
          } catch {
            // Fallback to non-streaming
            console.warn("Streaming failed, using non-streaming");
            const response = await this.client.getChatCompletion(
              messages,
              "default",
              abortSignal
            );
            this.outputChannel.append(response);
          }

          this.outputChannel.appendLine("\n\n[Command completed]");
        } catch (err) {
          this.outputChannel.appendLine(
            `\n[Error]: ${err instanceof Error ? err.message : String(err)}`
          );
          vscode.window.showErrorMessage(
            `Command failed: ${err instanceof Error ? err.message : String(err)}`
          );
        }
      }
    );
  }

  private createAbortSignal(token: vscode.CancellationToken): AbortSignal {
    const controller = new AbortController();

    if (token.isCancellationRequested) {
      controller.abort();
    }

    token.onCancellationRequested(() => {
      controller.abort();
    });

    return controller.signal;
  }
}

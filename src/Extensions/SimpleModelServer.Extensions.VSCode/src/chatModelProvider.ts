import * as vscode from "vscode";
import { RouterClient, Model, ChatMessage } from "./routerClient";

/**
 * Implements the VS Code Language Model Chat Provider interface to register
 * SimpleModelServer's models with GitHub Copilot Chat
 */
export class SimpleModelServerChatProvider
  implements vscode.LanguageModelChatProvider
{
  private client: RouterClient;
  private modelCache: vscode.LanguageModelChatInformation[] | null = null;
  private modelCacheTime: number = 0;
  private readonly cacheDurationMs = 5 * 60 * 1000; // 5 minutes

  constructor(client: RouterClient) {
    this.client = client;
  }

  /**
   * Provide information about available language models from SimpleModelServer
   * This is called by Copilot Chat to populate the model picker
   */
  async provideLanguageModelChatInformation(
    _options: vscode.PrepareLanguageModelChatModelOptions,
    token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelChatInformation[]> {
    try {
      return await this.getModelsWithCache(token);
    } catch (err) {
      console.error("Failed to provide model information:", err);
      throw new Error(
        `Failed to list models: ${err instanceof Error ? err.message : String(err)}`
      );
    }
  }

  /**
   * Provide chat responses for the selected model
   * This is called when a user sends a message in Copilot Chat
   */
  async provideLanguageModelChatResponse(
    model: vscode.LanguageModelChatInformation,
    messages: readonly vscode.LanguageModelChatRequestMessage[],
    _options: vscode.ProvideLanguageModelChatResponseOptions,
    progress: vscode.Progress<vscode.LanguageModelResponsePart>,
    token: vscode.CancellationToken
  ): Promise<void> {
    try {
      // Convert VS Code chat messages to RouterClient format
      const chatMessages: ChatMessage[] = messages.map((msg) => ({
        role: this.mapMessageRole(msg.role),
        content: this.extractTextContent(msg.content),
      }));

      // Create abort signal from cancellation token
      const abortSignal = this.createAbortSignal(token);

      // Stream the response
      try {
        for await (const chunk of this.client.streamChatCompletion(
          chatMessages,
          model.id,
          abortSignal
        )) {
          if (token.isCancellationRequested) {
            break;
          }

          // Report progress to Copilot Chat UI
          const textPart = new vscode.LanguageModelTextPart(chunk);
          progress.report(textPart);
        }
      } catch (streamErr) {
        // If streaming fails, try non-streaming fallback
        if (
          streamErr instanceof Error &&
          streamErr.message.includes("Stream failed")
        ) {
          console.warn("Streaming failed, falling back to non-streaming");
          const response = await this.client.getChatCompletion(
            chatMessages,
            model.id,
            abortSignal
          );

          if (response) {
            const textPart = new vscode.LanguageModelTextPart(response);
            progress.report(textPart);
          }
        } else {
          throw streamErr;
        }
      }
    } catch (err) {
      const errorMsg = `Chat request failed: ${err instanceof Error ? err.message : String(err)}`;
      console.error(errorMsg);
      throw new Error(errorMsg);
    }
  }

  /**
   * Provide token count for the given text
   */
  async provideTokenCount(
    _model: vscode.LanguageModelChatInformation,
    text: string | vscode.LanguageModelChatRequestMessage,
    _token: vscode.CancellationToken
  ): Promise<number> {
    if (typeof text === "string") {
      return this.client.estimateTokenCount(text);
    } else {
      // For message objects, count all content
      const textContent = this.extractTextContent(text.content);
      return this.client.estimateTokenCount(textContent);
    }
  }

  /**
   * Update the router client connection
   */
  updateClient(client: RouterClient): void {
    this.client = client;
    this.clearModelCache();
  }

  private mapMessageRole(
    role: vscode.LanguageModelChatMessageRole
  ): "user" | "assistant" {
    if (role === vscode.LanguageModelChatMessageRole.User) {
      return "user";
    }
    // Default to assistant for any other role
    return "assistant";
  }

  private extractTextContent(
    content: ReadonlyArray<vscode.LanguageModelInputPart | unknown>
  ): string {
    const textParts: string[] = [];

    for (const part of content) {
      if (part instanceof vscode.LanguageModelTextPart) {
        textParts.push(part.value);
      } else if (typeof part === "string") {
        textParts.push(part);
      }
    }

    return textParts.join("\n");
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

  private async getModelsWithCache(
    token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelChatInformation[]> {
    const now = Date.now();

    if (this.modelCache && now - this.modelCacheTime < this.cacheDurationMs) {
      return this.modelCache;
    }

    const models = await this.client.listModels(
      this.createAbortSignal(token)
    );
    const chatInfos = models.map((model) => ({
      id: model.id,
      name: model.id,
      family: "simplemodelserver",
      version: "1.0",
      maxInputTokens: 8000, // Default estimate; could be configurable
      maxOutputTokens: 8000, // Default estimate; could be configurable
      capabilities: {
        toolCalling: false,
        imageInput: false,
      },
    }));

    this.modelCache = chatInfos;
    this.modelCacheTime = now;

    return chatInfos;
  }

  private clearModelCache(): void {
    this.modelCache = null;
    this.modelCacheTime = 0;
  }
}

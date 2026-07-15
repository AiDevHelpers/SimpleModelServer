import * as vscode from "vscode";
import { encode } from "gpt-tokenizer";

export interface Model {
  id: string;
  object: string;
  created: number;
  owned_by: string;
}

export interface ModelsResponse {
  object: string;
  data: Model[];
}

export interface ChatMessage {
  role: "system" | "user" | "assistant";
  content: string;
}

export interface ChatCompletionRequest {
  model: string;
  messages: ChatMessage[];
  stream?: boolean;
  temperature?: number;
  top_p?: number;
  max_tokens?: number;
}

export interface ChatDelta {
  content?: string;
}

export interface ChatChoice {
  delta?: ChatDelta;
  message?: { role: string; content: string };
  finish_reason?: string;
}

export interface ChatCompletionResponse {
  id: string;
  object: string;
  choices: ChatChoice[];
  usage?: {
    prompt_tokens: number;
    completion_tokens: number;
    total_tokens: number;
  };
}

export interface HealthResponse {
  status: string;
}

// Server serializes with default (PascalCase) property naming — no camelCase policy configured.
export interface SystemInfoResponse {
  Configuration: {
    Global: {
      Provider?: string;
    };
  };
}

export class RouterClient {
  private baseUrl: string;
  private apiKey: string;

  constructor(host: string, port: number, apiKey: string = "") {
    this.baseUrl = `http://${host}:${port}`;
    this.apiKey = apiKey;
  }

  /**
   * Check router health status
   */
  async checkHealth(signal?: AbortSignal): Promise<boolean> {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 3000);

      try {
        const response = await fetch(`${this.baseUrl}/health`, {
          method: "GET",
          signal: signal || controller.signal,
        });
        return response.ok;
      } finally {
        clearTimeout(timeoutId);
      }
    } catch (err) {
      return false;
    }
  }

  /**
   * Get the active inference provider ("ollama" or "lmstudio"), for display purposes only —
   * provider selection is entirely server-side (config.json), not something this extension
   * configures. Returns undefined if the server is unreachable or the field is absent (older
   * server builds predate this field).
   */
  async getProvider(signal?: AbortSignal): Promise<string | undefined> {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 3000);

      try {
        const response = await fetch(`${this.baseUrl}/api/system/info`, {
          method: "GET",
          signal: signal || controller.signal,
        });
        if (!response.ok) {
          return undefined;
        }
        const info = (await response.json()) as SystemInfoResponse;
        return info.Configuration?.Global?.Provider;
      } finally {
        clearTimeout(timeoutId);
      }
    } catch (err) {
      return undefined;
    }
  }

  /**
   * Get available models from the router
   */
  async listModels(signal?: AbortSignal): Promise<Model[]> {
    try {
      const response = await fetch(`${this.baseUrl}/v1/models`, {
        method: "GET",
        headers: this.buildHeaders(),
        signal,
      });

      if (!response.ok) {
        throw new Error(`Failed to list models: ${response.statusText}`);
      }

      const data = await response.json() as ModelsResponse;
      return data.data || [];
    } catch (err) {
      throw new Error(`Failed to list models: ${err instanceof Error ? err.message : String(err)}`);
    }
  }

  /**
   * Stream chat completion from the router
   * Parses SSE (Server-Sent Events) format responses
   */
  async *streamChatCompletion(
    messages: ChatMessage[],
    model: string,
    signal?: AbortSignal,
    temperature?: number,
    topP?: number,
    maxTokens?: number
  ): AsyncGenerator<string> {
    const request: ChatCompletionRequest = {
      model,
      messages,
      stream: true,
      temperature,
      top_p: topP,
      max_tokens: maxTokens,
    };

    try {
      const response = await fetch(`${this.baseUrl}/v1/chat/completions`, {
        method: "POST",
        headers: {
          ...this.buildHeaders(),
          "Content-Type": "application/json",
        },
        body: JSON.stringify(request),
        signal,
      });

      if (!response.ok) {
        throw new Error(`Failed to get completion: ${response.statusText}`);
      }

      if (!response.body) {
        throw new Error("No response body");
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";

      try {
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });

          // Process complete lines
          const lines = buffer.split("\n");
          buffer = lines[lines.length - 1];

          for (let i = 0; i < lines.length - 1; i++) {
            const line = lines[i].trim();

            if (line === "" || line === ":") {
              continue; // Keep-alive or empty line
            }

            if (line === "data: [DONE]") {
              break; // End of stream
            }

            if (line.startsWith("data: ")) {
              const jsonStr = line.slice(6);
              try {
                const data = JSON.parse(jsonStr) as ChatCompletionResponse;
                const content = data.choices?.[0]?.delta?.content;
                if (content) {
                  yield content;
                }
              } catch (parseErr) {
                console.error("Failed to parse SSE chunk:", parseErr);
              }
            }
          }
        }

        // Process any remaining buffer
        if (buffer && buffer.startsWith("data: ")) {
          const jsonStr = buffer.slice(6);
          if (jsonStr !== "[DONE]") {
            try {
              const data = JSON.parse(jsonStr) as ChatCompletionResponse;
              const content = data.choices?.[0]?.delta?.content;
              if (content) {
                yield content;
              }
            } catch (parseErr) {
              console.error("Failed to parse final SSE chunk:", parseErr);
            }
          }
        }
      } finally {
        reader.releaseLock();
      }
    } catch (err) {
      throw new Error(
        `Stream failed: ${err instanceof Error ? err.message : String(err)}`
      );
    }
  }

  /**
   * Get non-streaming chat completion (waits for full response)
   */
  async getChatCompletion(
    messages: ChatMessage[],
    model: string,
    signal?: AbortSignal,
    temperature?: number,
    topP?: number,
    maxTokens?: number
  ): Promise<string> {
    const request: ChatCompletionRequest = {
      model,
      messages,
      stream: false,
      temperature,
      top_p: topP,
      max_tokens: maxTokens,
    };

    try {
      const response = await fetch(`${this.baseUrl}/v1/chat/completions`, {
        method: "POST",
        headers: {
          ...this.buildHeaders(),
          "Content-Type": "application/json",
        },
        body: JSON.stringify(request),
        signal,
      });

      if (!response.ok) {
        throw new Error(`Failed to get completion: ${response.statusText}`);
      }

      const data = await response.json() as ChatCompletionResponse;
      return data.choices?.[0]?.message?.content || "";
    } catch (err) {
      throw new Error(
        `Failed to get completion: ${err instanceof Error ? err.message : String(err)}`
      );
    }
  }

  /**
   * Estimate token count using the cl100k_base tokenizer. The router can serve arbitrary
   * local models whose real vocab differs from OpenAI's, so this remains an approximation,
   * but it tracks real subword boundaries and is far closer than a chars/4 guess.
   */
  estimateTokenCount(text: string): number {
    return encode(text).length;
  }

  private buildHeaders(): Record<string, string> {
    const headers: Record<string, string> = {
      "User-Agent": "SimpleModelServer-VSCode/1.0",
    };

    if (this.apiKey) {
      headers["Authorization"] = `Bearer ${this.apiKey}`;
    }

    return headers;
  }

  /**
   * Update connection parameters
   */
  setConnection(host: string, port: number, apiKey: string) {
    this.baseUrl = `http://${host}:${port}`;
    this.apiKey = apiKey;
  }
}

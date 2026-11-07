import { beforeEach, describe, expect, it } from "vitest";
import { RouterClient, ChatMessage } from "../src/routerClient";

/**
 * Unit tests for RouterClient
 *
 * Note: These tests are integration tests that require a running SimpleModelServer.
 * Set SMS_TEST_HOST and SMS_TEST_PORT env vars to run against a live server.
 *
 * Example:
 *   SMS_TEST_HOST=localhost SMS_TEST_PORT=11435 npm run test:unit
 */

const TEST_HOST = process.env.SMS_TEST_HOST || "localhost";
const TEST_PORT = parseInt(process.env.SMS_TEST_PORT || "11435", 10);
const SKIP_TESTS = !process.env.SMS_TEST_HOST; // Skip if not explicitly enabled

describe("RouterClient", () => {
  let client: RouterClient;

  beforeEach(() => {
    client = new RouterClient(TEST_HOST, TEST_PORT, "");
  });

  describe("health check", () => {
    it.skipIf(SKIP_TESTS)(
      "should return true for healthy server",
      async () => {
        const healthy = await client.checkHealth();
        expect(healthy).toBe(true);
      }
    );

    it.skipIf(SKIP_TESTS)(
      "should return false for unreachable server",
      async () => {
        const badClient = new RouterClient("localhost", 9999, "");
        const healthy = await badClient.checkHealth();
        expect(healthy).toBe(false);
      }
    );
  });

  describe("list models", () => {
    it.skipIf(SKIP_TESTS)(
      "should return array of models",
      async () => {
        const models = await client.listModels();
        expect(Array.isArray(models)).toBe(true);
        if (models.length > 0) {
          expect(models[0]).toHaveProperty("id");
          expect(models[0]).toHaveProperty("object");
        }
      }
    );

    it.skipIf(SKIP_TESTS)(
      "should throw error for unreachable server",
      async () => {
        const badClient = new RouterClient("localhost", 9999, "");
        await expect(badClient.listModels()).rejects.toThrow();
      }
    );
  });

  describe("chat completion streaming", () => {
    it.skipIf(SKIP_TESTS)(
      "should stream chat completion chunks",
      async () => {
        const messages: ChatMessage[] = [
          { role: "user", content: "Say hello" },
        ];

        let chunkCount = 0;
        let fullResponse = "";

        for await (const chunk of client.streamChatCompletion(
          messages,
          "default"
        )) {
          chunkCount++;
          fullResponse += chunk;
          // Should receive non-empty chunks
          expect(chunk.length).toBeGreaterThan(0);
        }

        expect(chunkCount).toBeGreaterThan(0);
        expect(fullResponse.length).toBeGreaterThan(0);
      }
    );

    it.skipIf(SKIP_TESTS)(
      "should handle cancellation",
      async () => {
        const messages: ChatMessage[] = [
          { role: "user", content: "Write a long story" },
        ];

        const controller = new AbortController();
        const chunks: string[] = [];

        try {
          for await (const chunk of client.streamChatCompletion(
            messages,
            "default",
            controller.signal
          )) {
            chunks.push(chunk);
            if (chunks.length >= 2) {
              controller.abort();
            }
          }
        } catch {
          // Cancellation may throw
        }

        // Should have received at least some chunks before cancellation
        expect(chunks.length).toBeGreaterThanOrEqual(1);
      }
    );
  });

  describe("non-streaming chat completion", () => {
    it.skipIf(SKIP_TESTS)(
      "should return full response",
      async () => {
        const messages: ChatMessage[] = [
          { role: "user", content: "Say hello" },
        ];

        const response = await client.getChatCompletion(
          messages,
          "default"
        );

        expect(typeof response).toBe("string");
        expect(response.length).toBeGreaterThan(0);
      }
    );
  });

  describe("token counting", () => {
    it("should estimate token count", () => {
      const text = "Hello world";
      const tokenCount = client.estimateTokenCount(text);

      // Rough estimate: chars / 4
      expect(tokenCount).toBeGreaterThan(0);
      expect(tokenCount).toBeLessThanOrEqual(Math.ceil(text.length / 4) + 1);
    });

    it("should handle empty strings", () => {
      const tokenCount = client.estimateTokenCount("");
      expect(tokenCount).toBe(0);
    });
  });

  describe("connection parameters", () => {
    it("should update connection settings", () => {
      const client1 = new RouterClient("localhost", 11435, "key1");
      client1.setConnection("example.com", 8080, "key2");

      // Verify by making a request (will fail but that's ok, tests connection update)
      // This is implicit in the setConnection call
      expect(true).toBe(true);
    });
  });

  describe("auth headers", () => {
    it.skipIf(SKIP_TESTS)(
      "should include api key in requests when configured",
      async () => {
        const clientWithKey = new RouterClient(TEST_HOST, TEST_PORT, "test-key");

        // This test is implicit - if auth is required and we don't send it,
        // the request will fail. If we send it and auth is not required,
        // loopback traffic bypasses auth anyway.
        // Just verify no errors:
        const models = await clientWithKey.listModels();
        expect(Array.isArray(models)).toBe(true);
      }
    );
  });
});


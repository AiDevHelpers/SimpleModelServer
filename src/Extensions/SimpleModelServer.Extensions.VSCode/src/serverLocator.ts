import * as http from "http";
import * as vscode from "vscode";
import Bonjour from "bonjour-service";

export type ConnectionMode = "local" | "manual" | "mdns";

export interface ServerConnectionSettings {
  mode: ConnectionMode;
  manualHost?: string;
  manualPort?: number;
  apiKey?: string;
  rememberedMdnsInstanceName?: string;
}

export interface ResolvedServerConnection {
  baseUrl: string;
  isLoopback: boolean;
  apiKey?: string;
  resolvedVia: ConnectionMode;
}

const MDNS_SERVICE_TYPE = "_draftmodel._tcp";
const DEFAULT_PORT = 11435;
const LOCAL_PROBE_TIMEOUT_MS = 2000;
const MDNS_DISCOVERY_TIMEOUT_MS = 3000;

interface DiscoveredService {
  instanceName: string;
  hostname: string;
  port: number;
}

/**
 * Locates SimpleModelServer instances using the configured connection mode.
 * Supports Local (http://localhost), Manual (explicit host/port), and mDNS discovery modes.
 */
export class ServerLocator {
  private cachedResult: ResolvedServerConnection | null = null;

  constructor(
    private context: vscode.ExtensionContext,
    private onError?: (err: Error) => void
  ) {}

  /**
   * Asynchronously resolves the server connection, using cached result if available.
   */
  async resolve(): Promise<ResolvedServerConnection> {
    if (this.cachedResult) {
      return this.cachedResult;
    }
    return (this.cachedResult = await this.resolveInternal());
  }

  /**
   * Asynchronously rediscovers the server connection, clearing any cached result.
   */
  async rediscover(): Promise<ResolvedServerConnection> {
    this.cachedResult = null;
    return this.resolve();
  }

  private async resolveInternal(): Promise<ResolvedServerConnection> {
    const settings = this.loadSettings();

    switch (settings.mode) {
      case "local":
        return await this.resolveLocal(settings);
      case "manual":
        return this.resolveManual(settings);
      case "mdns":
        return await this.resolveMdns(settings);
      default:
        const _exhaustive: never = settings.mode;
        throw new Error(`Unknown connection mode: ${_exhaustive}`);
    }
  }

  private async resolveLocal(
    settings: ServerConnectionSettings
  ): Promise<ResolvedServerConnection> {
    const port = settings.manualPort && settings.manualPort > 0
      ? settings.manualPort
      : DEFAULT_PORT;
    const url = `http://localhost:${port}/health`;

    try {
      const isHealthy = await this.probeHealth(url, LOCAL_PROBE_TIMEOUT_MS);
      if (isHealthy) {
        return {
          baseUrl: `http://localhost:${port}`,
          isLoopback: true,
          apiKey: undefined,
          resolvedVia: "local",
        };
      }
    } catch (err) {
      this.onError?.(err instanceof Error ? err : new Error(String(err)));
    }

    // Fallback to mDNS if local probe fails
    return this.resolveMdns(settings);
  }

  private resolveManual(
    settings: ServerConnectionSettings
  ): ResolvedServerConnection {
    if (!settings.manualHost || settings.manualHost.trim() === "") {
      throw new Error(
        "Manual connection mode requires manualHost to be set."
      );
    }

    const port =
      settings.manualPort && settings.manualPort > 0
        ? settings.manualPort
        : DEFAULT_PORT;
    const baseUrl = `http://${settings.manualHost}:${port}`;
    const isLoopback = this.isLoopbackAddress(settings.manualHost);

    return {
      baseUrl,
      isLoopback,
      apiKey: isLoopback ? undefined : settings.apiKey,
      resolvedVia: "manual",
    };
  }

  private async resolveMdns(
    settings: ServerConnectionSettings
  ): Promise<ResolvedServerConnection> {
    const services = await this.browseMdns(MDNS_DISCOVERY_TIMEOUT_MS);

    if (services.length === 0) {
      throw new Error(
        "No SimpleModelServer instance found via mDNS within the timeout."
      );
    }

    // Prefer remembered instance, otherwise use first responder
    const selectedService =
      services.find(
        (s) => s.instanceName === settings.rememberedMdnsInstanceName
      ) || services[0];

    return {
      baseUrl: `http://${selectedService.hostname}:${selectedService.port}`,
      isLoopback: false,
      apiKey: settings.apiKey,
      resolvedVia: "mdns",
    };
  }

  private async probeHealth(url: string, timeoutMs: number): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      const timeout = setTimeout(() => resolve(false), timeoutMs);

      try {
        const req = http.get(url, { timeout: timeoutMs }, (res) => {
          clearTimeout(timeout);
          resolve(res.statusCode ? res.statusCode >= 200 && res.statusCode < 300 : false);
          res.resume(); // Consume response to free socket
        });

        req.on("error", () => {
          clearTimeout(timeout);
          resolve(false);
        });
      } catch (err) {
        clearTimeout(timeout);
        resolve(false);
      }
    });
  }

  private async browseMdns(timeoutMs: number): Promise<DiscoveredService[]> {
    return new Promise((resolve) => {
      const services: DiscoveredService[] = [];
      const instanceSet = new Set<string>();
      let bonjourInstance: InstanceType<typeof Bonjour> | null = null;
      let resolved = false;

      const cleanup = () => {
        if (bonjourInstance) {
          try {
            bonjourInstance.destroy();
          } catch (err) {
            // Ignore cleanup errors
          }
        }
      };

      const timeout = setTimeout(() => {
        if (!resolved) {
          resolved = true;
          cleanup();
          resolve(services);
        }
      }, timeoutMs);

      try {
        bonjourInstance = new Bonjour();

        bonjourInstance.find(
          { type: "draftmodel", protocol: "tcp" },
          (service) => {
            if (service.addresses && service.port && service.name) {
              // Pick the first IPv4 address, fall back to hostname
              const hostname =
                service.addresses.find((addr) => !addr.includes(":")) ||
                service.addresses[0] ||
                service.host ||
                service.name;

              const discovered: DiscoveredService = {
                instanceName: service.name,
                hostname,
                port: service.port,
              };

              // Use Set to avoid duplicates (same instance might respond multiple times)
              const key = `${discovered.hostname}:${discovered.port}`;
              if (!instanceSet.has(key)) {
                instanceSet.add(key);
                services.push(discovered);
              }
            }
          }
        );
      } catch (err) {
        if (!resolved) {
          resolved = true;
          clearTimeout(timeout);
          cleanup();
          resolve(services);
        }
      }
    });
  }

  private isLoopbackAddress(host: string): boolean {
    return (
      host.toLowerCase() === "localhost" ||
      host === "127.0.0.1" ||
      host === "::1"
    );
  }

  private loadSettings(): ServerConnectionSettings {
    const globalState = this.context.globalState;
    const config = vscode.workspace.getConfiguration("simplemodelserver");

    // Load user-facing configuration
    const mode = (config.get<string>("connectionMode") || "local") as ConnectionMode;
    const manualHost = config.get<string>("host");
    const manualPort = config.get<number>("port");
    const apiKey = config.get<string>("apiKey");

    // Load runtime state (remembered mDNS instance)
    const rememberedMdnsInstanceName = globalState.get<string>(
      "rememberedMdnsInstanceName"
    );

    return {
      mode,
      manualHost,
      manualPort,
      apiKey,
      rememberedMdnsInstanceName,
    };
  }

  /**
   * Remember the given mDNS instance name for future discovery.
   */
  async rememberMdnsInstance(instanceName: string): Promise<void> {
    await this.context.globalState.update(
      "rememberedMdnsInstanceName",
      instanceName
    );
  }

  /**
   * Clear the remembered mDNS instance name.
   */
  async forgetMdnsInstance(): Promise<void> {
    await this.context.globalState.update("rememberedMdnsInstanceName", undefined);
  }
}

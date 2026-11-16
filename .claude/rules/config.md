---
paths:
  - "src/Config/**"
  - "src/Providers/**"
  - "src/Server/**/Configuration/**"
---

# Config and models

- `AppConfig.Load` accepts two `models` shapes:
  - Structured (what `DefaultConfig` writes): `models.configurations[]`, each with `id`, `nickname`, `isDefault`, `families[]` and `embed`. A family has `family`, `variant`, `keepAliveMinutes`, `draft` and `main`. Each of `draft` and `main` has `size`, `numCtx`, `stop`, `temperature`, `topP`.
  - Flat (legacy): role-keyed `draft`, `main`, `embed` objects with `name`, `base`, or `family`/`variant`/`size`, plus `num_ctx`, `keep_alive`, `stop`, `temperature`, `top_p`.
- Configuration selection: `global.configurationId`, then `isDefault`, then the first entry.
- `base` is the Ollama registry reference: `{family}-{variant}:{size}`, e.g. `qwen2.5-coder:0.5b`. It never includes the chat suffix.
- The local name is `{family}-{variant}-{size}{chatSuffix}`, with `chatSuffix` defaulting to `-chat`.
- `Custom` family has no registry name, so it needs explicit `name` and `base`. Otherwise it is skipped.
- Family enum to Ollama name: `AppConfig.ToOllamaFamilyName`.
- `EnsureModelAsync` creates a derived model from its base only when the local name is absent from `ollama list`. An existing derived model keeps its old parameters; run `ollama rm` to refresh. A base equal to the model's own name is a plain pull, not a create.
- System.Text.Json writes enums as integers. Read both the ordinal and the name (see `ParseProviderType`, `ParseModelFamily`).
- Admin-updatable settings live only in `GlobalSettingRegistry`. Add new ones there. `configurationId` is validated in `AdminEndpointHandler`, which rejects unknown ids.
- Never have the installer write `SimpleModelServer.config.json`. `DefaultConfig.EnsureConfig` skips when the file exists, so a partial file leaves the models section missing.

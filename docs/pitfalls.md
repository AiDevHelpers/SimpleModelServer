# Pitfalls

Resolved failures, each with symptom, real cause, and fix. Check here before debugging the same symptom.

### Models fail to pull: "pull model manifest: file does not exist"
- Symptom: server exits at startup with `ollama pull <custom-name>` failing.
- Cause: the fresh-install config used the structured `models.configurations` schema, but the runtime parsed only the flat schema. Model sources were empty, so custom names were pulled literally.
- Fix: `AppConfig` parses the structured schema (commit `beb2dcc`).

### Derived model name had `-chat` in the base
- Symptom: `qwen2.5-coder-chat:0.5b` not found in the registry.
- Cause: the chat suffix was added to the registry base as well as the local name.
- Fix: the base is `{family}-{variant}:{size}`. The suffix only names the local model.

### Server crashes before models are read
- Symptom: startup fails in `ParseGlobal`, in `ParseProvider`, with "Number cannot be converted to String".
- Cause: System.Text.Json writes enums as numbers by default, and `provider.type` was read as a string.
- Fix: read enums as either ordinal or name (`ParseProviderType`).

### Installer produced a config with no models
- Symptom: after install, the server has no models and `ollama pull` fails.
- Cause: the installer wrote a partial `SimpleModelServer.config.json`. `DefaultConfig.EnsureConfig` skips when the file exists.
- Fix: the installer no longer writes the config.

### Embedding model "created" from itself
- Symptom: `nomic-embed-text` fails in `/api/create`.
- Cause: base equal to the model's own name was treated as a derived model.
- Fix: `EnsureModelAsync` pulls when base equals the name.

### Changed model parameters not applied
- Symptom: temperature or top-p changes in config have no effect.
- Cause: `EnsureModelAsync` skips models that already exist in `ollama list`.
- Fix: `ollama rm` the derived model, then restart the server.

### NU1605: package downgrade detected
- Symptom: restore fails with "Detected package downgrade: Microsoft.CodeAnalysis from 5.9.0 to 5.6.0".
- Cause: one project pinned a different version of a package that a referenced project pins at another version.
- Fix: align the versions across referencing projects.

### CI silently skipped tests
- Symptom: `dotnet test` on the solution runs fewer test projects than exist.
- Cause: four test projects were not listed in `SimpleModelServer.slnx`.
- Fix: list every test project in the solution.

### `dotnet test` on Server.Tests: "Testing with VSTest target is no longer supported"
- Symptom: the test project refuses to run.
- Cause: the .NET 11 preview SDK is the default on this machine. The test project's VSTest target isn't supported there.
- Fix: run with the .NET 10 SDK.

### Inno Setup compile errors
- `VersionInfoProductVersion` is invalid: it must be numeric, so use `MyAppNumericVersion`.
- `LoadStringFromFile` needs an `AnsiString` buffer, not a `String`.
- A pipe passed to `sc.exe` does nothing. Wrap it in `cmd /c`.

### PowerShell `$args` in CI
- Symptom: a splatted array of arguments is empty or wrong.
- Cause: `$args` is an automatic variable.
- Fix: name the variable `$buildArgs`.

### Router unloads the model it is checking (warming_up forever)
- Symptom: `/api/generate` on the router keeps returning `202`/`warming_up`. `/api/ps` shows no draft or main model, even with `keep_alive` at 30m.
- Cause: `OllamaProcessManager.IsModelResponsiveAsync` sent `keep_alive: 0`. Ollama unloads a model right after a `keep_alive: 0` request, so each probe evicted the model it was checking, and the warm-up reloaded it in a loop.
- Fix: the probe omits `keep_alive`. Only the unload path (`UnloadModelAsync`) sends `keep_alive: 0`.

### Router's warm-up check cancels a cold model load
- Symptom: `warming_up` persists even after the probe fix. The main model never stays loaded in Ollama.
- Cause: the warm-up gate probed with a 2s timeout, but a cold load of the 3B main model takes about 2.7s. The timeout cancelled the probe mid-load.
- Fix: `ModelResponsiveWarmupCheckTimeout` is 10s.

### Router's main reply is empty, falls back to the draft's first token
- Symptom: a chat reply is just the draft's first token (for example "The"), or `warming_up` follows a good reply.
- Cause: the token poll cancelled each `ReadLineAsync` after 30 ms. Cancelling a read on an HTTP response stream tears down the connection, so the next read threw `ObjectDisposedException`. `ReadTokenAsync` reports that as end-of-stream, so the main text came back empty.
- Fix: keep one read in flight per stream and wait on it for the poll timeout instead of cancelling it. The fault is logged as a warning.

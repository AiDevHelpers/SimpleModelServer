# Visual Studio Extension Settings Panel

The SimpleModelServer Visual Studio extension provides a comprehensive settings panel for configuring server connection, agent behavior, and—most importantly—editing agent prompts at runtime.

## Accessing Settings

**In Visual Studio:**
1. Tools → SimpleModelServer Settings (or Options → SimpleModelServer)
2. The Settings Panel opens in the Visual Studio options dialog

## Settings Sections

### Server Connection

Configure where SimpleModelServer is running:

- **Server Host** — Hostname or IP address (default: `localhost`)
- **Server Port** — Port number (default: `11435` for router, `1234` for LM Studio)
- **API Key (Optional)** — Bearer token for remote access (leave blank for localhost)
- **Test Connection** — Button to verify connection to the server

**Example:**
```
Host: localhost
Port: 11435
API Key: [leave blank]
```

Click "Test Connection" to confirm connectivity before adjusting agent prompts.

### Agent Configuration

Configure default agent behavior:

- **Default Agent** — Dropdown to select the default agent type (Ask, Code, Debug, Planning)

### Enabled Features

Quick toggles for optional features:

- **Explain Code** — Enable code explanation feature
- **Generate Tests** — Enable test generation
- **Optimize Code** — Enable code optimization suggestions
- **Remember Last Chat** — Persist chat history between sessions

### Agent Prompts

Edit agent system prompts and validate changes with the golden set.

#### Workflow

1. **Select an Agent** — Dropdown shows: Code, Debug, Planning
   - Each agent has a different system prompt
   - Prompt is loaded automatically when you select an agent

2. **Edit the Prompt** — Multi-line text box with the agent's current system prompt
   - Make your changes
   - Use clear, specific instructions for best results
   - Example: "You are a C# expert. Provide idiomatic .NET solutions with error handling."

3. **Save Prompt** — Click "Save Prompt" button
   - Changes are sent to `/admin/config` endpoint
   - If successful, the panel reloads available agents
   - A status message confirms the change

4. **Validate with Golden Set (Optional)** — If the server suggests running the golden set:
   - A dialog appears: "Run the golden set now to validate the new prompt?"
   - **Yes** → Launches `SimpleModelServer.Evals --mode router` and shows results
   - **No** → Skips validation (not recommended for production)

#### Golden Set Results

If you click "Yes" to run the golden set:

- **Passed** — ✅ Golden set completed successfully
  - The new prompt maintains routing accuracy
  - Safe to use in production
  - Status shows: "Golden set passed."

- **Failed** — ⚠️ Golden set had failures
  - Review the output for which tests failed
  - Consider revising the prompt and re-running
  - Status shows: "Golden set finished with exit code [code]. See output below."
  - A popup displays the full eval output

#### Example: Optimizing the Code Agent

**Original Prompt:**
```
You are a code assistant.
```

**Improved Prompt:**
```
You are an expert C# developer with deep knowledge of .NET 8+ best practices.

When generating code:
- Use modern C# features (records, nullable reference types, pattern matching)
- Follow Microsoft's C# coding guidelines
- Include error handling for edge cases
- Provide XML doc comments for public members
- Prioritize readability and maintainability over clever code

When analyzing code:
- Point out potential null reference errors
- Suggest LINQ alternatives where applicable
- Recommend async/await patterns for I/O operations
```

**Then:**
1. Click "Save Prompt"
2. When prompted, click "Yes" to run the golden set
3. If the golden set passes, the new prompt is validated and ready for production

### Status Messages

The panel displays status messages to guide your workflow:

- **"Testing connection..."** → Verifies server connectivity
- **"Connection successful!"** → ✅ Server is reachable
- **"Connection failed."** → ❌ Check host/port settings
- **"Saving prompt for 'code'..."** → Sending change to server
- **"Agent 'code' prompt updated and reloaded."** → ✅ Change applied
- **"Running golden set..."** → Validating the new prompt
- **"Golden set passed."** → ✅ Prompt is production-ready
- **"Golden set finished with exit code [N]..."** → ⚠️ Review output

## Prerequisites

### For Prompt Editing

1. **SimpleModelServer must be running** — Check "Test Connection" first
2. **Ollama or LM Studio must be available** — For the golden set eval
3. **SimpleModelServer.Evals CLI must be accessible** — For `--mode router` (optional, but recommended)

If `SimpleModelServer.Evals` is not on your PATH:
- In Settings, set "Evals Executable Path" to the full path
- Default: `SimpleModelServer.Evals.exe` (assumes it's on PATH or in the same directory as SimpleModelServer.Server.exe)

### For Golden Set Validation

- **Required models must be pulled** — draft, main, and embed models (same as SimpleModelServer's defaults)
- **Ollama/LM Studio must be responsive** — Golden set runs real inference, not mocked
- **Time** — First run may take 60–90 seconds (cold model load), subsequent runs are faster

## Troubleshooting

### "Could not launch 'SimpleModelServer.Evals.exe'"

**Cause:** The Evals executable is not on PATH or not found at the configured path.

**Fix:**
1. Ensure `SimpleModelServer.Evals.exe` is in the directory where SimpleModelServer.Server.exe is installed
2. Or, in Settings, set "Evals Executable Path" to the full path, e.g.:
   ```
   C:\Program Files\SimpleModelServer\SimpleModelServer.Evals.exe
   ```

### "LM Studio is not running on port 1234"

**Cause:** LM Studio isn't accessible at the configured server address.

**Fix:**
1. Ensure LM Studio is running with its local server enabled
2. Verify "Server Host" and "Server Port" match your LM Studio configuration
3. Click "Test Connection" to confirm

### "Golden set finished with exit code [N]"

**Cause:** One or more tests failed with the new prompt.

**Fix:**
1. Review the popup output — it shows which tests failed
2. Revise the prompt to be clearer or more specific
3. Save the updated prompt and run the golden set again
4. Iterate until all tests pass

## Best Practices

### Writing Effective Agent Prompts

1. **Be specific about the task** — "C# expert" > "code assistant"
2. **Mention constraints** — .NET version, coding standards, performance concerns
3. **Show examples if helpful** — "Prefer LINQ over foreach loops"
4. **Explain any special behavior** — "Always add try-catch blocks for I/O operations"
5. **Keep it concise** — Long prompts can degrade model performance

### Validation Workflow

1. **Always run the golden set** when changing prompts for production use
2. **Test with representative inputs** — Try your agent with typical questions
3. **Watch for regressions** — Ensure other agent types still work well
4. **Commit prompt changes** — Document why you changed the prompt in version control

## Advanced: Manual Golden Set

You can also run the golden set manually from the terminal:

```powershell
# Default Ollama on localhost:11435
SimpleModelServer.Evals --mode router --router-url http://localhost:11435

# Remote server with Bearer token
SimpleModelServer.Evals --mode router --router-url http://remote-server:11435 --api-key YOUR_KEY
```

The golden set runs a battery of tests to ensure your agent still routes requests correctly and maintains baseline quality.

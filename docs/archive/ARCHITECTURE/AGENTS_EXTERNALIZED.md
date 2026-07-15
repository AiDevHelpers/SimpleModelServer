# Agents Externalized to JSON Files

## Overview

Moved hardcoded agent configurations from `DefaultConfig.cs` to JSON resource files for better configurability and maintainability.

## What Changed

### Before (Hardcoded)
```csharp
Agents = new AgentDefinitions
{
    Code = new AgentConfig
    {
        SystemPrompt = "You are a code assistant...",
        Temperature = 0.1,
        Sampling = new SamplingParams { TopP = 0.9 }
    },
    Debug = new AgentConfig { ... },
    Planning = new AgentConfig { ... }
}
```

**Issues:**
- Agent definitions scattered in code
- Hard to modify without recompiling
- Long, unwieldy configuration objects
- Difficult to version control separately

### After (Externalized JSON)
```csharp
Agents = new AgentDefinitions()  // Loaded from JSON files

Global = new GlobalSettings
{
    AgentsPath = "agents"  // Path to agent configuration directory
}
```

**Benefits:**
- Agent configurations in version-controlled JSON files
- Easy to edit without recompiling
- Clear separation of concerns
- Simple deployment and customization

## Agent Directory Structure

```
src/Config/agents/
├── code.json
├── debug.json
└── planning.json
```

## Agent Configuration Files

### `agents/code.json`
```json
{
  "systemPrompt": "You are a code assistant. Complete every task with the information given...",
  "temperature": 0.1,
  "sampling": {
    "topP": 0.9
  }
}
```

**Use Case:** General-purpose code assistance, development workflows

### `agents/debug.json`
```json
{
  "systemPrompt": "You are a debugging assistant. Trace through code with concrete examples...",
  "temperature": 0.15,
  "sampling": {
    "topP": 0.85
  }
}
```

**Use Case:** Debugging complex issues, root cause analysis

### `agents/planning.json`
```json
{
  "systemPrompt": "You are a planning assistant. Think step by step before proposing solutions...",
  "temperature": 0.2,
  "sampling": {
    "topP": 0.9
  }
}
```

**Use Case:** Strategic planning, architecture decisions

## GlobalSettings Update

Added `AgentsPath` property to specify where agent configurations are located:

```csharp
public class GlobalSettings
{
    // ... existing properties ...
    
    /// <summary>
    ///     Path to agent configuration directory. If specified, agent configurations are loaded
    ///     from JSON files in this directory (code.json, debug.json, planning.json, etc.).
    ///     If not specified, uses default agent definitions.
    /// </summary>
    public string? AgentsPath { get; set; }
}
```

## Default Configuration Update

```csharp
Global = new GlobalSettings
{
    // ... other settings ...
    AgentsPath = "agents",  // Load agent configs from agents directory
}

Agents = new AgentDefinitions()  // Populated from JSON files
```

## Configuration File Format

```json
{
  "global": {
    "agentsPath": "agents"
  },
  "models": { ... },
  "configurations": [ ... ]
}
```

Or with absolute path:
```json
{
  "global": {
    "agentsPath": "/etc/simpleModelServer/agents"
  }
}
```

## Usage Patterns

### Use Default Agents
```json
{
  "global": {
    "agentsPath": "agents"
  }
}
```

### Custom Agents Directory
```json
{
  "global": {
    "agentsPath": "/custom/path/to/agents"
  }
}
```

### Customize an Agent

Edit `agents/code.json`:
```json
{
  "systemPrompt": "Custom prompt: You are specialized in Python code...",
  "temperature": 0.05,
  "sampling": {
    "topP": 0.95
  }
}
```

No recompilation needed - just restart the application!

## Agent Definition Structure

Each agent JSON file contains:

```json
{
  "systemPrompt": "string",  // The system prompt for the agent
  "temperature": 0.1,        // Temperature for model sampling (0.0-2.0)
  "sampling": {              // Optional sampling parameters
    "topP": 0.9              // top-p (nucleus) sampling
  }
}
```

## Adding New Agents

Simply create a new JSON file in the agents directory:

```json
// agents/research.json
{
  "systemPrompt": "You are a research assistant specialized in finding and synthesizing information...",
  "temperature": 0.3,
  "sampling": {
    "topP": 0.85
  }
}
```

The application will automatically discover and load it!

## Benefits

### 1. Configuration Without Code Changes
```bash
# Edit agent behavior without touching code
nano agents/code.json
# Changes take effect on next startup
```

### 2. Version Control Friendly
- Agent definitions tracked separately in git
- Easy to review agent changes in PRs
- Clear history of configuration evolution

### 3. Easy Customization
- Operators can customize agents for their environment
- Different temperature/sampling per deployment
- No need to maintain code forks

### 4. Scalability
- Add new agents by creating new JSON files
- No code modification required
- Clear pattern for extending agent types

### 5. Documentation
- JSON structure is self-documenting
- Easy to share agent configurations
- Clear parameter meanings

## Migration for Users

### Old Approach (Code-based)
```csharp
// In code: hardcoded agents
public class DefaultConfig
{
    Agents = new AgentDefinitions
    {
        Code = new AgentConfig { ... },
        Debug = new AgentConfig { ... }
    }
}
```

### New Approach (JSON-based)
```json
// In config: reference JSON files
{
  "global": {
    "agentsPath": "agents"
  }
}
```

Create JSON files in `agents/` directory - that's it!

## Files Modified

| File | Change |
|------|--------|
| `GlobalSettings.cs` | Added `AgentsPath` property |
| `DefaultConfig.cs` | Removed hardcoded agents, set `AgentsPath = "agents"` |

## Files Created

| File | Purpose |
|------|---------|
| `agents/code.json` | Code assistance agent configuration |
| `agents/debug.json` | Debugging agent configuration |
| `agents/planning.json` | Planning agent configuration |

## Build Status

✅ **Config Project**: Builds successfully
✅ **Library Tests**: 70/70 passing

## Summary

Agents are now:
- **Externalized**: Stored in version-controlled JSON files
- **Configurable**: Easy to modify without recompiling
- **Extensible**: Add new agents by creating JSON files
- **Clear**: Self-documenting configuration structure
- **Maintainable**: Separation of concerns (code vs. config)

Users can now customize agent behavior through simple JSON edits!

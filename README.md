# SimpleModelServer

> **📚 Complete documentation available in [`docs/INDEX.md`](docs/INDEX.md)** - Use this as your navigation hub for API guides, architecture, features, and more.

## Overview

**SimpleModelServer** is a local HTTP API server that provides AI model inference capabilities with advanced features like draft-and-refine generation, multi-agent support, and GPU acceleration. Designed for developers and system administrators who need a powerful, easy-to-use local model server.

### Key Features

- **Draft-and-Refine Generation**: Fast initial draft generation followed by refinement for higher quality output
- **Multiple Model Providers**: Support for Ollama and LM Studio
- **GPU Acceleration**: Automatic GPU detection and VRAM management
- **Multi-Agent Support**: Multiple specialized agent types for different tasks
- **REST API**: Clean HTTP API for easy integration
- **Windows Service**: Run as a Windows Service for always-on availability
- **Visual Studio Extension**: Direct integration with Visual Studio for code analysis and generation
- **Configuration Management**: Flexible model configuration with multiple profiles

### Use Cases

- **Development**: Local AI-powered code generation, completion, and refactoring
- **Testing**: Run inference tests without cloud dependencies
- **Model Optimization**: Fine-tune model selection and hardware settings
- **Integration**: Embed AI capabilities into custom applications
- **Research**: Evaluate different models and configurations locally

### Architecture Highlights

SimpleModelServer is built on a clean, layered architecture:

```
┌─────────────────────────────────────────────────┐
│            Client Applications                   │
│  (VS Extension, Chat Client, HTTP Clients)     │
└────────────────────┬────────────────────────────┘
                     │ HTTP
┌────────────────────▼────────────────────────────┐
│          SimpleModelServer (HTTP API)            │
│  • Configuration Management                     │
│  • Request Routing & Agent Selection           │
│  • Hardware Monitoring                          │
└────────────────────┬────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────┐
│         Model Providers                          │
│  • Ollama Integration                           │
│  • LM Studio Integration                        │
└────────────────────┬────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────┐
│          Local Model Runtime                     │
│  (Ollama or LM Studio Process)                 │
└─────────────────────────────────────────────────┘
```

---

## Table of Contents

- [Getting Started](#getting-started)
- [Installation](#installation)
- [Usage](#usage)
- [Configuration](#configuration)
- [Features in Detail](#features-in-detail)
- [Troubleshooting](#troubleshooting)
- [System Requirements](#system-requirements-reference)
- [Support & Community](#support--community)
- [FAQ](#faq)

---

## Getting Started

### Quick Start (5 Minutes)

1. **Install .NET Runtime**: Download and install [.NET 10.0 Runtime](https://dotnet.microsoft.com/download)
2. **Install Ollama**: Download [Ollama](https://ollama.ai) and start the Ollama service
3. **Run SimpleModelServer**:
   ```powershell
   dotnet run --project src/SimpleModelServer.Loader -- --service
   ```
4. **Verify Installation**: Open a browser or use curl to test:
   ```bash
   curl http://localhost:11435/api/health
   ```

---

## Installation

### System Requirements

#### Minimum
- **OS**: Windows 10 or Windows 11 (x64, ARM64)
- **Processor**: Intel or AMD x64/ARM64
- **RAM**: 4 GB minimum (8 GB recommended)
- **Disk**: 2 GB free space
- **.NET Runtime**: .NET 10.0 or later

#### Recommended
- **OS**: Windows 11 (latest)
- **Processor**: Intel Core i7+ or AMD Ryzen 5+
- **RAM**: 16 GB or more
- **GPU**: NVIDIA (CUDA-enabled) or AMD (ROCm-enabled)
- **GPU VRAM**: 6 GB+ (depending on model size)

#### Optional Dependencies
- **Ollama**: For running models (separate installation)
- **LM Studio**: Alternative model provider (separate installation)

### Installation Methods

#### Method 1: From Source

```powershell
# Clone the repository
git clone https://github.com/yourusername/SimpleModelServer.git
cd SimpleModelServer

# Build the solution
dotnet build -c Release

# Run the server
dotnet run --project src/SimpleModelServer.Loader -- --service
```

#### Method 2: Pre-built Installer (Windows)

```powershell
# Download the MSI installer from releases
# Run: SimpleModelServer-Setup-x.y.z.msi

# Or install from command line:
msiexec /i SimpleModelServer-Setup-x.y.z.msi /quiet
```

#### Method 3: Docker

```bash
# Build the Docker image
docker build -t simple-model-server .

# Run the container
docker run -p 11435:11435 -v %APPDATA%\SimpleModelServer:/config simple-model-server
```

### First-Time Setup

#### 1. Configuration File

Configuration files are stored in: `C:\ProgramData\SimpleModelServer\`

The default configuration includes:
- `config.json` - Global settings and model configurations
- `models.json` - Model family definitions
- `agents.json` - Agent type configurations

#### 2. Verify Installation

```powershell
# Check service is running
Get-Service SimpleModelServerLoader | Select-Object Status

# Test the API
Invoke-WebRequest -Uri http://localhost:11435/api/health

# Get system information
Invoke-WebRequest -Uri http://localhost:11435/api/system/info | ConvertFrom-Json
```

#### 3. Test Model Generation

```powershell
# Using curl
curl -X POST http://localhost:11435/api/generate `
  -ContentType "application/json" `
  -Body @{
    "model" = "HelperModel"
    "prompt" = "Hello, world!"
    "stream" = $false
  } | ConvertFrom-Json
```

---

## Usage

### Running the Server

#### As a Windows Service (Recommended)

```powershell
# Start the service
Start-Service SimpleModelServerLoader

# Check status
Get-Service SimpleModelServerLoader

# Stop the service
Stop-Service SimpleModelServerLoader

# View logs (Event Viewer)
# Applications and Services Logs → SimpleModelServer
```

#### As a Console Application

```powershell
# Interactive mode with console output
dotnet run --project src/SimpleModelServer.Loader

# With custom configuration path
dotnet run --project src/SimpleModelServer.Loader -- --config "C:\MyConfig\config.json"

# Router only (no service management)
dotnet run --project src/SimpleModelServer.Loader -- --router --router-port 11435
```

### Interactive Chat Client

The built-in chat client provides an interactive interface:

```powershell
# Interactive chat session
dotnet run --project src/SimpleModelServer.Chat -- --interactive

# Single prompt
dotnet run --project src/SimpleModelServer.Chat -- --prompt "Explain quantum computing"

# With specific agent
dotnet run --project src/SimpleModelServer.Chat -- --interactive --agent code-analyzer
```

### Visual Studio Extension

#### Installation

1. Download `SimpleModelServer.Extensions.VS.vsix` from releases
2. In Visual Studio 2022:
   - Extensions → Manage Extensions
   - Search for "SimpleModelServer"
   - Or: Extensions → Manage Extensions → "..." → Install from File
3. Restart Visual Studio

#### Usage

| Feature | Keyboard Shortcut | Menu |
|---------|------------------|------|
| Explain Code | Ctrl+Alt+E | Tools → SimpleModelServer → Explain Code |
| Generate Tests | Ctrl+Alt+T | Tools → SimpleModelServer → Generate Tests |
| Optimize Code | Ctrl+Alt+O | Tools → SimpleModelServer → Optimize Code |
| Interactive Chat | Ctrl+Alt+C | Tools → SimpleModelServer → Chat |

#### Features

- **Explain Code**: Get AI-generated explanations of selected code
- **Generate Tests**: Automatically generate unit tests for selected functions
- **Optimize Code**: Get suggestions for code performance improvements
- **Documentation**: Generate documentation comments and docstrings
- **Code Review**: Ask the AI to review code for issues and improvements

---

## Configuration

### Configuration File Locations

| Component | Location |
|-----------|----------|
| Global Config | `C:\ProgramData\SimpleModelServer\config.json` |
| Model Definitions | `C:\ProgramData\SimpleModelServer\models.json` |
| Agent Definitions | `C:\ProgramData\SimpleModelServer\agents.json` |
| Logs | `C:\ProgramData\SimpleModelServer\logs\` |
| Cache | `C:\ProgramData\SimpleModelServer\cache\` |

### Global Settings (config.json)

```json
{
  "global": {
    "defaultAgentType": "code",
    "configurationId": "fast",
    "ollamaPort": 11434,
    "routerPort": 11435,
    "routerHost": "localhost",
    "gpuLayers": "auto",
    "cpuThreads": "auto",
    "trustedNetworks": [],
    "mdnsEnabled": true
  }
}
```

#### Available Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `defaultAgentType` | string | "code" | Default agent for requests (code, summarizer, analyzer) |
| `configurationId` | string | "fast" | Model configuration to use (fast, balanced, accurate) |
| `ollamaPort` | integer | 11434 | Port where Ollama listens |
| `routerPort` | integer | 11435 | Port for SimpleModelServer HTTP API |
| `routerHost` | string | "localhost" | Host to bind to (localhost, 0.0.0.0, etc.) |
| `gpuLayers` | string | "auto" | GPU layer count (auto, number, or disabled) |
| `cpuThreads` | string | "auto" | CPU thread count (auto or number) |
| `trustedNetworks` | array | [] | CIDR blocks allowed access |
| `mdnsEnabled` | boolean | true | Enable mDNS service discovery |

### Model Configurations

SimpleModelServer uses predefined model configurations that pair draft and main models:

```json
{
  "models": {
    "configurations": [
      {
        "id": "fast",
        "nickname": "Fast & Responsive",
        "isDefault": true,
        "families": [
          {
            "family": "qwen2.5",
            "variant": "coder",
            "draft": { "size": "0.5b" },
            "main": { "size": "3b" }
          }
        ]
      },
      {
        "id": "accurate",
        "nickname": "Accurate & Thorough",
        "isDefault": false,
        "families": [
          {
            "family": "deepseek",
            "variant": "coder",
            "draft": { "size": "1.3b" },
            "main": { "size": "6.7b" }
          }
        ]
      }
    ]
  }
}
```

### Environment Variables

```powershell
# Set verbose logging
$env:SIMPLEMODELSERVER_VERBOSE_LOGGING = "1"

# Override configuration path
$env:SIMPLEMODELSERVER_CONFIG_PATH = "C:\MyConfig\config.json"

# Override model configuration
$env:MODEL_CONFIGURATION = "accurate"

# Set Ollama host
$env:OLLAMA_HOST = "http://192.168.1.100:11434"
```

---

## Features in Detail

### Draft-and-Refine Generation

The draft-and-refine approach generates text in two stages:

1. **Draft Stage**: A small, fast model generates initial content
2. **Refine Stage**: A larger, more capable model improves and refines the draft

**Benefits:**
- Faster overall generation than using only the large model
- Higher quality than using only the small model
- Reduced VRAM requirements compared to large model only

**Configuration:**
```json
{
  "families": [
    {
      "family": "qwen2.5",
      "draft": { "size": "0.5b" },
      "main": { "size": "3b" }
    }
  ]
}
```

**When to Use:**
- Code generation (draft for structure, main for polish)
- Documentation generation (draft for outline, main for details)
- Creative writing (draft for ideas, main for refinement)

### Multiple Agents

Agents are specialized personas that focus on different tasks:

#### Available Agent Types

| Agent | Purpose | Best For |
|-------|---------|----------|
| `code` | Code generation and analysis | Writing, analyzing, refactoring code |
| `summarizer` | Text summarization | Condensing documentation, articles |
| `analyzer` | Code review and analysis | Finding issues, suggesting improvements |
| `writer` | General content generation | Documentation, comments, explanations |
| `debugger` | Error analysis and fixing | Debugging, error messages, troubleshooting |

#### Switching Agents

```bash
# Via API
curl -X POST http://localhost:11435/api/generate \
  -H "Content-Type: application/json" \
  -d '{
    "model": "HelperModel",
    "prompt": "Summarize this code",
    "agent": "summarizer"
  }'

# Via Chat Client
dotnet run --project src/SimpleModelServer.Chat -- --agent summarizer --interactive

# Via VS Extension
# Tools → SimpleModelServer → Agent Selection dropdown
```

### Hardware Management

SimpleModelServer automatically detects and manages hardware:

#### GPU Detection

- **NVIDIA**: CUDA compute capability detection, VRAM allocation
- **AMD**: ROCm support with VRAM detection
- **CPU Fallback**: Graceful CPU-only mode if no GPU detected

#### VRAM Management

```json
{
  "global": {
    "gpuLayers": "auto"  // auto, 0-100, or specific number
  }
}
```

- `auto`: Automatically optimize based on available VRAM
- `0`: CPU-only mode (no GPU acceleration)
- `1-100`: Specific number of layers to GPU offload

#### Performance Tuning

```powershell
# Check hardware capabilities
curl http://localhost:11435/api/system/info | ConvertFrom-Json

# Set specific GPU memory
$config = @{
  global = @{ gpuLayers = "40" }
} | ConvertTo-Json
[IO.File]::WriteAllText('config.json', $config)
```

### REST API Overview

SimpleModelServer provides a clean REST API for programmatic access. Full details are available in CONSUME.md.

#### Core Endpoints

- **POST /api/generate** - Generate text with specified model
- **GET /api/tags** - List available models
- **GET /api/system/info** - System capabilities and configuration
- **GET /api/health** - Health check
- **GET /api/models/{modelName}** - Model information

#### Response Streaming

For long-running requests, use Server-Sent Events (SSE):

```javascript
fetch('http://localhost:11435/api/generate', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    model: 'HelperModel',
    prompt: 'Generate code...',
    stream: true
  })
})
.then(response => response.body.getReader())
.then(reader => {
  // Process streaming tokens
});
```

---

## Troubleshooting

### Server Won't Start

**Problem**: Service fails to start or crashes immediately

**Solutions**:
```powershell
# 1. Check logs
Get-Content "$env:APPDATA\SimpleModelServer\logs\*.log"

# 2. Verify .NET installation
dotnet --version

# 3. Check Ollama is running
curl http://localhost:11434/api/health

# 4. Try console mode (more error details)
dotnet run --project src/SimpleModelServer.Loader
```

### Ollama Connection Issues

**Problem**: Server can't connect to Ollama

**Solutions**:
```powershell
# 1. Verify Ollama is running
Get-Process ollama

# 2. Check Ollama port
netstat -ano | findstr 11434

# 3. Update Ollama host in config
# Edit C:\ProgramData\SimpleModelServer\config.json:
# "ollamaPort": 11434

# 4. Restart both services
Stop-Service SimpleModelServerLoader
Restart-Service OllamaService
Start-Service SimpleModelServerLoader
```

### GPU Not Detected

**Problem**: GPU is available but not being used

**Solutions**:
```powershell
# 1. Check GPU detection
curl http://localhost:11435/api/system/info

# 2. Verify GPU drivers are installed
Get-WmiObject Win32_VideoController

# 3. Enable GPU in config
# Edit config.json and set gpuLayers to a number or "auto"

# 4. Restart service
Restart-Service SimpleModelServerLoader

# 5. Check logs
Get-EventLog -LogName Application -Source SimpleModelServer -Newest 10
```

### Configuration File Errors

**Problem**: Configuration file not found or invalid

**Solutions**:
```powershell
# 1. Verify file exists
Test-Path "C:\ProgramData\SimpleModelServer\config.json"

# 2. Check JSON syntax
[IO.File]::ReadAllText('C:\ProgramData\SimpleModelServer\config.json') | ConvertFrom-Json

# 3. Reset to default
Remove-Item "C:\ProgramData\SimpleModelServer\config.json"
# Restart service to regenerate default

# 4. Specify custom path
dotnet run --project src/SimpleModelServer.Loader -- --config "C:\MyConfig\config.json"
```

### Port Conflicts

**Problem**: Port 11435 is already in use

**Solutions**:
```powershell
# 1. Find what's using the port
netstat -ano | findstr 11435

# 2. Change router port in config
# Edit config.json: "routerPort": 11436

# 3. Or kill the conflicting process (use with caution)
taskkill /PID 1234 /F

# 4. Verify new port is working
curl http://localhost:11436/api/health
```

### High CPU Usage

**Problem**: CPU usage is high even when idle

**Solutions**:
```powershell
# 1. Check active requests
curl http://localhost:11435/api/system/info

# 2. Reduce CPU threads
# Edit config.json: "cpuThreads": "4"

# 3. Disable GPU if CPU is bottleneck
# Edit config.json: "gpuLayers": "0"

# 4. Restart service
Restart-Service SimpleModelServerLoader
```

### Memory Issues

**Problem**: Out of memory errors or excessive RAM usage

**Solutions**:
```powershell
# 1. Check current memory usage
Get-Process SimpleModelServerLoader

# 2. Reduce GPU layers to use less VRAM
# Edit config.json: "gpuLayers": "20"

# 3. Use a smaller model configuration
# Edit config.json: "configurationId": "fast"

# 4. Increase system RAM or reduce other processes
```

---

## System Requirements Reference

### OS Support

| OS | Architecture | Status |
|----|--------------|--------|
| Windows 11 | x64, ARM64 | Fully Supported |
| Windows 10 | x64 | Supported |
| Windows Server 2022 | x64 | Supported |
| Linux | x64 (WSL2) | Community Support |
| macOS | x64, ARM64 | Community Support |

### .NET Runtime Versions

| Version | Status | EOL |
|---------|--------|-----|
| .NET 10.0 | Current | Latest |
| .NET 9.0 | Supported | May 2026 |
| .NET 8.0 | LTS | Nov 2026 |
| .NET 7.0 | Unsupported | May 2023 |

### Hardware Compatibility

#### GPUs (Acceleration)

- **NVIDIA**: GeForce GTX 960+ (CUDA 11.8+)
- **AMD**: Radeon RX 580+ (ROCm 5.0+)
- **Intel**: Arc GPU (experimental)
- **Apple**: MPS (via community support)

#### Processors

- **Intel**: 3rd Gen Core i5+ (Sandy Bridge+)
- **AMD**: Ryzen 5 1600+
- **ARM**: Raspberry Pi 4+ (with sufficient RAM)

---

## Support & Community

### Getting Help

- **GitHub Issues**: [Report bugs and request features](https://github.com/yourusername/SimpleModelServer/issues)
- **Documentation**: [Full documentation and guides](https://github.com/yourusername/SimpleModelServer/wiki)
- **Discussions**: [Community Q&A and discussions](https://github.com/yourusername/SimpleModelServer/discussions)

### Contributing

Contributions are welcome! Please see CONTRIBUTING.md for guidelines.

### License

SimpleModelServer is released under the MIT License.

---

## FAQ

**Q: Can I use SimpleModelServer on Linux/macOS?**
A: Yes, with WSL2 on Windows or native .NET runtime on Linux/macOS. Community support available.

**Q: What models can I use?**
A: Any model compatible with Ollama or LM Studio. Recommended: Qwen2.5-Coder, DeepSeek-Coder, Mistral.

**Q: Can I use multiple GPUs?**
A: Not currently. Single GPU support only. Multi-GPU support planned for future releases.

**Q: How do I update SimpleModelServer?**
A: Download the latest installer or rebuild from source with `git pull && dotnet build -c Release`.

**Q: Is there a REST API?**
A: Yes! See CONSUME.md for complete API documentation.

**Q: Can I deploy this to the cloud?**
A: Yes, Docker support is available. See Docker section in Installation.

**Q: What's the latency for inference?**
A: Depends on model size and hardware. Typically 100-500ms for draft, 1-5s for main model.

---

## Version Information

| Component | Version | Status |
|-----------|---------|--------|
| SimpleModelServer | 1.0.0+ | Current Release |
| .NET Runtime | 10.0+ | Required |
| Ollama | Latest | Recommended |

**Last Updated**: July 14, 2026

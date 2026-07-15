# SimpleModelServer Documentation

Welcome to the SimpleModelServer documentation hub. This is your central navigation point for all documentation related to SimpleModelServer.

## Overview

SimpleModelServer is a local HTTP API server that provides AI model inference capabilities with advanced features like draft-and-refine generation, multi-agent support, and GPU acceleration. For a complete overview, see the [main README](../README.md).

---

## Documentation Sections

### Getting Started & Setup
Start here if you're new to SimpleModelServer.

- **[Architecture Overview](architecture.md)** - System design and components
- **[Model Configuration Guide](Config/model-configuration.md)** - How to configure models and providers (Ollama / LM Studio)
- **[Quick Reference](Config/model-config-quick-ref.md)** - Quick lookup for configuration options
- **[mDNS Discovery Guide](Server/mdns-discovery.md)** - Network discovery configuration

### Developer Guide
Resources for developers integrating SimpleModelServer into applications.

- **[API Guide](archive/DEVELOPER_GUIDE/API_GUIDE.md)** - Complete REST API reference and examples
- **[Router Contract](Server/router-contract.md)** - Understanding the request routing system

### Admin Configuration
The web-based admin panel and REST API for managing server settings at runtime.

- **[Admin Config Overview](Server/ADMIN_CONFIG.md)** - Configuration management & admin API (includes agent prompt editing)
- **[Admin Quick Start](Server/ADMIN_QUICKSTART.md)** - One-minute setup for the admin panel
- **[Admin System Guide](Server/README_ADMIN.md)** - Complete guide to the admin configuration system

### Extensions & Integration
Documentation for IDE integrations and client tools.

- **[VS Extension Settings Panel](Extensions/VS_EXTENSION_SETTINGS.md)** - Configure connection, edit agent prompts, run golden set validation
- **[TokenSaver Integration](Server/TOKENSAVER_INTEGRATION.md)** - Connect TokenSaver.MCP to SimpleModelServer router for shared inference
- **[TokenSaver SignalR Integration](Server/TOKENSAVER_SIGNALR_INTEGRATION.md)** - Real-time model reload coordination between TokenSaver and SimpleModelServer

### Logging
- **[Logging Configuration](Config/LOGGING.md)** - Serilog-based structured logging (console, Event Log, named pipes, file)
- **[Logging Config Examples](Config/LOGGING_CONFIG_EXAMPLES.md)** - Worked examples for common logging setups
- **[Serilog Migration Notes](Config/SERILOG_MIGRATION.md)** - Migration from Microsoft.Extensions.Logging to Serilog

### Features
Detailed documentation of key features.

- **[Model Family Feature](Config/model-family-feature.md)** - Model family, variant, and size configuration
- **[Chat Suffix Feature](archive/FEATURES/CHAT_SUFFIX_FEATURE.md)** - Chat suffix configuration for model naming
- **[KeepAlive Minutes Feature](archive/FEATURES/KEEPALIVE_MINUTES_UPDATE.md)** - Connection keepalive configuration

### Architecture & Design
Deep dives into system architecture and design decisions.

- **[Clean Architecture Summary](archive/ARCHITECTURE/CLEAN_ARCHITECTURE_SUMMARY.md)** - Backward compatibility removal and clean structure
- **[Family Pair Structure](archive/ARCHITECTURE/FAMILY_PAIR_STRUCTURE.md)** - Draft model and main model pairing
- **[Agents Externalized](archive/ARCHITECTURE/AGENTS_EXTERNALIZED.md)** - Agent configuration externalization
- **[Multi-Config System](archive/ARCHITECTURE/MULTI_CONFIG_SYSTEM.md)** - Configuration management system
- **[ModelFamily Enum](archive/ARCHITECTURE/MODELFAMILY_ENUM.md)** - Model family enumeration design

### Staged Pipeline
Documentation of the staged pipeline architecture for inference optimization.

- **[Pipeline Goals](Common/staged-pipeline-goals.md)** - Strategic objectives
- **[Pipeline Design](Common/staged-pipeline-design.md)** - Architecture and design
- **[Pipeline Concerns](Common/staged-pipeline-concerns.md)** - Technical considerations
- **[Pipeline Plan](Common/staged-pipeline-plan.md)** - Implementation plan
- **[Pipeline Review](Common/staged-pipeline-review.md)** - Design review
- **[Pipeline Test Plan](Common/staged-pipeline-test-plan.md)** - Testing strategy

### Extensions
- **[VS Code Extension Plan](VSCODE_EXTENSION_PLAN.md)** - Porting the VS extension to VS Code as a Copilot model provider

### Implementation Notes
Development and implementation documentation.

- **[Implementation Summary](archive/IMPLEMENTATION_NOTES/IMPLEMENTATION_SUMMARY.md)** - Overview of implementation
- **[Implementation Complete](archive/IMPLEMENTATION_NOTES/IMPLEMENTATION_COMPLETE.md)** - Completion status
- **[Extension Implementation Complete](archive/IMPLEMENTATION_NOTES/EXTENSION_IMPLEMENTATION_COMPLETE.md)** - VS Extension implementation status
- **[FamilyPair Implementation Summary](archive/IMPLEMENTATION_NOTES/FAMILYPAIR_IMPLEMENTATION_SUMMARY.md)** - Family pair feature implementation
- **[Default Config Updated](archive/IMPLEMENTATION_NOTES/DEFAULT_CONFIG_UPDATED.md)** - Default configuration changes

### Build & Evaluation
Resources for building and evaluating the system.

- **[Build Guide](../build/BUILD_GUIDE.md)** - Building SimpleModelServer
- **[Implementation Report](../build/IMPLEMENTATION_REPORT.md)** - Build and implementation report
- **[Infrastructure Summary](../build/INFRASTRUCTURE_SUMMARY.md)** - Infrastructure overview
- **[Versioning](../build/VERSIONING.md)** - Version management

### Benchmarks & Evaluation
Performance and evaluation resources.

- **[Phase 0 Benchmarks](benchmarks/phase0-results.md)** - Initial performance benchmarks
- **[Evaluation Harness Prompt](prompts/eval-harness-prompt.md)** - Evaluation harness guidelines
- **[Golden Set Authoring Prompt](prompts/golden-set-authoring-prompt.md)** - Golden test set creation

### Project Resources

- **[Changelog](CHANGELOG.md)** - Version history and release notes
- **[Project Setup (Extensions)](../src/Extensions/SimpleModelServer.Extensions.VS/README.md)** - VS Extension documentation
- **[Evals Documentation](../src/Evals/SimpleModelServer.Evals/README.md)** - Evaluation framework
- **[Golden Tests](../tests/golden/README.md)** - Golden test suite

---

## Quick Links

### For Users
- [Main README](../README.md) - Project overview and quick start
- [Model Configuration](Config/model-configuration.md) - Configure your models and inference provider
- [Architecture Overview](architecture.md) - Understand the system
- [Admin Quick Start](Server/ADMIN_QUICKSTART.md) - Manage server settings from the web UI

### For Developers
- [API Guide](archive/DEVELOPER_GUIDE/API_GUIDE.md) - Integrate with the API
- [Router Contract](Server/router-contract.md) - Understanding routing
- [VS Extension Guide](../src/Extensions/SimpleModelServer.Extensions.VS/README.md) - Extension development

### For System Administrators
- [Build Guide](../build/BUILD_GUIDE.md) - Build and deploy
- [Admin Config Overview](Server/ADMIN_CONFIG.md) - Runtime configuration management
- [Logging Configuration](Config/LOGGING.md) - Structured logging setup
- [Infrastructure Summary](../build/INFRASTRUCTURE_SUMMARY.md) - System infrastructure
- [Versioning](../build/VERSIONING.md) - Version management

---

## Document Organization

Documentation is organized under `docs/` by the project it documents, mirroring `src/`'s layout, with legacy/superseded documents kept under `archive/` for reference:

```
docs/
├── INDEX.md (this file)
├── CHANGELOG.md
├── architecture.md
├── VSCODE_EXTENSION_PLAN.md
│
├── Common/               — staged-pipeline-*.md (6 files)
├── Config/                — model-configuration.md, model-config-quick-ref.md, model-family-feature.md,
│                             LOGGING.md, LOGGING_CONFIG_EXAMPLES.md, SERILOG_MIGRATION.md
├── Server/                — router-contract.md, mdns-discovery.md, ADMIN_CONFIG.md, ADMIN_QUICKSTART.md, README_ADMIN.md
│
├── archive/                — superseded docs, kept for reference (ARCHITECTURE/, DEVELOPER_GUIDE/,
│                             FEATURES/, IMPLEMENTATION_NOTES/, and standalone files)
├── benchmarks/            — phase0-results.md
└── prompts/                — eval-harness-prompt.md, golden-set-authoring-prompt.md
```

`Client/`, `Contracts/`, `Evals/`, `Extensions/`, `Hardware/`, `Providers/`, and `UI/` subdirectories also exist under `docs/` for future project-specific documentation but are currently empty.

---

## Navigation Tips

1. **New to SimpleModelServer?** Start with the [Main README](../README.md)
2. **Want to integrate the API?** Go to [API Guide](archive/DEVELOPER_GUIDE/API_GUIDE.md)
3. **Need configuration help?** See [Model Configuration](Config/model-configuration.md)
4. **Managing server settings at runtime?** See [Admin Quick Start](Server/ADMIN_QUICKSTART.md)
5. **Interested in system design?** Check out [Architecture Overview](architecture.md)
6. **Looking for implementation details?** Browse [Implementation Notes](archive/IMPLEMENTATION_NOTES/)

---

## Contributing

When adding new documentation:
1. Create files under the `docs/<ProjectName>/` subdirectory matching the `src/` project it documents (e.g. `docs/Server/` for `src/Server/`), or at `docs/` top-level for cross-cutting docs.
2. Update this INDEX.md to include links to new documents.
3. Use consistent markdown formatting.
4. Include a brief description of the document content.

---

**Last Updated**: 2026-07-15

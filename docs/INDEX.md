# SimpleModelServer Documentation

Welcome to the SimpleModelServer documentation hub. This is your central navigation point for all documentation related to SimpleModelServer.

## Overview

SimpleModelServer is a local HTTP API server that provides AI model inference capabilities with advanced features like draft-and-refine generation, multi-agent support, and GPU acceleration. For a complete overview, see the [main README](../README.md).

---

## Documentation Sections

### Getting Started & Setup
Start here if you're new to SimpleModelServer.

- **[Architecture Overview](architecture.md)** - System design and components
- **[Model Configuration Guide](model-configuration.md)** - How to configure models and providers
- **[Quick Reference](model-config-quick-ref.md)** - Quick lookup for configuration options
- **[mDNS Discovery Guide](mdns-discovery.md)** - Network discovery configuration

### Developer Guide
Resources for developers integrating SimpleModelServer into applications.

- **[API Guide](archive/DEVELOPER_GUIDE/API_GUIDE.md)** - Complete REST API reference and examples
- **[Router Contract](router-contract.md)** - Understanding the request routing system

### Features
Detailed documentation of key features.

- **[Model Family Feature](model-family-feature.md)** - Model family, variant, and size configuration
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

- **[Pipeline Goals](staged-pipeline-goals.md)** - Strategic objectives
- **[Pipeline Design](staged-pipeline-design.md)** - Architecture and design
- **[Pipeline Concerns](staged-pipeline-concerns.md)** - Technical considerations
- **[Pipeline Plan](staged-pipeline-plan.md)** - Implementation plan
- **[Pipeline Review](staged-pipeline-review.md)** - Design review
- **[Pipeline Test Plan](staged-pipeline-test-plan.md)** - Testing strategy

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
- [Model Configuration](model-configuration.md) - Configure your models
- [Architecture Overview](architecture.md) - Understand the system

### For Developers
- [API Guide](archive/DEVELOPER_GUIDE/API_GUIDE.md) - Integrate with the API
- [Router Contract](router-contract.md) - Understanding routing
- [VS Extension Guide](../src/Extensions/SimpleModelServer.Extensions.VS/README.md) - Extension development

### For System Administrators
- [Build Guide](../build/BUILD_GUIDE.md) - Build and deploy
- [Infrastructure Summary](../build/INFRASTRUCTURE_SUMMARY.md) - System infrastructure
- [Versioning](../build/VERSIONING.md) - Version management

---

## Document Organization

```
docs/
├── INDEX.md (this file)
├── CHANGELOG.md
├── README.md (links to this hub)
│
├── ARCHITECTURE/
│   ├── CLEAN_ARCHITECTURE_SUMMARY.md
│   ├── FAMILY_PAIR_STRUCTURE.md
│   ├── AGENTS_EXTERNALIZED.md
│   ├── MULTI_CONFIG_SYSTEM.md
│   └── MODELFAMILY_ENUM.md
│
├── DEVELOPER_GUIDE/
│   └── API_GUIDE.md
│
├── FEATURES/
│   ├── CHAT_SUFFIX_FEATURE.md
│   └── KEEPALIVE_MINUTES_UPDATE.md
│
├── IMPLEMENTATION_NOTES/
│   ├── IMPLEMENTATION_SUMMARY.md
│   ├── IMPLEMENTATION_COMPLETE.md
│   ├── EXTENSION_IMPLEMENTATION_COMPLETE.md
│   ├── FAMILYPAIR_IMPLEMENTATION_SUMMARY.md
│   └── DEFAULT_CONFIG_UPDATED.md
│
├── benchmarks/
│   └── phase0-results.md
│
├── prompts/
│   ├── eval-harness-prompt.md
│   └── golden-set-authoring-prompt.md
│
├── *.md (main documentation files)
│   ├── architecture.md
│   ├── model-configuration.md
│   ├── model-config-quick-ref.md
│   ├── model-family-feature.md
│   ├── mdns-discovery.md
│   ├── router-contract.md
│   └── staged-pipeline-*.md
```

---

## Navigation Tips

1. **New to SimpleModelServer?** Start with the [Main README](../README.md)
2. **Want to integrate the API?** Go to [API Guide](archive/DEVELOPER_GUIDE/API_GUIDE.md)
3. **Need configuration help?** See [Model Configuration](model-configuration.md)
4. **Interested in system design?** Check out [Architecture Overview](architecture.md)
5. **Looking for implementation details?** Browse [Implementation Notes](IMPLEMENTATION_NOTES/)

---

## Contributing

When adding new documentation:
1. Create files in the appropriate category subdirectory
2. Update this INDEX.md to include links to new documents
3. Use consistent markdown formatting
4. Include a brief description of the document content

---

**Last Updated**: 2024-07-14

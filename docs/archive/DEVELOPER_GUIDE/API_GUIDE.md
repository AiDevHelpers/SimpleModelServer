# CONSUME.md — Developer Guide for SimpleModelServer

This document is for developers and application engineers who want to integrate SimpleModelServer into their applications or build clients that consume its API.

## Overview

SimpleModelServer provides a REST API for AI model inference. It's designed to be easy to consume from any programming language while providing advanced features like streaming responses, multi-agent support, and system capability discovery.

### What SimpleModelServer Provides

- **HTTP REST API** for model inference requests
- **Server-Sent Events (SSE)** for streaming token-by-token responses
- **Model Discovery** endpoints to query available models and capabilities
- **System Information** endpoint for hardware and configuration details
- **Health Checks** for monitoring and service availability
- **Multiple Agent Types** for specialized inference tasks

### API-First Design

SimpleModelServer is designed with API-first principles:
- Every feature is accessible via HTTP endpoints
- No SDK required (though language-specific libraries are helpful)
- Standard HTTP methods and status codes
- JSON request/response format
- Streaming support for long-running operations

---

## Table of Contents

- [Getting Started](#getting-started)
- [Architecture](#architecture)
- [API Reference](#api-reference)
- [Client Libraries & Examples](#client-libraries--examples)
- [Advanced Topics](#advanced-topics)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting-for-integration)

---

## Getting Started

### Prerequisites

- SimpleModelServer running locally (default: `localhost:11435`) or on a network server
- HTTP client library (curl, requests, fetch, HttpClient, etc.)
- JSON parsing capability
- Basic understanding of REST APIs and HTTP

### Minimal Example

```bash
# Health check
curl http://localhost:11435/api/health

# List available models
curl http://localhost:11435/api/tags

# Generate text
curl -X POST http://localhost:11435/api/generate \
  -H "Content-Type: application/json" \
  -d '{
    "model": "HelperModel",
    "prompt": "Hello, world!",
    "stream": false
  }'
```

### Checking Server Capabilities

Always query `/api/system/info` on startup to discover what's available:

```bash
curl http://localhost:11435/api/system/info
```

Response:
```json
{
  "capabilities": {
    "availableModels": ["HelperModel", "CodeAnalyzer"],
    "availableAgents": ["code", "summarizer", "analyzer"],
    "hardwareInfo": {
      "gpuDetected": true,
      "totalVram": 8192,
      "totalRam": 32768
    },
    "features": ["streaming", "agents", "draft-and-refine"]
  },
  "configuration": {
    "defaultAgent": "code",
    "routerPort": 11435,
    "ollamaPort": 11434
  }
}
```

---

## Architecture

### Request Flow

```
Client Request
    ↓
SimpleModelServer (Router)
    ↓
Agent Selection & Request Routing
    ↓
Model Provider (Ollama/LM Studio)
    ↓
Local Model Runtime
    ↓
Response Stream or Completion
    ↓
Client Receives Response
```

### Response Modes

SimpleModelServer supports two response modes:

#### 1. Non-Streaming (stream=false)
- Request returns when generation is complete
- Full response in single JSON object
- Suitable for short, quick requests
- Blocking until completion

#### 2. Streaming (stream=true)
- Response uses Server-Sent Events (SSE)
- Tokens sent as they're generated
- Suitable for long-running requests
- Better user experience (progressive display)

### Components

**Router**: The HTTP API endpoint that receives requests and routes them to appropriate agents and models.

**Agent**: A specialized persona (code, summarizer, analyzer) that affects generation behavior.

**Model Provider**: Abstraction over Ollama, LM Studio, or other model runners.

**Local Runtime**: The actual model execution (Ollama process, LM Studio, etc.).

---

## API Reference

### Base URL

```
http://localhost:11435
```

(Configurable via `routerPort` and `routerHost` in settings)

### Core Endpoints

#### 1. POST /api/generate

Generate text using a specified model.

**Request:**
```json
{
  "model": "HelperModel",
  "prompt": "Write a hello world program",
  "stream": false,
  "system": "You are a helpful assistant",
  "agent": "code",
  "options": {
    "temperature": 0.7,
    "top_p": 0.9,
    "top_k": 40
  }
}
```

**Fields:**

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `model` | string | Yes | - | Model name (from /api/tags) |
| `prompt` | string | Yes | - | The input text to generate from |
| `stream` | boolean | No | false | Enable token streaming |
| `system` | string | No | - | System prompt for context |
| `agent` | string | No | default | Agent type (code, summarizer, analyzer) |
| `options` | object | No | {} | Model-specific sampling parameters |

**Response (stream=false):**
```json
{
  "model": "HelperModel",
  "created_at": "2026-07-14T10:30:00Z",
  "response": "Hello, world! Here's a simple program...",
  "done": true,
  "context": [],
  "total_duration": 2500000000,
  "load_duration": 500000000,
  "prompt_eval_count": 25,
  "prompt_eval_duration": 1000000000,
  "eval_count": 150,
  "eval_duration": 1000000000
}
```

**Response (stream=true):**

Server-Sent Events stream. Each event contains a partial response:

```
data: {"response":"Hello","done":false}
data: {"response":",","done":false}
data: {"response":" world","done":false}
data: {"response":"!","done":true,"context":[],...}
```

**HTTP Status Codes:**

| Code | Meaning |
|------|---------|
| 200 | Success |
| 400 | Bad request (invalid JSON, missing fields) |
| 404 | Model not found |
| 503 | Service unavailable (Ollama not running) |
| 500 | Internal server error |

**Example:**

```bash
curl -X POST http://localhost:11435/api/generate \
  -H "Content-Type: application/json" \
  -d '{
    "model": "HelperModel",
    "prompt": "Hello",
    "stream": false
  }'
```

---

#### 2. GET /api/tags

List available models.

**Request:**
```bash
curl http://localhost:11435/api/tags
```

**Response:**
```json
{
  "models": [
    {
      "name": "HelperModel",
      "size": 3000000000,
      "digest": "sha256:abcd1234...",
      "details": {
        "format": "gguf",
        "family": "llama",
        "families": ["llama"],
        "parameter_size": "3B",
        "quantization_level": "Q4"
      }
    },
    {
      "name": "CodeAnalyzer",
      "size": 6700000000,
      "digest": "sha256:efgh5678...",
      "details": { ... }
    }
  ]
}
```

---

#### 3. GET /api/system/info

Get system capabilities and configuration.

**Request:**
```bash
curl http://localhost:11435/api/system/info
```

**Response:**
```json
{
  "capabilities": {
    "availableModels": ["HelperModel", "CodeAnalyzer"],
    "availableAgents": ["code", "summarizer", "analyzer", "writer", "debugger"],
    "hardwareInfo": {
      "gpuDetected": true,
      "gpuName": "NVIDIA GeForce RTX 3090",
      "totalVram": 8589934592,
      "availableVram": 7516192768,
      "totalRam": 34359738368,
      "availableRam": 16106127360,
      "cpuCores": 16
    },
    "features": {
      "streaming": true,
      "agents": true,
      "draftAndRefine": true,
      "multiAgent": true,
      "gpuAcceleration": true
    }
  },
  "configuration": {
    "defaultAgent": "code",
    "activeConfiguration": "fast",
    "routerPort": 11435,
    "ollamaPort": 11434,
    "mdnsEnabled": true
  }
}
```

**Use this endpoint to:**
- Discover what agents are available
- Check hardware capabilities
- Determine feature support
- Get configuration details

---

#### 4. GET /api/health

Health check endpoint for monitoring.

**Request:**
```bash
curl http://localhost:11435/api/health
```

**Response:**
```json
{
  "status": "healthy",
  "timestamp": "2026-07-14T10:30:00Z",
  "uptime": 3600000,
  "requestsProcessed": 42,
  "lastError": null
}
```

**Status Values:**
- `healthy` - Service is running normally
- `degraded` - Service running but with issues
- `unhealthy` - Service not responding

---

#### 5. GET /api/models/{modelName}

Get detailed information about a specific model.

**Request:**
```bash
curl http://localhost:11435/api/models/HelperModel
```

**Response:**
```json
{
  "name": "HelperModel",
  "size": 3000000000,
  "format": "gguf",
  "family": "llama",
  "parameterSize": "3B",
  "quantization": "Q4",
  "loaded": true,
  "lastAccessed": "2026-07-14T10:25:00Z",
  "capabilities": {
    "chat": true,
    "completion": true,
    "embedding": false
  }
}
```

---

## Client Libraries & Examples

### HTTP Client (Any Language)

#### cURL

```bash
# Simple request
curl -X POST http://localhost:11435/api/generate \
  -H "Content-Type: application/json" \
  -d '{
    "model": "HelperModel",
    "prompt": "Hello",
    "stream": false
  }'

# With streaming
curl -X POST http://localhost:11435/api/generate \
  -H "Content-Type: application/json" \
  -d '{
    "model": "HelperModel",
    "prompt": "Write code",
    "stream": true
  }'

# Extract just response
curl -s http://localhost:11435/api/generate \
  -H "Content-Type: application/json" \
  -d '{...}' | jq -r '.response'
```

#### Python (requests)

```python
import requests
import json

# Simple generation
response = requests.post(
    'http://localhost:11435/api/generate',
    json={
        'model': 'HelperModel',
        'prompt': 'Hello, world!',
        'stream': False
    }
)
result = response.json()
print(result['response'])

# Streaming generation
response = requests.post(
    'http://localhost:11435/api/generate',
    json={
        'model': 'HelperModel',
        'prompt': 'Write a function',
        'stream': True
    },
    stream=True
)

for line in response.iter_lines():
    if line:
        data = json.loads(line.decode().replace('data: ', ''))
        print(data['response'], end='', flush=True)
```

#### Python (aiohttp - Async)

```python
import aiohttp
import asyncio
import json

async def generate_async():
    async with aiohttp.ClientSession() as session:
        async with session.post(
            'http://localhost:11435/api/generate',
            json={
                'model': 'HelperModel',
                'prompt': 'Hello',
                'stream': True
            }
        ) as resp:
            async for line in resp.content:
                if line:
                    data = json.loads(line.decode().replace('data: ', ''))
                    print(data['response'], end='', flush=True)

asyncio.run(generate_async())
```

#### C# (.NET)

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class SimpleModelServerClient
{
    private readonly HttpClient _client;
    private const string BaseUrl = "http://localhost:11435";

    public SimpleModelServerClient()
    {
        _client = new HttpClient();
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        var request = new
        {
            model = "HelperModel",
            prompt = prompt,
            stream = false
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _client.PostAsync(
            $"{BaseUrl}/api/generate",
            content
        );

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("response").GetString() ?? "";
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(string prompt)
    {
        var request = new
        {
            model = "HelperModel",
            prompt = prompt,
            stream = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await _client.PostAsync(
            $"{BaseUrl}/api/generate",
            content
        );

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("data: "))
            {
                var json = line.Substring("data: ".Length);
                using var doc = JsonDocument.Parse(json);
                var response_text = doc.RootElement.GetProperty("response").GetString() ?? "";
                yield return response_text;
            }
        }
    }
}

// Usage
var client = new SimpleModelServerClient();
var result = await client.GenerateAsync("Hello");
Console.WriteLine(result);

// Streaming
await foreach (var token in client.GenerateStreamAsync("Write code"))
{
    Console.Write(token);
}
```

#### JavaScript (Node.js with axios)

```javascript
const axios = require('axios');

// Simple generation
async function generate(prompt) {
    const response = await axios.post('http://localhost:11435/api/generate', {
        model: 'HelperModel',
        prompt: prompt,
        stream: false
    });
    return response.data.response;
}

// Streaming generation
async function generateStream(prompt) {
    const response = await axios.post(
        'http://localhost:11435/api/generate',
        {
            model: 'HelperModel',
            prompt: prompt,
            stream: true
        },
        {
            responseType: 'stream',
            headers: { 'Content-Type': 'application/json' }
        }
    );

    return new Promise((resolve, reject) => {
        let result = '';
        response.data.on('data', (chunk) => {
            const line = chunk.toString().trim();
            if (line.startsWith('data: ')) {
                const data = JSON.parse(line.substring('data: '.length));
                result += data.response;
                process.stdout.write(data.response);
            }
        });
        response.data.on('end', () => resolve(result));
        response.data.on('error', reject);
    });
}

generate('Hello, world!').then(console.log);
```

#### JavaScript (Browser with Fetch API)

```javascript
// Simple generation
async function generate(prompt) {
    const response = await fetch('http://localhost:11435/api/generate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            model: 'HelperModel',
            prompt: prompt,
            stream: false
        })
    });
    const data = await response.json();
    return data.response;
}

// Streaming generation
async function* generateStream(prompt) {
    const response = await fetch('http://localhost:11435/api/generate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            model: 'HelperModel',
            prompt: prompt,
            stream: true
        })
    });

    const reader = response.body.getReader();
    const decoder = new TextDecoder();

    while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value);
        const lines = chunk.split('\n');

        for (const line of lines) {
            if (line.startsWith('data: ')) {
                const data = JSON.parse(line.substring('data: '.length));
                yield data.response;
            }
        }
    }
}

// Usage
generate('Hello').then(console.log);

// Streaming
(async () => {
    for await (const token of generateStream('Write code')) {
        process.stdout.write(token);
    }
})();
```

#### Java

```java
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.net.URI;
import com.google.gson.Gson;
import com.google.gson.JsonObject;

public class SimpleModelServerClient {
    private final HttpClient client;
    private final String baseUrl = "http://localhost:11435";

    public SimpleModelServerClient() {
        this.client = HttpClient.newHttpClient();
    }

    public String generate(String prompt) throws Exception {
        JsonObject request = new JsonObject();
        request.addProperty("model", "HelperModel");
        request.addProperty("prompt", prompt);
        request.addProperty("stream", false);

        HttpRequest httpRequest = HttpRequest.newBuilder()
            .uri(new URI(baseUrl + "/api/generate"))
            .header("Content-Type", "application/json")
            .POST(HttpRequest.BodyPublishers.ofString(request.toString()))
            .build();

        HttpResponse<String> response = client.send(httpRequest,
            HttpResponse.BodyHandlers.ofString());

        Gson gson = new Gson();
        JsonObject result = gson.fromJson(response.body(), JsonObject.class);
        return result.get("response").getAsString();
    }
}

// Usage
SimpleModelServerClient client = new SimpleModelServerClient();
String result = client.generate("Hello");
System.out.println(result);
```

---

## Advanced Topics

### Authentication & Security

**Current Status**: SimpleModelServer v1.0 does not require authentication.

**Planned for Future Versions**:
- API key authentication
- OAuth2 support
- HTTPS/TLS support

**Network Security Best Practices**:

```json
{
  "global": {
    "trustedNetworks": [
      "127.0.0.1/8",
      "192.168.1.0/24"
    ]
  }
}
```

---

### Streaming Responses

#### Server-Sent Events (SSE) Format

Streaming responses use the SSE protocol. Each token is sent as:

```
data: {"response":"token","done":false}
```

When complete:

```
data: {"response":"","done":true,"context":[...],...}
```

#### Parsing Streaming Responses

**Key Points**:
1. Read lines from response body
2. Lines starting with "data: " contain JSON
3. Parse JSON to get token
4. Stop when `done: true`
5. Handle partial tokens (e.g., part of a word)

**Example - Python**:
```python
response = requests.post(..., stream=True)
buffer = ""

for line in response.iter_lines():
    if line.startswith(b'data: '):
        json_str = line[6:].decode()
        data = json.loads(json_str)
        buffer += data['response']
        if data['done']:
            print(f"Complete: {buffer}")
            break

print(f"Accumulated tokens: {buffer}")
```

---

### Batch Requests

For multiple independent requests:

```python
import asyncio
import aiohttp

async def batch_generate(prompts):
    async with aiohttp.ClientSession() as session:
        tasks = [
            session.post(
                'http://localhost:11435/api/generate',
                json={'model': 'HelperModel', 'prompt': p, 'stream': False}
            )
            for p in prompts
        ]
        responses = await asyncio.gather(*tasks)
        return [await r.json() for r in responses]

# Usage
results = asyncio.run(batch_generate([
    "Hello",
    "World",
    "Test"
]))
```

---

### Error Handling

```python
import requests
from requests.exceptions import Timeout, ConnectionError

def safe_generate(prompt, timeout=30):
    try:
        response = requests.post(
            'http://localhost:11435/api/generate',
            json={
                'model': 'HelperModel',
                'prompt': prompt,
                'stream': False
            },
            timeout=timeout
        )
        response.raise_for_status()
        return response.json()['response']

    except Timeout:
        print("Request timed out (server slow or busy)")
        # Retry with longer timeout or use streaming
        
    except ConnectionError:
        print("Cannot connect to SimpleModelServer")
        print("Verify it's running: curl http://localhost:11435/api/health")
        
    except requests.HTTPError as e:
        if e.response.status_code == 404:
            print("Model not found. Available models:")
            # Query /api/tags
        elif e.response.status_code == 503:
            print("Service unavailable. Check Ollama is running")
        else:
            print(f"HTTP {e.response.status_code}: {e.response.text}")
            
    except Exception as e:
        print(f"Unexpected error: {e}")
        return None
```

---

### Retry Strategies

```python
import time
from functools import wraps

def retry_with_backoff(max_retries=3, base_delay=1):
    def decorator(func):
        def wrapper(*args, **kwargs):
            for attempt in range(max_retries):
                try:
                    return func(*args, **kwargs)
                except Exception as e:
                    if attempt == max_retries - 1:
                        raise
                    delay = base_delay * (2 ** attempt)  # exponential backoff
                    print(f"Attempt {attempt + 1} failed: {e}. Retrying in {delay}s...")
                    time.sleep(delay)
        return wrapper
    return decorator

@retry_with_backoff(max_retries=3, base_delay=1)
def generate(prompt):
    response = requests.post('http://localhost:11435/api/generate', ...)
    response.raise_for_status()
    return response.json()['response']
```

---

### Timeout Handling

```csharp
// C# with timeout
var handler = new HttpClientHandler();
using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

try {
    var response = await client.PostAsync(...);
} catch (TaskCanceledException) {
    // Timeout occurred
    // For streaming, consider longer timeout or use streaming API
}
```

---

## Best Practices

### 1. Startup Initialization

Always discover capabilities on application startup:

```python
def initialize_client():
    # Check health
    health = requests.get('http://localhost:11435/api/health').json()
    assert health['status'] in ['healthy', 'degraded']
    
    # Get capabilities
    info = requests.get('http://localhost:11435/api/system/info').json()
    
    # Check required capabilities
    capabilities = info['capabilities']
    assert 'code' in capabilities['availableAgents']
    assert 'HelperModel' in capabilities['availableModels']
    
    return info
```

### 2. Cache System Information

Don't query `/api/system/info` on every request. Cache it and refresh periodically:

```python
import time

class ClientCache:
    def __init__(self, refresh_interval=3600):  # 1 hour
        self.system_info = None
        self.last_refresh = 0
        self.refresh_interval = refresh_interval
    
    def get_system_info(self):
        now = time.time()
        if self.system_info is None or (now - self.last_refresh) > self.refresh_interval:
            self.system_info = requests.get('http://localhost:11435/api/system/info').json()
            self.last_refresh = now
        return self.system_info
```

### 3. Use Streaming for Long Requests

Streaming provides better UX:

```python
# Good for short responses
response = requests.post(..., json={..., 'stream': False})
print(response.json()['response'])

# Better for long responses (code generation, etc.)
for token in stream_generate(prompt):
    print(token, end='', flush=True)
```

### 4. Validate Model Names

Always validate model names before sending requests:

```python
def validate_and_generate(prompt, model):
    # Get available models
    tags = requests.get('http://localhost:11435/api/tags').json()
    available = [m['name'] for m in tags['models']]
    
    if model not in available:
        raise ValueError(f"Model '{model}' not found. Available: {available}")
    
    # Safe to generate
    return requests.post(
        'http://localhost:11435/api/generate',
        json={'model': model, 'prompt': prompt, 'stream': False}
    ).json()
```

### 5. Connection Pooling

Reuse HTTP connections:

```csharp
// Singleton HttpClient for all requests
public static class HttpClientSingleton
{
    private static readonly Lazy<HttpClient> _instance = 
        new Lazy<HttpClient>(() => new HttpClient());
    
    public static HttpClient Instance => _instance.Value;
}

// Usage
var client = HttpClientSingleton.Instance;
await client.PostAsync(...);
```

### 6. Log All Requests

For debugging and monitoring:

```python
import logging

logger = logging.getLogger('SimpleModelServer')

def logged_generate(prompt, model='HelperModel'):
    logger.info(f"Generating with {model}: {prompt[:50]}...")
    
    try:
        response = requests.post(
            'http://localhost:11435/api/generate',
            json={'model': model, 'prompt': prompt, 'stream': False}
        )
        response.raise_for_status()
        result = response.json()
        logger.info(f"Generated {len(result['response'])} characters")
        return result
    except Exception as e:
        logger.error(f"Generation failed: {e}")
        raise
```

### 7. Handle Network Timeouts Gracefully

```javascript
async function generateWithFallback(prompt) {
    try {
        // Try with 30 second timeout
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 30000);
        
        const response = await fetch('http://localhost:11435/api/generate', {
            method: 'POST',
            signal: controller.signal,
            // ...
        });
        
        clearTimeout(timeout);
        return await response.json();
        
    } catch (error) {
        if (error.name === 'AbortError') {
            console.log("Request timed out. Trying streaming API...");
            // Fall back to streaming
            return await generateStreaming(prompt);
        }
        throw error;
    }
}
```

---

## Integration Patterns

### Pattern 1: Simple Code Generation

```python
def generate_code(requirements):
    response = requests.post(
        'http://localhost:11435/api/generate',
        json={
            'model': 'HelperModel',
            'prompt': f"Write Python code: {requirements}",
            'agent': 'code',
            'stream': False
        }
    )
    return response.json()['response']

# Usage
code = generate_code("Function to sort a list")
print(code)
```

### Pattern 2: Multi-Agent Analysis

```python
def analyze_with_multiple_agents(text, agents=['analyzer', 'summarizer']):
    results = {}
    for agent in agents:
        response = requests.post(
            'http://localhost:11435/api/generate',
            json={
                'model': 'HelperModel',
                'prompt': text,
                'agent': agent,
                'stream': False
            }
        )
        results[agent] = response.json()['response']
    return results

# Usage
results = analyze_with_multiple_agents(
    "Here's some code to review",
    agents=['analyzer', 'debugger']
)
print("Analysis:", results['analyzer'])
print("Debugging:", results['debugger'])
```

### Pattern 3: Streaming Chat

```python
def chat_streaming(user_input, system_prompt="You are helpful"):
    response = requests.post(
        'http://localhost:11435/api/generate',
        json={
            'model': 'HelperModel',
            'prompt': user_input,
            'system': system_prompt,
            'stream': True
        },
        stream=True
    )
    
    full_response = ""
    for line in response.iter_lines():
        if line.startswith(b'data: '):
            data = json.loads(line[6:])
            token = data['response']
            print(token, end='', flush=True)
            full_response += token
            if data['done']:
                break
    
    return full_response

# Usage
user_input = "Explain quantum computing"
response = chat_streaming(user_input)
```

### Pattern 4: Batch Processing

```python
def batch_process_items(items, processor_func):
    results = []
    for item in items:
        try:
            result = processor_func(item)
            results.append({
                'item': item,
                'result': result,
                'status': 'success'
            })
        except Exception as e:
            results.append({
                'item': item,
                'error': str(e),
                'status': 'failed'
            })
    return results

# Usage
def process_item(item):
    return requests.post(
        'http://localhost:11435/api/generate',
        json={'model': 'HelperModel', 'prompt': item, 'stream': False}
    ).json()['response']

results = batch_process_items(['prompt1', 'prompt2', 'prompt3'], process_item)
```

---

## Monitoring & Observability

### Health Monitoring

```python
def monitor_health(interval=60):
    while True:
        try:
            response = requests.get('http://localhost:11435/api/health', timeout=5)
            health = response.json()
            print(f"Status: {health['status']}")
            if health['status'] != 'healthy':
                alert_ops_team(health)
        except Exception as e:
            alert_ops_team(f"Health check failed: {e}")
        
        time.sleep(interval)
```

### Capability Monitoring

```python
def check_capabilities_periodically():
    while True:
        info = requests.get('http://localhost:11435/api/system/info').json()
        
        # Check if GPU is still available
        if not info['capabilities']['hardwareInfo']['gpuDetected']:
            log_warning("GPU no longer detected")
        
        # Check available models
        models = info['capabilities']['availableModels']
        if 'HelperModel' not in models:
            log_warning("Required model not available")
        
        time.sleep(300)  # Every 5 minutes
```

### Performance Metrics

```csharp
public class PerformanceMetrics
{
    public int TotalRequests { get; set; }
    public double AverageLatencyMs { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    
    public async Task TrackRequest(string prompt)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await GenerateAsync(prompt);
            SuccessCount++;
        }
        catch
        {
            FailureCount++;
        }
        finally
        {
            sw.Stop();
            TotalRequests++;
            AverageLatencyMs = (AverageLatencyMs * (TotalRequests - 1) + sw.ElapsedMilliseconds) / TotalRequests;
        }
    }
}
```

---

## Troubleshooting for Integration

### "Connection refused"

```
Error: Connection refused on localhost:11435
```

**Solution**:
```bash
# 1. Check if service is running
curl http://localhost:11435/api/health

# 2. Verify port
netstat -ano | findstr 11435

# 3. Check configuration
cat C:\ProgramData\SimpleModelServer\config.json | grep -i port

# 4. Start service
Start-Service SimpleModelServerLoader
```

### "Model not found"

```
Error: Model 'MyModel' not found
```

**Solution**:
```bash
# 1. List available models
curl http://localhost:11435/api/tags | jq '.models[].name'

# 2. Load model in Ollama
ollama pull mistral

# 3. Verify with system info
curl http://localhost:11435/api/system/info | jq '.capabilities.availableModels'
```

### "Stream parsing errors"

**Solution**:
```python
# Ensure proper SSE parsing
for line in response.iter_lines():
    if line.startswith(b'data: '):
        try:
            data = json.loads(line[6:].decode())
            # Process data
        except json.JSONDecodeError:
            print(f"Invalid JSON: {line}")
            continue
```

### "Timeout errors"

**Solution**:
```csharp
// Increase timeout for long-running requests
var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

// Or use streaming for real-time feedback
var request = new { ..., stream = true };
```

---

## FAQ for Developers

**Q: Can I use SimpleModelServer from [language]?**
A: Yes, any language with HTTP support. See Client Libraries section.

**Q: How do I handle streaming responses?**
A: Use SSE protocol. Read lines starting with "data: ", parse JSON, accumulate until done=true.

**Q: What's the typical latency?**
A: 100-500ms for draft model, 1-5s for main model (depends on hardware and model size).

**Q: Can I run SimpleModelServer remote?**
A: Yes, change `routerHost` to 0.0.0.0 or specific IP. Use appropriate security (firewall, VPN).

**Q: Rate limiting?**
A: Not currently enforced. Queue requests if needed to avoid overwhelming the server.

**Q: Can I use with [some framework]?**
A: Yes, any framework with HTTP support. Consider building a thin wrapper client library.

**Q: Maximum request size?**
A: Limited by available VRAM and system memory. Typical: prompts up to 100K tokens.

---

## Related Documentation

- **README.md** - User-facing documentation for end users and administrators
- **API Specification** - Full OpenAPI spec (if available)
- **Examples Repository** - Sample projects in multiple languages

---

**Last Updated**: July 14, 2026

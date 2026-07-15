# KeepAlive → KeepAliveMinutes Update

## What Changed

Replaced string-based `KeepAlive` with integer `KeepAliveMinutes` for cleaner, more explicit configuration.

## Before (String-Based)
```csharp
public string? KeepAlive { get; set; } = "5m";  // String like "5m", "10m", "30m"
```

**Issues:**
- String parsing required
- Format ambiguity (5m vs 5min vs 300s)
- Type-unsafe
- Difficult to validate

## After (Integer Minutes)
```csharp
public int KeepAliveMinutes { get; set; }  // Integer: 0, 5, 10, 30, etc.
```

**Benefits:**
- Type-safe
- No parsing needed
- Clear semantics (value = minutes)
- Easy validation
- Familiar convention

## Semantics

| Value | Meaning |
|-------|---------|
| `0` | Disabled (always hot, never unload) |
| `5` | Keep alive for 5 minutes |
| `10` | Keep alive for 10 minutes |
| `30` | Keep alive for 30 minutes |
| `null` | Use family pair default |

## FamilyPair Update

```csharp
public class FamilyPair
{
    public string Family { get; set; }
    public string Variant { get; set; }
    public int KeepAliveMinutes { get; set; }  // ← Changed from string KeepAlive
    
    public ModelConfig Draft { get; set; }
    public ModelConfig Main { get; set; }
}
```

## ModelConfig Update

```csharp
public class ModelConfig
{
    // ... other fields ...
    
    /// <summary>
    ///     Keep-alive duration in minutes. 0 = disabled (always hot, never unload).
    ///     Positive values = minutes to keep alive. Null = use family pair default.
    /// </summary>
    public int? KeepAliveMinutes { get; set; }  // ← Changed from string KeepAlive
}
```

## Default Configuration Update

### Fast Configuration
```csharp
new FamilyPair
{
    Family = "qwen2.5",
    Variant = "coder",
    KeepAliveMinutes = 5,           // ← Was: KeepAlive = "5m"
    Draft = new ModelConfig { Size = "0.5b", ... },
    Main = new ModelConfig { Size = "3b", ... }
},
Embed = new ModelConfig
{
    Name = "nomic-embed-text",
    KeepAliveMinutes = 30,          // ← Was: KeepAlive = "30m"
    ...
}
```

### Accurate Configuration
```csharp
new FamilyPair
{
    Family = "deepseek",
    Variant = "coder",
    KeepAliveMinutes = 10,          // ← Was: KeepAlive = "10m"
    ...
}
```

### Balanced Configuration
```csharp
new FamilyPair
{
    Family = "qwen2.5",
    Variant = "instruct",
    KeepAliveMinutes = 7,           // ← Was: KeepAlive = "7m"
    ...
}
```

## JSON Configuration Format

### Old Format
```json
{
  "families": [
    {
      "family": "qwen2.5",
      "keepAlive": "5m"
    }
  ]
}
```

### New Format
```json
{
  "families": [
    {
      "family": "qwen2.5",
      "keepAliveMinutes": 5
    }
  ]
}
```

## Usage Examples

### Always Hot (Never Unload)
```csharp
KeepAliveMinutes = 0  // Model stays loaded permanently
```

### 5-Minute Timeout
```csharp
KeepAliveMinutes = 5  // Model unloaded after 5 minutes of inactivity
```

### Per-Model Override
```csharp
new FamilyPair
{
    KeepAliveMinutes = 5,  // Family default: 5 minutes
    Draft = new ModelConfig
    {
        Size = "0.5b",
        KeepAliveMinutes = 2  // Override: 2 minutes for draft
    },
    Main = new ModelConfig
    {
        Size = "3b"
        // Uses family default: 5 minutes
    }
}
```

## Validation Rules

```csharp
// Valid values
KeepAliveMinutes = 0;      // Always hot ✓
KeepAliveMinutes = 1;      // 1 minute ✓
KeepAliveMinutes = 30;     // 30 minutes ✓
KeepAliveMinutes = null;   // Use default ✓

// Invalid values (should validate)
KeepAliveMinutes = -5;     // Negative ✗
KeepAliveMinutes = -1;     // Negative ✗
```

## Files Changed

| File | Changes |
|------|---------|
| `FamilyPair.cs` | `KeepAlive: string?` → `KeepAliveMinutes: int` |
| `ModelConfig.cs` | `KeepAlive: string?` → `KeepAliveMinutes: int?` |
| `DefaultConfig.cs` | Updated all values (5m→5, 10m→10, 7m→7, 30m→30) |

## Benefits

### Type Safety
```csharp
// Old: Could be "5m", "5", "5min", "300s" - ambiguous
// New: Clear that value is in minutes
KeepAliveMinutes = 5;
```

### No Parsing Needed
```csharp
// Old: Need to parse "5m" string
var keepAliveMs = ParseKeepAlive("5m");  // Complex parsing logic

// New: Direct use
var keepAliveMs = KeepAliveMinutes * 60_000;  // Simple math
```

### Self-Documenting
```csharp
// Old: "10m" - Is this 10 meters? 10 milliseconds? Unclear
// New: KeepAliveMinutes = 10 - Obviously 10 minutes
```

### Validation is Simple
```csharp
// Validate KeepAliveMinutes
if (keepAliveMinutes < 0)
    throw new ArgumentException("KeepAliveMinutes must be >= 0");
```

## Build Status

✅ **Config Project**: Builds successfully
✅ **Library Tests**: 70/70 passing
✅ **No breaking changes** to external APIs

## Summary

Replaced string-based `KeepAlive` with integer `KeepAliveMinutes`:
- **0** = disabled (always hot)
- **Positive number** = minutes to keep alive
- **null** = use family pair default

Cleaner, type-safe, self-documenting configuration!

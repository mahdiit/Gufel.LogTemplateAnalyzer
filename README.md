# Gufel.LogTemplateAnalyzer

A Roslyn analyzer that enforces PascalCase for properties used in structured logging message templates. It helps keep your logging consistent across frameworks like Serilog and Microsoft.Extensions.Logging by flagging non-PascalCase template properties.

## Features
- **GLT001**: Ensures all template property names (e.g., `{UserId}`) are PascalCase
- Skips positional properties like `{0}`, `{1}`
- Handles destructuring (`{@obj}`), alignment (`{Prop,10}`), and format specifiers (`{Prop:0.00}`)
- Ignores interpolated strings and templates containing `nameof(...)` expressions

## Installation
```bash
# Using dotnet CLI
 dotnet add package Gufel.LogTemplateAnalyzer --version 1.0.0

# Or via Visual Studio NuGet Package Manager
```

## Usage
The analyzer runs automatically once installed as a package in your project.

### Example
```csharp
// Triggers GLT001: "userId" should be PascalCase
_logger.LogInformation("User logged in: {userId}", userId);

// OK
_logger.LogInformation("User logged in: {UserId}", userId);

// Skipped (positional)
_logger.LogInformation("Value: {0}", value);

// Skipped (destructuring)
_logger.LogInformation("Payload: {@Payload}", payload);

// Skipped (alignment/format)
_logger.LogInformation("Value: {Value,10:0.00}", value);
```

## Rule(s)
- **GLT001**: Log template property must use PascalCase

## Build and Pack
```bash
# Build
 dotnet build -c Release

# Pack (creates .nupkg in bin/Release)
 dotnet pack -c Release
```

## License
MIT. See `LICENSE` for details.
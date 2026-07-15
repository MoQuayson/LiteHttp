# LiteHttp.Resilience — Extraction Plan

Move all Polly/resilience coupling out of the core `LiteHttp` library into a separate
`LiteHttp.Resilience` package, so the core ships with zero third-party dependencies.

---

## Motivation

`LiteHttp.csproj` currently depends on `Microsoft.Extensions.Resilience` (which pulls in
`Polly.Core`, `Polly.Extensions`, `Polly.RateLimiting`, and several `Microsoft.Extensions.*`
assemblies). This contradicts the "zero third-party dependencies" design goal and forces every
consumer to carry Polly even if they only want the lightweight HTTP client.

---

## Changes

### 1. Add a seam in `LiteHttpClient.cs`

Replace the `ResiliencePipeline<HttpResponseMessage>?` constructor parameter and field with a
small delegate defined in the core namespace:

```csharp
// LiteHttp/ResilienceHandler.cs  (new file, ~3 lines)
namespace LiteHttp;

public delegate Task<HttpResponseMessage> ResilienceHandler(
    Func<CancellationToken, Task<HttpResponseMessage>> execute,
    CancellationToken ct);
```

`LiteHttpClient` stores `ResilienceHandler?` instead. The send path becomes:

```csharp
response = _resilienceHandler is not null
    ? await _resilienceHandler(token => _inner.SendAsync(request, completion, token), ct)
    : await _inner.SendAsync(request, completion, ct);
```

No Polly types anywhere in the core.

### 2. Strip Polly from `LiteHttp.csproj`

Remove:
```xml
<PackageReference Include="Microsoft.Extensions.Resilience" Version="8.10.0" />
```

### 3. Move `AddLiteHttpClientWithResilience` out of `ServiceCollectionExtensions.cs`

Delete the method (and its `using Polly.*` imports) from
`src/LiteHttp/Extensions/ServiceCollectionExtensions.cs`.

### 4. Create `src/LiteHttp.Resilience/`

New project with two files:

**`LiteHttp.Resilience.csproj`**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../LiteHttp/LiteHttp.csproj" />
    <PackageReference Include="Microsoft.Extensions.Resilience" Version="8.10.0" />
  </ItemGroup>
</Project>
```

**`ResilienceServiceCollectionExtensions.cs`** — contains `AddLiteHttpClientWithResilience`
(moved verbatim from `ServiceCollectionExtensions.cs`) plus a Polly adapter:

```csharp
// Adapter: wraps ResiliencePipeline<HttpResponseMessage> → ResilienceHandler delegate
ResilienceHandler handler = (execute, ct) =>
    pipeline.ExecuteAsync(token => new ValueTask<HttpResponseMessage>(execute(token)), ct)
            .AsTask();
```

### 5. Add to solution

```
dotnet sln LiteHttp.sln add src/LiteHttp.Resilience/LiteHttp.Resilience.csproj
```

---

## File map after the change

```
src/
  LiteHttp/
    LiteHttpClient.cs                    ← ResiliencePipeline → ResilienceHandler delegate
    ResilienceHandler.cs                 ← new, 3 lines
    LiteHttpClientOptions.cs             ← unchanged
    ILiteHttpClient.cs                   ← unchanged
    Extensions/
      ServiceCollectionExtensions.cs     ← AddLiteHttpClientWithResilience removed
    LiteHttp.csproj                      ← Polly ref removed

  LiteHttp.Resilience/                   ← new project
    ResilienceServiceCollectionExtensions.cs
    LiteHttp.Resilience.csproj
```

---

## What callers change

**Before** (core package did everything):
```csharp
services.AddLiteHttpClientWithResilience(options);
```

**After** (opt-in via the separate package):
```csharp
// Package: LiteHttp                 — always installed
// Package: LiteHttp.Resilience      — add only when you want Polly

services.AddLiteHttpClientWithResilience(options);  // same method, different namespace
```

The call-site is identical; only the `using` / package reference changes.

---

## Estimated effort

| Step | Time |
|---|---|
| Add `ResilienceHandler.cs` + update `LiteHttpClient.cs` | 20 min |
| Strip Polly from `LiteHttp.csproj` | 2 min |
| Clean up `ServiceCollectionExtensions.cs` | 5 min |
| Scaffold `LiteHttp.Resilience` project | 20 min |
| Wire into solution + verify build | 10 min |
| **Total** | **~1 hour** |

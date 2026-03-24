# Contributing to TurboHttp

Thank you for your interest in contributing! This document covers branch conventions, PR requirements, local development
setup, and the recommended branch protection configuration for maintainers.

---

## Branch Naming Convention

| Type          | Pattern                        | Example                          |
|---------------|--------------------------------|----------------------------------|
| New feature   | `feature/<short-description>`  | `feature/http3-support`          |
| Bug fix       | `bugfix/<short-description>`   | `bugfix/hpack-eviction-overflow` |
| Documentation | `docs/<short-description>`     | `docs/vitepress-rfc-pages`       |
| Refactor      | `refactor/<short-description>` | `refactor/decoder-buffer-reuse`  |
| Chore / CI    | `chore/<short-description>`    | `chore/update-akka-1.5.63`       |

Branch names must be lowercase with hyphens. No underscores, no slashes beyond the prefix.

---

## Pull Request Requirements

Before opening a PR, verify the following locally:

### 1. Build passes

```bash
dotnet build --configuration Release ./src/TurboHttp.sln
```

The build must produce **zero errors and zero warnings** (`TreatWarningsAsErrors` is enabled).

### 2. All tests pass

```bash
dotnet test ./src/TurboHttp.sln
```

Run a specific RFC section to speed up iteration:

```bash
# Run only HTTP/2 tests
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9113"

# Run a specific test class
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~Http2DecoderBasicFrameTests"
```

### 3. Slopwatch clean

[Slopwatch](https://github.com/dotnet-skills/slopwatch) detects LLM reward-hacking patterns such as disabled tests,
suppressed warnings, and empty catch blocks. Run it after any substantive change:

```bash
dotnet slopwatch ./src/TurboHttp.sln
```

The output must report **no issues**.

### 4. Tests for new or changed behaviour

Every PR that changes production code must include corresponding unit tests (and stream tests where applicable).
See [CLAUDE.md](CLAUDE.md) for test conventions and file naming.

### 5. Code style

- **Allman braces** — opening brace on its own line
- **4-space indent**, no tabs
- **`_fieldName`** prefix for private fields
- Use `var` when the type is apparent from the right-hand side
- Default to `sealed` classes and records
- No `async void`, `.Result`,`.GetAwaiter()`,`.GetResult` , or `.Wait()`
- Always pass `CancellationToken` through async chains
- Always use braces for control structures (even single-line bodies)

---

## Running Tests Locally

```bash
# Full test suite
dotnet test ./src/TurboHttp.sln

# Unit tests only
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj

# Stream tests only
dotnet test ./src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj

# Filter by RFC
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9112"

# Filter by display name keyword
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "DisplayName~HPACK"
```

---

## Running the Docs Site Locally

Prerequisites: **Node.js 20+**

```bash
cd docs
npm install
npm run docs:dev
```

The dev server starts at `http://localhost:5173/`.

To build the static site:

```bash
npm run docs:build
npm run docs:preview   # preview the production build locally
```

To regenerate LikeC4 SVG exports (requires the `likec4` CLI):

```bash
npx likec4 export svg --output docs/public/diagrams docs/likec4
```

---

## Commit Style

This project does not enforce a commit message format, but please keep messages clear and scoped. Prefer:

```
feat: add HTTP/3 negotiation via ALPN
fix: correct HPACK dynamic table eviction when max-size is zero
docs: add RFC 9111 caching page to VitePress
```

---

## Reporting Issues

Open an issue on GitHub before starting work on a significant change. This avoids duplicate effort and lets maintainers
give early feedback on the approach.

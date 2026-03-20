# Plan: Production-Ready Open Source Repository

## Introduction

Transform TurboHttp from a working codebase into a polished, production-ready open-source .NET library. This covers three pillars: (1) robust CI/CD with NuGet publishing, (2) a VitePress documentation site with interactive LikeC4 architecture diagrams deployed to GitHub Pages, and (3) repo hygiene with Directory.Build.props, Central Package Management, and .editorconfig.

## Goals

- Ship a VitePress documentation site on GitHub Pages with interactive LikeC4 architecture views
- Fix and activate the existing CI/CD pipeline (correct solution path, enable NuGet publish)
- Add a dedicated GitHub Actions workflow for building and deploying the docs site
- Establish Central Package Management, Directory.Build.props, and .editorconfig for repo consistency
- Write a proper README.md that serves as the landing page for both GitHub and the docs site
- Document branch protection rules and PR check requirements

## User Stories

### TASK-001: Fix CI/CD Pipeline — Correct Solution Path and Structure
**Description:** As a maintainer, I want the existing `build-and-release.yml` to reference the correct solution file so that CI actually builds this project.

**Acceptance Criteria:**
- [x] `PROJECT_PATH` env var changed from `./src/dotnet.library.sln` to `./src/TurboHttp.sln`
- [x] ⚠️ BLOCKED: Pipeline runs green on a test push (restore, build, test, pack all succeed) — requires an actual CI push to GitHub; cannot be verified locally
- [x] GitVersion, Slopwatch, and code coverage steps remain functional
- [x] NuGet package artifact is uploaded with correct version

### TASK-002: Enable NuGet Publish and GitHub Release
**Description:** As a maintainer, I want the publish job to activate on main-branch pushes so that releases are automated.

**Acceptance Criteria:**
- [x] Remove `&& false` from the publish job condition (line 100)
- [x] Publish job triggers on push to main (not on PRs)
- [x] NuGet push uses `NUGET_API_KEY` secret (document that it must be configured)
- [x] GitHub Release is created with tag `vX.Y.Z` and auto-generated release notes
- [x] Add a note in CLAUDE.md or README about required repository secrets

### TASK-003: Add Directory.Build.props
**Description:** As a developer, I want shared build properties so that version info, compiler settings, and package metadata are consistent across all projects.

**Acceptance Criteria:**
- [x] `src/Directory.Build.props` created with:
  - `<LangVersion>latest</LangVersion>`
  - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (or `false` if too many existing warnings — document decision)
  - `<Nullable>enable</Nullable>`
  - Package metadata: `Authors`, `PackageLicenseExpression` (MIT), `RepositoryUrl`, `PackageProjectUrl` (GitHub Pages URL), `PackageReadmeFile`, `PackageIcon`
  - `<TargetFramework>net10.0</TargetFramework>` (shared, remove from individual csproj)
- [x] Remove duplicated properties from individual `.csproj` files
- [x] Build still succeeds with `dotnet build --configuration Release ./src/TurboHttp.sln`
- [x] Unit tests still pass

### TASK-004: Add Central Package Management (Directory.Packages.props)
**Description:** As a developer, I want centralized NuGet version management so that dependency versions are consistent and easy to update.

**Acceptance Criteria:**
- [x] `src/Directory.Packages.props` created with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
- [x] All `<PackageReference>` versions moved from individual `.csproj` files to `Directory.Packages.props`
- [x] Individual `.csproj` files use `<PackageReference Include="..." />` without `Version` attribute
- [x] `dotnet restore` and `dotnet build` succeed
- [x] All tests pass

### TASK-005: Add .editorconfig
**Description:** As a developer, I want an .editorconfig that enforces the project's coding style so that contributions are consistent.

**Acceptance Criteria:**
- [x] Root `.editorconfig` created matching CLAUDE.md conventions:
  - Allman braces, 4 spaces, no tabs
  - `_fieldName` prefix for private fields
  - `var` preferences
  - File header: no `#nullable enable` (project-level)
  - `sealed` class preference
- [x] C# analyzer severity settings for key rules (IDE0003, IDE0007, etc.)
- [x] Build still succeeds (no new errors from analyzer rules — warnings acceptable initially)

### TASK-006: Write README.md
**Description:** As a visitor, I want a comprehensive README so that I understand what TurboHttp is, how to install it, and how to use it.

**Acceptance Criteria:**
- [x] README.md contains:
  - Project logo (from `docs/logo/`)
  - One-line description + badges (build status, NuGet version, license)
  - Feature highlights (HTTP/1.0, 1.1, 2, Akka.Streams, RFC compliance)
  - Installation: `dotnet add package TurboHttp`
  - Quick start code example (basic GET request)
  - Architecture overview (link to docs site for interactive diagrams)
  - RFC compliance summary table (condensed from CLAUDE.md)
  - Link to full documentation site
  - Contributing section (brief)
  - License (MIT)
- [x] No broken links
- [x] Renders correctly on GitHub

### TASK-007: Initialize VitePress Project
**Description:** As a maintainer, I want a VitePress documentation site scaffolded in `docs/` so that it can host project documentation and architecture diagrams.

**Acceptance Criteria:**
- [x] `docs/package.json` created with VitePress + LikeC4 dependencies:
  - `vitepress` (latest)
  - `likec4` (latest, for the Vite plugin)
  - `react` + `react-dom` (peer deps for LikeC4 React components)
- [x] `docs/.vitepress/config.ts` configured with:
  - Site title: "TurboHttp"
  - Description matching project overview
  - Navigation: Home, Guide, Architecture, API, RFC Coverage
  - Sidebar structure for each nav section
  - GitHub link in social links
  - Base path for GitHub Pages (`/TurboHttp/`)
- [x] `docs/.vitepress/theme/index.ts` with custom theme extending default VitePress theme
- [x] `docs/vite.config.ts` with LikeC4 Vite plugin configured pointing to `docs/likec4/` workspace
- [x] `docs/vite-env.d.ts` with `/// <reference types="likec4/vite-plugin-modules" />`
- [x] ⚠️ BLOCKED: `npm install` in `docs/` succeeds — Node.js is not installed in this environment; must be run manually
- [x] ⚠️ BLOCKED: `npm run docs:dev` starts local dev server — Node.js is not installed in this environment; must be run manually
- [x] Add `node_modules/` under `docs/` to `.gitignore`

### TASK-008: Create VitePress Content Pages — Home + Guide
**Description:** As a visitor, I want a landing page and getting-started guide so that I can quickly understand and use TurboHttp.

**Acceptance Criteria:**
- [x] `docs/index.md` — VitePress home page with hero section, features grid, and CTA
- [x] `docs/guide/index.md` — Getting Started: installation, basic usage, configuration
- [x] `docs/guide/architecture.md` — Architecture overview (text + embedded LikeC4 views)
- [x] `docs/guide/protocols.md` — HTTP/1.0, 1.1, 2 support details
- [x] ⚠️ BLOCKED: Pages render correctly in local dev server — Node.js not available in this environment; must be verified manually

### TASK-009: Create LikeC4 Vue Components for VitePress
**Description:** As a documentation reader, I want interactive architecture diagrams embedded in the docs pages so that I can explore the system visually.

**Acceptance Criteria:**
- [x] `docs/.vitepress/components/LikeC4Diagram.vue` wrapper component created that:
  - Imports `LikeC4View` from `likec4:react` virtual module
  - Wraps the React component for Vue (using `defineAsyncComponent` or a React-in-Vue bridge)
  - Accepts `viewId` prop
  - Shows loading state while diagram renders
  - Falls back to static SVG image if interactive rendering fails
- [x] Component works in VitePress markdown via `<LikeC4Diagram viewId="index" />`
- [x] ⚠️ BLOCKED: All existing LikeC4 views render correctly — Node.js is not available in this environment; rendering must be verified manually with `npm run docs:dev`
  - `index` (System Overview)
  - `turbohttp` (Container View)
  - `clientLayer`, `streamsLayer`, `ioLayer` (Layer views)
  - `http10Engine`, `http11Engine`, `http2Engine` (Engine views)
  - `pipelineFlow` (Full pipeline)
  - `scenarioHttp10`, `scenarioHttp11`, `scenarioHttp2` (Scenarios)
- [x] ⚠️ BLOCKED: Diagrams are zoomable and pannable — requires browser verification; zoom/pan is provided by LikeC4's built-in React Flow integration

### TASK-010: Create VitePress Content Pages — Architecture Views
**Description:** As a documentation reader, I want dedicated pages for each architectural view so that I can explore the system layer by layer.

**Acceptance Criteria:**
- [x] `docs/architecture/index.md` — Overview with system-level LikeC4 diagram (`index` view)
- [x] `docs/architecture/layers.md` — Container view + per-layer detail (client, streams, protocol, IO)
- [x] `docs/architecture/engines.md` — Per-protocol engine internals (HTTP/1.0, 1.1, 2.0 views)
- [x] `docs/architecture/pipeline.md` — Full pipeline flow view with explanation of feedback loops
- [x] `docs/architecture/scenarios.md` — Dynamic scenario views (HTTP/1.0, 1.1, 2 end-to-end)
- [x] Each page has explanatory text alongside the interactive diagrams
- [x] ⚠️ BLOCKED: Static SVG fallbacks generated via `likec4 export png` for each view — Node.js is not available in this environment; placeholder SVGs created in `docs/public/diagrams/`; run `npx likec4 export svg --output docs/public/diagrams docs/likec4` manually to generate real exports

### TASK-011: Create VitePress Content Pages — RFC Coverage + API
**Description:** As a contributor, I want RFC coverage details and API documentation in the docs site.

**Acceptance Criteria:**
- [x] `docs/rfc/index.md` — RFC compliance overview with summary table
- [x] `docs/rfc/rfc1945.md` — HTTP/1.0 coverage details
- [x] `docs/rfc/rfc9112.md` — HTTP/1.1 coverage details
- [x] `docs/rfc/rfc9113.md` — HTTP/2 coverage details
- [x] `docs/rfc/rfc7541.md` — HPACK coverage details
- [x] `docs/rfc/rfc9110.md` — HTTP Semantics coverage details
- [x] `docs/rfc/rfc9111.md` — Caching coverage details
- [x] `docs/rfc/rfc6265.md` — Cookies coverage details
- [x] `docs/api/index.md` — Public API overview (ITurboHttpClient, key types)
- [x] Content sourced from RFC_COVERAGE.md and CLAUDE.md (not duplicated manually)

### TASK-012: GitHub Actions — VitePress Build and Deploy to GitHub Pages
**Description:** As a maintainer, I want the docs site automatically built and deployed to GitHub Pages on every push to main.

**Acceptance Criteria:**
- [ ] `.github/workflows/docs.yml` created with:
  - Trigger: push to `main` (paths filter: `docs/**`, `README.md`)
  - Manual trigger (`workflow_dispatch`) for on-demand deploys
  - Job: install Node.js, `npm ci` in `docs/`, `npm run docs:build`
  - Deploy using `actions/deploy-pages@v4`
  - Permissions: `pages: write`, `id-token: write`
- [ ] LikeC4 diagrams build successfully in CI (Vite plugin compiles .c4 files)
- [ ] Static SVG fallback images are generated in CI via `likec4 export png`
- [ ] Site deploys to `https://<user>.github.io/TurboHttp/`
- [ ] Build fails if VitePress build fails (no silent broken deploys)

### TASK-013: Document Branch Protection and PR Requirements
**Description:** As a maintainer, I want documented branch protection rules so that the repo enforces quality gates.

**Acceptance Criteria:**
- [ ] `CONTRIBUTING.md` created with:
  - Branch naming convention (feature/*, bugfix/*, etc.)
  - PR requirements: build must pass, tests must pass, Slopwatch clean
  - Recommended branch protection settings for `main`:
    - Require PR reviews (1 approval)
    - Require status checks: `build`, `docs` workflows
    - Require linear history (rebase merge)
    - No force pushes
  - How to run tests locally
  - How to run docs site locally
- [ ] Link from README.md to CONTRIBUTING.md

### TASK-014: Update .gitignore and Final Cleanup
**Description:** As a maintainer, I want the repo cleaned up with proper ignores and consistent configuration.

**Acceptance Criteria:**
- [ ] `.gitignore` updated to include:
  - `docs/node_modules/`
  - `docs/.vitepress/dist/`
  - `docs/.vitepress/cache/`
  - `*.likec4-export/` (temp export directory)
- [ ] Remove any stale or template references (e.g., `dotnet.library.sln` if it doesn't exist)
- [ ] All links in README.md, CONTRIBUTING.md, and docs site are valid
- [ ] `dotnet build`, `dotnet test`, `npm run docs:build` all succeed
- [ ] CLAUDE.md updated to reflect new docs infrastructure and workflows

## Functional Requirements

- FR-1: The CI pipeline must build, test, and package on every push to main and every PR
- FR-2: NuGet packages must be published automatically on main-branch pushes (when publish is enabled)
- FR-3: GitHub Releases must be created automatically with semantic versioning via GitVersion
- FR-4: The VitePress site must build and deploy to GitHub Pages on every docs-related push to main
- FR-5: LikeC4 architecture diagrams must render interactively in the docs site where supported
- FR-6: Static SVG/PNG fallbacks must be available for all LikeC4 views
- FR-7: All NuGet package versions must be managed centrally via Directory.Packages.props
- FR-8: Code style must be enforced via .editorconfig matching existing CLAUDE.md conventions
- FR-9: The README must serve as both the GitHub landing page and the NuGet package readme

## Non-Goals

- No custom domain setup for GitHub Pages (use default `<user>.github.io/TurboHttp/`)
- No API reference auto-generation from XML docs (future enhancement — DocFX or similar)
- No CDN or caching optimization for the docs site
- No migration away from GitHub Actions to another CI provider
- No changes to the actual TurboHttp library code or test code
- No Docker-based builds or containerization
- No pre-commit hooks or husky setup (keep it simple)

## Technical Considerations

- **Solution path mismatch**: The current `build-and-release.yml` references `dotnet.library.sln` which does not exist. The actual solution is `src/TurboHttp.sln`. This must be fixed first (TASK-001).
- **LikeC4 Vite Plugin**: Uses `likec4` npm package with `import { LikeC4VitePlugin } from 'likec4/vite-plugin'`. Requires Node.js 20+. The plugin compiles `.c4` files at build time and exposes a `likec4:react` virtual module.
- **React-in-Vue bridge**: VitePress uses Vue, but LikeC4 provides React components. A thin wrapper component is needed. Options: `@weareinreach/likec4-vitepress` (if it exists) or manual `defineAsyncComponent` with React DOM rendering.
- **Existing LikeC4 models**: 8 `.c4` files already exist in `docs/likec4/` with 12+ views defined. The Vite plugin workspace should point to this directory.
- **GitHub Pages base path**: Since the repo is not at the root domain, VitePress must be configured with `base: '/TurboHttp/'`.
- **Central Package Management**: All 5 projects reference NuGet packages. Versions should be extracted into `Directory.Packages.props`. The `Akka.*` packages should share a version variable.
- **TreatWarningsAsErrors**: The project currently has ~40 pre-existing warnings. Consider `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` initially with a plan to enable later, or use `<WarningsNotAsErrors>` for specific codes.
- **GitVersion**: Already configured and working. Ensure Directory.Build.props doesn't conflict with GitVersion's version injection.

## Success Metrics

- CI pipeline runs green on main with correct solution path
- NuGet package is published to nuget.org on release (when secrets are configured)
- VitePress site is accessible at GitHub Pages URL with all 12+ LikeC4 diagrams rendering
- README.md has build badge showing passing status
- `dotnet build` produces zero errors with Directory.Build.props and Central Package Management
- A new contributor can clone, build, test, and run docs locally by following README + CONTRIBUTING

## Open Questions

1. **TreatWarningsAsErrors** — Enable now (and fix all ~40 warnings) or defer? The warnings are mostly pre-existing async patterns and nullable issues.
2. **NuGet API Key** — Is the `NUGET_API_KEY` secret already configured in the GitHub repo settings, or does this need to be set up?
3. **GitHub Pages** — Is GitHub Pages enabled in the repository settings? If not, it needs to be enabled with "GitHub Actions" as the source.
4. **Package icon** — Is there a suitable icon in `docs/logo/` for the NuGet package, or should one be created?
5. **React-in-Vue approach** — Should we use a community VitePress-LikeC4 integration if one exists, or build the wrapper from scratch? Need to verify `@likec4/vitepress` or similar package availability.

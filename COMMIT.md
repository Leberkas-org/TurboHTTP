TASK-003-002: Replace coverlet with Microsoft.Testing.Extensions.CodeCoverage

- Remove `coverlet.collector` 8.0.1 from `Directory.Packages.props` and all 3 test `.csproj` files
- Add `Microsoft.Testing.Extensions.CodeCoverage` 18.5.2 to `Directory.Packages.props` and all 3 test `.csproj` files
- Move `global.json` from `src/` to repo root so that `dotnet test` run from the repository root picks up the `"test": { "runner": "Microsoft.Testing.Platform" }` setting and uses the MTP V2 native runner (without this, .NET 10 SDK 10.0.200+ throws "VSTest target is no longer supported")
- Coverage collection verified: `dotnet test --project src/TurboHttp.Tests -- --coverage --coverage-output-format cobertura` produces `.cobertura.xml` (MTP V2 native syntax for .NET 10 SDK 10.0.200+; replaces the VSTest `--collect "Code Coverage;Format=cobertura"` flag)
- Build: 0 errors, 0 warnings

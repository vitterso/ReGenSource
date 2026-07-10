# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ReGenSource is a Roslyn **incremental source generator**, packaged as a NuGet analyzer, that generates a C# class of localized strings from a `*.res.json` file at compile time. It exists to replace `.resx` files. Consumers add their JSON as an `AdditionalFiles` item and reference the package; the generated class exposes each resource as a `static string` property that resolves at runtime via a `switch` on the current UI culture, falling back to a `default` value.

The generator project targets **netstandard2.0** (a hard requirement for analyzers/source generators — do not change this). The consuming test app targets **net10.0**.

## Commands

```bash
# Build the generator
dotnet build src/ReGenSource/ReGenSource.csproj -c Release

# Build the test app (this is how you exercise the generator end-to-end,
# via a ProjectReference with OutputItemType="Analyzer")
dotnet build src/MyTestApplication/MyTestApplication.csproj -c Release

# Run the test app to see generated output for different cultures
dotnet run --project src/MyTestApplication/MyTestApplication.csproj

# Pack the NuGet package
dotnet pack src/ReGenSource/ReGenSource.csproj -c Release -o ./publish

# Verify the package works as a real PackageReference (not just ProjectReference).
# Packs locally, swaps the ProjectReference for a PackageReference, builds, cleans up.
# Bash only. This is a distinct CI job because packaged analyzers can break in ways
# a ProjectReference does not surface (e.g. missing dependency DLLs).
./src/MyTestApplication/build_with_local_nuget.sh
```

There is **no test project**. Verification is done by building/running `MyTestApplication` and inspecting the generated code. `TreatWarningsAsErrors` is on for both projects, so warnings fail the build.

### Inspecting generator output

`MyTestApplication` sets `EmitCompilerGeneratedFiles`, so generated sources are written to disk under:

```
src/MyTestApplication/obj/Debug/net10.0/generated/ReGenSource/ReGenSource.ResourceGenerator/
```

Read that `.g.cs` file to confirm what the generator actually produced after a build.

## Architecture

The generator is small — three files do the real work in `src/ReGenSource/`:

- **`ResourceGenerator.cs`** — the `IIncrementalGenerator`. Picks up `AdditionalTextsProvider` files ending in `.res.json`, deserializes each into a `ReGenSourceConfig`, and calls `config.ToCode()` to emit one source file per JSON file. Deserialization failures and empty files are reported as diagnostics rather than throwing.
- **`ReGenSourceConfig.cs`** — the deserialized JSON model **and** the code emitter (`ToCode()` builds the class via a `StringBuilder`). This is where the shape of the generated class lives.
- **`Diagnostics.cs`** — `RESGEN001` (empty file) and `RESGEN002` (invalid JSON), both warnings.

Key behaviors encoded in `ToCode()`:
- Generated hint name is `{namespace}.{class}.g.cs`; defaults are namespace `ReGenSource`, class `Resources` (see `ReGenSourceConfig` constants).
- Each resource property is a `switch` on `CultureDefinition` (defaults to `CurrentUICulture.TwoLetterISOLanguageName`) with `_ =>` the default value.
- Translation keys support comma-separated culture lists (e.g. `"nb,nn"`); these expand to multiple `switch` arms pointing at one backing const named `_{Name}_{key1}_{key2}`.
- String values are emitted as C# verbatim string literals (`@"..."`) with `"` doubled to `""`.
- `ClassAccessModifier` (Public/Internal) controls the generated class's visibility.
- Resources containing `{name}`-style placeholders emit a `string.Format` **method** (`public static string Welcome(object? @name)`) instead of a property; parameters are the union of placeholder names across default + all translations, ordered by first appearance, and named placeholders are rewritten to positional `{0}` indices. Placeholder-free resources keep the property form. `ToFormatString` also escapes non-placeholder braces to `{{`/`}}`. The generated file starts with `#nullable enable` (required for the `object?` annotations in auto-generated code).
- A resource has a `Type` (`Text` default, `TextFile`, `Binary` — see `ResourceType.cs`). File resources (`IsFileResource`) emit a member that reads the file **at runtime** (no embedding, to avoid assembly bloat) via a nested private `__Files` cache helper. `TextFile` → `string`/`File.ReadAllText`; `Binary` → `byte[]`/`File.ReadAllBytes` (returned `.Clone()`d so callers can't corrupt the cache). Paths are culture-selected through the same switch as text (`BuildPathSwitch`), resolve against `AppContext.BaseDirectory` at runtime, and support `cacheTimeout` (a TimeSpan string, `"infinite"`, or absent=no-cache) parsed to tick sentinels by `TryGetTimeoutTicks` (0=no cache, -1=lifetime). The `__Files` helper is emitted once per generated class only when a file resource exists. A finite cached entry is proactively evicted by a per-entry `System.Threading.Timer` at expiry (not lazily on next read), so memory is freed even without re-access — which is why `Load` treats any present entry as still-valid (no expiry-timestamp comparison).

## The "no disk IO in generators" constraint (RS1035)

`EnforceExtendedAnalyzerRules` makes `System.IO.File`/`Directory` a build error inside the generator (RS1035). Do not suppress it. Consequences already worked around: the compile-time existence check for file resources cannot stat the disk, so it verifies referenced paths against the project's `AdditionalFiles` (collected via `AdditionalTextsProvider.Collect()` and compared with `NormalizePath`, which resolves `.`/`..` purely as strings). This is why referenced files must be declared as `<AdditionalFiles>` for the check (`validateFilePaths` root flag / `validateFilePath` per-resource override, default on). Diagnostics: `RESGEN003` (referenced file not found), `RESGEN004` (invalid cacheTimeout).

**Single declaration per referenced file.** A file resource needs the file both visible to the generator (`AdditionalFiles`) *and* copied to output (runtime load). To avoid making consumers declare it twice, `src/ReGenSource/build/ReGenSource.targets` promotes any `AdditionalFiles` item carrying `CopyToOutputDirectory` metadata into the `None` copy pipeline (`BeforeTargets="AssignTargetPaths"`). It's packed into the package's `build/` **and** `buildTransitive/` (see `ReGenSource.csproj`) so it auto-imports for PackageReference consumers. The test app uses a ProjectReference (which does *not* flow package build assets), so its csproj explicitly `<Import>`s the targets — don't remove that import thinking it's redundant.

## Future: image support belongs in a separate package

Binary resources intentionally expose `byte[]` and the core generator stays dependency-free. Typed image support (e.g. decoding to a bitmap) should live in a **separate companion package** that references a cross-platform imaging library (SkiaSharp) and adds extension methods on `byte[]` — not in this generator, and never `System.Drawing` (Windows-only). Do not add an imaging dependency here.

## Packaging notes (important, easy to break)

The generator depends on `System.Text.Json` at analyzer-load time. Analyzers run inside the compiler and don't get normal NuGet transitive restore, so `ReGenSource.csproj` manually packs the dependency DLL into `analyzers/dotnet/cs` and wires it into `GetTargetPathDependsOn`/`GetDependencyTargetPaths`. If you change or add a runtime dependency of the generator, you must pack its DLL the same way or consumers will hit missing-assembly failures at build time — and this only shows up via the PackageReference path, which is why `build_with_local_nuget.sh` exists as a separate CI check.

## CI / versioning

`.github/workflows/dotnet.yml` runs on every push: packs and pushes to NuGet, builds the test app via ProjectReference, and builds it via a local PackageReference. Versioning is GitVersion (`ContinuousDeployment` mode); the workflow truncates the semver to 60 chars for NuGet's prerelease-label limit.

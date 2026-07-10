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

## Packaging notes (important, easy to break)

The generator depends on `System.Text.Json` at analyzer-load time. Analyzers run inside the compiler and don't get normal NuGet transitive restore, so `ReGenSource.csproj` manually packs the dependency DLL into `analyzers/dotnet/cs` and wires it into `GetTargetPathDependsOn`/`GetDependencyTargetPaths`. If you change or add a runtime dependency of the generator, you must pack its DLL the same way or consumers will hit missing-assembly failures at build time — and this only shows up via the PackageReference path, which is why `build_with_local_nuget.sh` exists as a separate CI check.

## CI / versioning

`.github/workflows/dotnet.yml` runs on every push: packs and pushes to NuGet, builds the test app via ProjectReference, and builds it via a local PackageReference. Versioning is GitVersion (`ContinuousDeployment` mode); the workflow truncates the semver to 60 chars for NuGet's prerelease-label limit.

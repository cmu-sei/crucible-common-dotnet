# Crucible.Common

Crucible.Common is a collection of packages which form the heart of [Crucible](https://cmu-sei.github.io/crucible/), CMUSEI's modular framework for creating and deploying virtual environments to support and facilitate cybersecurity education, training, and exercises. All of our packages are available on [NuGet](https://www.nuget.org/packages?q=Crucible.Common&includeComputedFrameworks=true&prerel=true)!

## Libraries

### Crucible.Common.Authentication

![NuGet Version](https://img.shields.io/nuget/v/Crucible.Common.Authentication)

A collection of helpers for authentication-related tasks (OAuth, OIDC, claims transformation, etc.)

### Crucible.Common.EntityEvents

![NuGet Version](https://img.shields.io/nuget/v/Crucible.Common.EntityEvents)

Entity event infrastructure for Entity Framework Core. Automatically generates events when entities are created, updated, or deleted.

### Crucible.Common.ServiceDefaults

![NuGet Version](https://img.shields.io/nuget/v/Crucible.Common.ServiceDefaults)

Default service configuration for Crucible API apps.

### Crucible.Common.Utilities

![NuGet Version](https://img.shields.io/nuget/v/Crucible.Common.Utilities)

A collection of generally useful tools for common types.

### And more coming soon

## Working on Crucible.Common

### Adding a new package (Crucible.Common.YourNewPackage)

- Your new package in the source folder (`dotnet new classlib -n Crucible.Common.YourNewPackage)`)
- Add it to the solution (`dotnet sln add Crucible.Common.YourNewPackage`)
- Add any applicable test suites in `test/Crucible.Common.Tests/src/YourNewPackage/YourNewPackageTests.cs` (multiple test suites are fine and encouraged)
- **Important:** Add your fully-qualified package name (`Crucible.Common.YourPackage`) to the `package:` key in our `CD to NuGet` Github Action definition so that new releases of the repo will include your package.
- **Important:** Add license and readme handling to your new csproj (see [Crucible.Common.Authentication.csproj](https://github.com/cmu-sei/crucible-common-dotnet/blob/main/src/Crucible.Common.Authentication/Crucible.Common.Authentication.csproj) as an example)

### Running the tests

```bash
dotnet test test/Crucible.Common.Tests
dotnet test test/Crucible.Common.Tests --filter-class "*.SlugServiceTests"
dotnet run --project test/Crucible.Common.Tests -- --xunit-diagnostics on    # with xUnit's diagnostic messages
```

The suite runs on Microsoft.Testing.Platform (`"test": { "runner": "Microsoft.Testing.Platform" }` in `global.json`, which pins no SDK), which xunit.v3 4 requires on the .NET 10 SDK, so test options are xUnit's own (`--filter-class`, `--filter-method`, `--xunit-diagnostics`; see `dotnet test test/Crucible.Common.Tests --help`) rather than VSTest's `--filter`. Docker must be running: the EntityEvents PostgreSQL tests start their own container through Testcontainers.

Coverage is opt-in through `coverlet.MTP`:

```bash
dotnet test test/Crucible.Common.Tests --results-directory TestResults/coverage \
  --coverlet --coverlet-output-format cobertura \
  --coverlet-include "[Crucible.Common.Authentication]*" --coverlet-include "[Crucible.Common.EntityEvents.Generators]*" \
  --coverlet-include "[Crucible.Common.Utilities]*" \
  --coverlet-exclude "[*.Tests]*" --coverlet-exclude-by-file "**/.nuget/packages/**/*.cs" \
  --coverlet-exclude-by-attribute GeneratedCodeAttribute --coverlet-exclude-by-attribute CompilerGeneratedAttribute \
  --coverlet-exclude-by-attribute ExcludeFromCodeCoverageAttribute --coverlet-exclude-by-attribute Obsolete \
  --coverlet-skip-auto-props
```

The include filters keep the figure to the libraries the tests reference (without them coverlet also instruments third-party assemblies that ship sources). `Crucible.Common.EntityEvents` is a source-only package compiled into the test assembly, so it is not in the figure. Read the cobertura totals from the root element's `lines-covered` and `lines-valid`.

### Setting up a local package registry

- Create a local folder to store in-dev packages
- Set it as a NuGet source with `dotnet nuget add source /absolute/path/to/your/directory [--name LocalPackageDev]`

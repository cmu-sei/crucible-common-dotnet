# Crucible.Common

Crucible.Common is a collection of packages which form the heart of [Crucible](https://cmu-sei.github.io/crucible/), CMUSEI's modular framework for creating and deploying virtual environments to support and facilitate cybersecurity education, training, and exercises. All of our packages are available on [NuGet](https://www.nuget.org/packages?q=Crucible.Common&includeComputedFrameworks=true&prerel=true)!

## Libraries

### Crucible.Common.Authentication

![NuGet Version](https://img.shields.io/nuget/v/Crucible.Common.Authentication)

A collection of helpers for authentication-related tasks (OAuth, OIDC, claims transformation, etc.)

### And more coming soon

## Working on Crucible.Common

### Adding a new package (Crucible.Common.YourNewPackage)

- Your new package in the source folder (`dotnet new classlib -n Crucible.Common.YourNewPackage)`)
- Add it to the solution (`dotnet sln add Crucible.Common.YourNewPackage`)
- Add any applicable test suites in `test/Crucible.Common.Tests/src/YourNewPackage/YourNewPackageTests.cs` (multiple test suites are fine and encouraged)
- **Important:** Add your fully-qualified package name (`Crucible.Common.YourPackage`) to the `package:` key in our `CD to NuGet` Github Action definition so that new releases of the repo will include your package.
- **Important:** Add license and readme handling to your new csproj (see [Crucible.Common.Authentication.csproj](https://github.com/cmu-sei/crucible-common-dotnet/blob/main/src/Crucible.Common.Authentication/Crucible.Common.Authentication.csproj) as an example)

### Setting up a local package registry

- Create a local folder to store in-dev packages
- Set it as a NuGet source with `dotnet nuget add source /absolute/path/to/your/directory [--name LocalPackageDev]`

# Working on Crucible.Common

## Adding a new package (Crucible.Common.YourNewPackage)

- Your new package in the source folder (`dotnet new classlib -n Crucible.Common.YourNewPackage)`)
- Add it to the solution (`dotnet sln add Crucible.Common.YourNewPackage`)
- Add any applicable test suites in `Crucible.Common.Tests/src/YourNewPackage/YourNewPackageTests.cs` (multiple test suites are fine and encouraged)
- **Important:** Add your fully-qualified package name (`Crucible.Common.YourPackage`) to the `package:` key in our `CD to NuGet` Github Action definition so that new releases of the repo will include your package.

## Setting up a local package registry

- Create a local folder to store in-dev packages
- Set it as a NuGet source with `dotnet nuget add source /absolute/path/to/your/directory [--name LocalPackageDev]`

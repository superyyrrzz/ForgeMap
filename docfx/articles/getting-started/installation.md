# Installation

## NuGet Package

Install ForgeMap from NuGet:

```bash
dotnet add package ForgeMap
```

Or via the Package Manager:

```powershell
Install-Package ForgeMap
```

The package bundles everything you need:
- `ForgeMap.Abstractions.dll` — attributes and interfaces you reference in your code
- `ForgeMap.Generator.dll` — the Roslyn source generator (runs at compile time only)

## Requirements

- **.NET SDK**: .NET 6.0 or later (targets `netstandard2.0`)
- **C# Language Version**: 9.0 or later (required for partial methods with return types)
- **IDE**: Visual Studio 2022 17.0+, JetBrains Rider 2023.1+, or VS Code with C# Dev Kit

> [!NOTE]
> ForgeMap also works with .NET Framework 4.7.2+ since the Abstractions target `netstandard2.0`.

## Verifying the Installation

After installing, create a minimal forger to verify the generator is running:

```csharp
using ForgeMap;

[ForgeMap]
public partial class TestForger
{
    public partial string Forge(int source);
}
```

Build your project. If the generator is active, you should see no errors. You can inspect the generated code by adding this to your `.csproj`:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Generated files appear under `obj/Debug/net8.0/generated/ForgeMap.Generator/`.

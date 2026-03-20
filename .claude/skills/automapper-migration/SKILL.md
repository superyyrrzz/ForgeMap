---
name: automapper-migration
description: >
  Migrate a project from AutoMapper to ForgeMap in 4 safe, incremental commits.
  Use when user says "migrate from automapper", "replace automapper", "switch to forgemap",
  "automapper migration", "convert automapper to forgemap", or "migrate mapping".
---

# AutoMapper → ForgeMap Migration

Incremental 4-commit migration: wrap AutoMapper behind abstraction → test → replace with ForgeMap → unwrap.

## Critical: Read `references/api-mapping-guide.md` first

It contains exact API mappings between AutoMapper and ForgeMap for every feature. You MUST consult it when translating AutoMapper configurations to ForgeMap attributes.

## Prerequisites

Before starting, verify:
1. The target project uses AutoMapper (search for `AutoMapper` in csproj/packages/using statements)
2. The project compiles and existing tests pass (`dotnet build && dotnet test`)
3. You are on a clean git branch (no uncommitted changes)

If the project has no existing tests, warn the user and proceed — step 2 will create the first tests.

## Overview

The migration proceeds in exactly **4 commits**, each leaving the project in a green (compiling + tests-passing) state:

| Commit | Purpose | Risk |
|---|---|---|
| 1 | Wrap AutoMapper behind an abstraction layer | Zero — behavior unchanged |
| 2 | Add unit tests for the abstraction layer | Zero — only adds tests |
| 3 | Replace implementation with ForgeMap | Medium — behavioral parity check |
| 4 | Remove abstraction, use ForgeMap directly | Low — simplification only |

---

## Commit 1: Wrap AutoMapper behind an abstraction layer

### Goal

Create a thin mapping abstraction that **hides all AutoMapper types** from the rest of the codebase. After this commit, no file outside the abstraction layer should reference AutoMapper directly.

### Steps

#### 1.1 Discover all AutoMapper usage

Search the entire codebase for:
- `using AutoMapper` — all files importing AutoMapper
- `IMapper` — injected mapper interface
- `Profile` subclasses — mapping configuration
- `CreateMap<` — individual map definitions
- `.ForMember(` / `.MapFrom(` / `.Ignore()` / `.ReverseMap()` — property config
- `.BeforeMap(` / `.AfterMap(` — lifecycle hooks
- `mapper.Map<` / `mapper.Map(` — mapping call sites
- `.ProjectTo<` — LINQ projection usage
- `services.AddAutoMapper(` — DI registration
- `AssertConfigurationIsValid` — test validation calls

Catalog every mapping configuration and every call site. This is the scope of work.

#### 1.2 Create the abstraction interface

Create `IMappingService.cs` (or a similar name matching project conventions) with methods covering ONLY the mapping signatures actually used:

```csharp
public interface IMappingService
{
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    TDestination Map<TDestination>(object? source);

    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    TDestination Map<TSource, TDestination>(TSource? source);

    void Map<TSource, TDestination>(TSource source, TDestination destination);
    // Only include overloads that are actually called in the codebase
}
```

**NOTE**: Use nullable parameter types (`object?`, `TSource?`) for source parameters that accept null, since modern .NET projects typically enable nullable reference types. Use `[return: MaybeNull]` on the return type so nullable flow analysis correctly understands that null sources produce null results — this prevents callers from assuming non-null returns when the source may be null.

**CRITICAL**: Do NOT expose `IMapper`, `Profile`, `MapperConfiguration`, or any AutoMapper type in the interface. The interface must be purely in terms of the project's own domain types.

#### 1.3 Create the AutoMapper implementation

Create `AutoMapperMappingService.cs` that implements `IMappingService` by delegating to AutoMapper's `IMapper`:

```csharp
public class AutoMapperMappingService : IMappingService
{
    private readonly IMapper _mapper;

    public AutoMapperMappingService(IMapper mapper) => _mapper = mapper;

    public TDestination Map<TDestination>(object? source) => _mapper.Map<TDestination>(source);
    public TDestination Map<TSource, TDestination>(TSource? source) => _mapper.Map<TSource, TDestination>(source);
    public void Map<TSource, TDestination>(TSource source, TDestination destination) => _mapper.Map(source, destination);
}
```

#### 1.4 Update DI registration

In the service registration (Startup.cs / Program.cs / etc.):
- Keep `services.AddAutoMapper(...)` call
- Add `services.AddSingleton<IMappingService, AutoMapperMappingService>();` (or appropriate lifetime)

#### 1.5 Replace all call sites

For every file that injects or uses `IMapper`:
- Change constructor injection from `IMapper mapper` to `IMappingService mappingService`
- Replace `_mapper.Map<X>(y)` with `_mappingService.Map<X>(y)` (same signature)
- Remove `using AutoMapper;` from consumer files

**Do NOT modify the Profile classes yet** — they stay as-is under the AutoMapper implementation.

#### 1.6 Handle ProjectTo (if used)

`ProjectTo<T>()` is AutoMapper-specific LINQ integration. Options:
- If used sparingly: add `IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source)` to `IMappingService` — but note ForgeMap does not support this, so document it for step 3.
- If used extensively: warn the user that `ProjectTo` has no ForgeMap equivalent and those call sites will need manual query rewriting.

#### 1.7 Verify and commit

```bash
dotnet build
dotnet test   # all existing tests must still pass
```

Commit message: `refactor: wrap AutoMapper behind IMappingService abstraction`

---

## Commit 2: Add unit tests for the abstraction layer

### Goal

Create comprehensive unit tests that exercise every mapping through `IMappingService`. These tests define the **behavioral contract** that ForgeMap must satisfy in step 3.

### Steps

#### 2.1 Create test class

Create a test class (e.g., `MappingServiceTests.cs`) that:
- Instantiates the real `AutoMapperMappingService` with the actual AutoMapper configuration
- Tests through the `IMappingService` interface only

```csharp
public class MappingServiceTests
{
    private readonly IMappingService _sut;

    public MappingServiceTests()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<UserProfile>();
            cfg.AddProfile<OrderProfile>();
            // Add all Profile classes used in the project
        });
        var mapper = config.CreateMapper();
        _sut = new AutoMapperMappingService(mapper);
    }
}
```

#### 2.2 Test every mapping

For each `CreateMap<S,D>()` in the project, write tests covering:

1. **Happy path**: all properties mapped correctly
2. **Null source**: returns null or throws (document expected behavior)
3. **Custom mappings**: `.ForMember()` / `.MapFrom()` results
4. **Ignored properties**: verify they remain default
5. **Nested objects**: verify deep mapping
6. **Collections**: verify list/array mapping
7. **Reverse mapping**: if `.ReverseMap()` is configured

```csharp
[Fact]
public void Map_User_To_UserDto_MapsAllProperties()
{
    var user = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "john@example.com" };
    var dto = _sut.Map<User, UserDto>(user);

    Assert.Equal(1, dto.Id);
    Assert.Equal("John", dto.FirstName);
    Assert.Equal("Doe", dto.LastName);
    Assert.Equal("john@example.com", dto.Email);
}

[Fact]
public void Map_User_To_UserDto_NullSource_ReturnsNull()
{
    User? user = null;
    var dto = _sut.Map<User, UserDto>(user);
    Assert.Null(dto);
}
```

#### 2.3 Test MapInto (if used)

```csharp
[Fact]
public void MapInto_Updates_Existing_Object()
{
    var source = new User { Id = 1, Name = "Updated" };
    var existing = new UserDto { Id = 99, Name = "Original", Extra = "kept" };

    _sut.Map(source, existing);

    Assert.Equal(1, existing.Id);
    Assert.Equal("Updated", existing.Name);
    Assert.Equal("kept", existing.Extra); // unmapped property preserved
}
```

#### 2.4 Verify and commit

```bash
dotnet test   # all new tests must pass
```

Commit message: `test: add mapping contract tests for IMappingService`

---

## Commit 3: Replace AutoMapper with ForgeMap

### Goal

Swap the `AutoMapperMappingService` implementation with a `ForgeMapMappingService` that uses ForgeMap source-generated forgers. **All tests from commit 2 must pass.** If any fail, file a ForgeMap issue.

### CRITICAL: Consult `references/api-mapping-guide.md` for every translation

### Steps

#### 3.1 Add ForgeMap NuGet package

```xml
<PackageReference Include="ForgeMap" Version="1.0.0" />
```

Pin to a specific known-good version for reproducible builds. Update intentionally when needed.

**NOTE**: Some features in the API mapping reference (e.g., `[ConvertWith]`) require ForgeMap 1.1+. If you need those features, pin to the minimum version that includes them. The reference guide marks version requirements where they differ from 1.0.

Remove the AutoMapper package reference ONLY after all tests pass. For now, keep both.

#### 3.2 Translate Profile classes to Forger classes

For each AutoMapper `Profile`, create a corresponding `[ForgeMap]` partial class.

**Translation rules** (see `references/api-mapping-guide.md` for full table):

| AutoMapper | ForgeMap |
|---|---|
| `CreateMap<S,D>()` | `public partial D Forge(S source);` |
| `.ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | `[ForgeProperty(nameof(S.Y), nameof(D.X))]` |
| `.ForMember(d => d.X, o => o.Ignore())` | `[Ignore(nameof(D.X))]` |
| `.ReverseMap()` | `[ReverseForge]` |
| `.BeforeMap(...)` | `[BeforeForge(nameof(MethodName))]` |
| `.AfterMap(...)` | `[AfterForge(nameof(MethodName))]` |
| `.ForMember(d => d.X, o => o.MapFrom(s => expr))` | `[ForgeFrom(nameof(D.X), nameof(ResolverMethod))]` + static method |
| Nested `CreateMap<A,B>()` used in parent | `[ForgeWith(nameof(D.Prop), nameof(ForgeNested))]` + nested forge method | For single nested objects only |
| Nested collection (e.g., `List<A>` → `List<B>`) | `[ForgeWith]` referencing a collection-level forge method + element forge method | Element method must share the same name as the collection method (overload resolution); collection properties are NOT auto-mapped on a parent object |
| `mapper.Map(src, dest)` pattern | A void partial method with a `[UseExistingValue]` destination parameter (e.g., `ForgeInto(src, [UseExistingValue] dest)`). The name `ForgeInto` is a convention, not required |

**Common gotchas**:
- AutoMapper is case-insensitive by default; ForgeMap is case-sensitive. Add `PropertyMatching = PropertyMatching.ByNameCaseInsensitive` if the project relies on case-insensitive matching.
- AutoMapper auto-flattens (e.g., `Order.Customer.Name` → `CustomerName`). ForgeMap also auto-flattens by default (via `TryAutoFlatten`), but if auto-flatten fails to match, use explicit `[ForgeProperty("Customer.Name", nameof(OrderDto.CustomerName))]`.
- AutoMapper auto-discovers nested maps. ForgeMap requires explicit `[ForgeWith]`.
- `ProjectTo<T>()` has NO ForgeMap equivalent. These call sites must be rewritten to materialize the query first, then map in-memory: `query.ToList().Select(x => forger.Forge(x)).ToList()`. Warn the user about potential performance implications.

#### 3.3 Create ForgeMapMappingService

```csharp
public class ForgeMapMappingService : IMappingService
{
    private readonly AppForger _forger; // or multiple forgers

    public ForgeMapMappingService(AppForger forger) => _forger = forger;

    public TDestination Map<TDestination>(object? source)
    {
        // Handle null source — AutoMapper returns default(TDestination) for null
        if (source is null) return default!;

        // Use pattern matching or a dictionary to dispatch to correct Forge method
        // based on source runtime type and TDestination
        return source switch
        {
            User u when typeof(TDestination) == typeof(UserDto) => (TDestination)(object)_forger.Forge(u),
            Order o when typeof(TDestination) == typeof(OrderDto) => (TDestination)(object)_forger.Forge(o),
            // ... enumerate all mappings
            _ => throw new NotSupportedException($"No mapping from {source.GetType().Name} to {typeof(TDestination).Name}")
        };
    }

    public TDestination Map<TSource, TDestination>(TSource? source)
    {
        if (source is null) return default!;
        return Map<TDestination>((object)source);
    }

    public void Map<TSource, TDestination>(TSource source, TDestination destination)
    {
        // Dispatch to the appropriate Forge "Into" methods, similar to Map<TDestination> above.
        // If not needed, throw so failures are explicit rather than silent no-ops.
        throw new NotSupportedException(
            $"Map-into-existing-object not configured for {typeof(TSource).Name} -> {typeof(TDestination).Name}. " +
            "Add dispatch to the ForgeInto method here.");
    }
}
```

**NOTE**: The dispatch pattern above is temporary — it exists only for this abstraction layer which will be removed in commit 4. Optimize for correctness, not elegance.

#### 3.4 Update DI registration

```csharp
services.AddForgeMaps(); // registers forger classes
services.AddSingleton<IMappingService, ForgeMapMappingService>(); // or appropriate lifetime
// Remove: services.AddAutoMapper(...) — but only after tests pass
```

Update the test class to use `ForgeMapMappingService` instead of `AutoMapperMappingService`:

```csharp
public MappingServiceTests()
{
    var forger = new AppForger(); // or resolve from a ServiceProvider
    _sut = new ForgeMapMappingService(forger);
}
```

#### 3.5 Run tests and handle failures

```bash
dotnet build
dotnet test
```

**If all tests pass**: proceed to remove AutoMapper package reference and commit.

**If any tests fail**: This indicates a behavioral gap between AutoMapper and ForgeMap.

For each failure:
1. Analyze the failure — is it a ForgeMap bug or an expected behavioral difference?
2. If it's a **ForgeMap bug or missing feature**: create a GitHub issue on `superyyrrzz/ForgeMap` with:
   - Title: `[Migration] <concise description of the gap>`
   - Body:
     - AutoMapper behavior (with code example)
     - ForgeMap behavior (with code example and error/diff)
     - Expected ForgeMap behavior
     - Test case that reproduces the issue
   - Label: `bug` or `enhancement`
3. If it's an **expected behavioral difference** (e.g., case sensitivity): adjust the ForgeMap configuration or test expectation. Document the adjustment in the commit message.

```bash
# Create issue for each real gap
gh issue create --repo superyyrrzz/ForgeMap \
  --title "[Migration] <description>" \
  --body "$(cat <<'EOF'
## AutoMapper behavior
...
## ForgeMap behavior
...
## Expected behavior
...
## Reproduction
...
EOF
)" --label bug
```

After resolving all failures (or filing issues for genuine gaps), remove the AutoMapper package:

```xml
<!-- Remove from csproj -->
<PackageReference Include="AutoMapper" ... />
<PackageReference Include="AutoMapper.Extensions.Microsoft.DependencyInjection" ... />
```

Delete all AutoMapper Profile classes and the `AutoMapperMappingService`.

```bash
dotnet build && dotnet test
```

Commit message: `feat: replace AutoMapper with ForgeMap behind IMappingService`

Include in the commit body:
- Number of mappings migrated
- Any behavioral differences found (with issue links if filed)
- Any `ProjectTo` call sites that were rewritten

---

## Commit 4: Remove abstraction layer, use ForgeMap directly

### Goal

Eliminate `IMappingService` and have all code depend directly on ForgeMap forger classes. This removes the indirection and lets consumers benefit from ForgeMap's compile-time safety.

### Steps

#### 4.1 Replace IMappingService injection with forger injection

For every class that injects `IMappingService`:
- Replace `IMappingService mappingService` with the appropriate forger class (e.g., `AppForger forger`)
- Replace `_mappingService.Map<UserDto>(user)` with `_forger.Forge(user)` (direct forge calls)
- Replace `_mappingService.Map(source, dest)` with a call to the generated void method with `[UseExistingValue]` (often named `ForgeInto`, e.g., `_forger.ForgeInto(source, dest)`)

#### 4.2 Delete abstraction layer files

- Delete `IMappingService.cs`
- Delete `ForgeMapMappingService.cs`
- Delete `AutoMapperMappingService.cs` (if not already deleted)

#### 4.3 Update DI registration

```csharp
// Keep only:
services.AddForgeMaps();
// Remove: services.AddSingleton<IMappingService, ForgeMapMappingService>();
```

#### 4.4 Update tests

Rewrite `MappingServiceTests` to test forger classes directly:

```csharp
public class MappingTests
{
    private readonly AppForger _forger = new();
    // NOTE: `new()` works only if the forger has a parameterless constructor.
    // If it has DI dependencies, resolve via ServiceProvider after AddForgeMaps().

    [Fact]
    public void Forge_User_To_UserDto_MapsAllProperties()
    {
        var user = new User { Id = 1, FirstName = "John" };
        var dto = _forger.Forge(user);
        Assert.Equal(1, dto.Id);
        Assert.Equal("John", dto.FirstName);
    }
}
```

#### 4.5 Verify and commit

```bash
dotnet build && dotnet test
```

Commit message: `refactor: remove IMappingService abstraction, use ForgeMap directly`

---

## Error Handling

### Build failures in step 3

Common issues:
- **Missing `partial` keyword**: Forger classes and forge methods MUST be `partial`
- **Namespace mismatch**: Ensure ForgeMap attributes are imported (`using ForgeMap;`)
- **Diagnostic FM0005** (unmapped source property): Add `[Ignore]` or `SuppressDiagnostics = new[] { "FM0005" }` if intentional
- **Diagnostic FM0007** (nullable to non-nullable mapping): A nullable source property is mapped to a non-nullable destination — make the destination nullable, adjust null handling, or use `[ForgeFrom]` to provide a non-null fallback
- **Diagnostic FM0015** (`[ForgeWith]` target missing `[ReverseForge]`): Add `[ReverseForge]` to nested method or remove from parent

### Test failures in step 3

Categorize each failure:
1. **Configuration error** (typo, missing attribute) → fix the forger
2. **Behavioral difference** (legitimate) → adjust test + document
3. **ForgeMap bug** → file issue, add `[Skip]` with issue link, proceed

# ForgeMap v1.2 Specification: Null-Safe Property Assignment

> **Tracking issue:** [#53 — Source generator should emit null-safe code for nullable-to-non-nullable property assignments](https://github.com/superyyrrzz/ForgeMap/issues/53)

---

## Overview

ForgeMap v1.1 emits `#nullable enable` in all generated files but does not handle nullable-to-non-nullable **reference type** property assignments. When a nullable source property (e.g., `List<string>?`) is mapped to a non-nullable destination (e.g., `List<string>`), the generator emits a bare assignment that produces **CS8601** — a build error under `TreatWarningsAsErrors`.

This version adds a configurable `NullPropertyHandling` strategy that controls what code the generator emits for these mismatches, and wires up the existing FM0007 diagnostic that was defined but never reported.

| Feature | Problem Solved |
|---------|---------------|
| `NullPropertyHandling` enum | No way to control how nullable→non-nullable ref property assignments are generated |
| Three-tier configuration | Users need assembly-wide defaults, per-forger overrides, and per-property overrides |
| FM0007 diagnostic activation | Warning exists but is never reported; users get no signal about nullable mismatches |

### Scope

This feature applies **only** to nullable **reference type** properties mapped to non-nullable reference type destinations. `Nullable<T>` value types are already handled in v1.1 by unwrapping via `nullableExpr!.Value` (with casts as needed), which will throw at runtime if the value is `null`.

---

## Feature 1: `NullPropertyHandling` Enum

### Definition

```csharp
namespace ForgeMap;

/// <summary>
/// Specifies how nullable source properties should be assigned to non-nullable destination properties.
/// This setting only applies to reference type properties where the source has a nullable annotation
/// and the destination does not.
/// </summary>
public enum NullPropertyHandling
{
    /// <summary>
    /// Use the null-forgiving operator: <c>target.X = source.X!;</c>
    /// The assignment always happens. If the source value is null at runtime,
    /// the destination receives null (bypassing the compiler's nullable analysis).
    /// This is the default, matching AutoMapper's "assign through" behavior.
    /// </summary>
    NullForgiving,

    /// <summary>
    /// Skip the assignment when the source is null:
    /// <c>if (source.X is { } value) target.X = value;</c>
    /// The destination retains its constructor-initialized or default value.
    /// </summary>
    SkipNull,

    /// <summary>
    /// Coalesce to a type-appropriate default value:
    /// <c>target.X = source.X ?? &lt;default&gt;;</c>
    /// The assignment always happens. If the source value is null, a non-null
    /// default is substituted (see §Type-Aware Default Values).
    /// </summary>
    CoalesceToDefault,

    /// <summary>
    /// Throw an exception when the source is null:
    /// <c>target.X = source.X ?? throw new ArgumentNullException(nameof(source.X));</c>
    /// Fail-fast behavior for strict null safety using a single-evaluation, coalesce-to-throw pattern.
    /// </summary>
    ThrowException
}
```

### Compiler vs Runtime Safety

All four strategies eliminate CS8601 from generated code, but they differ in runtime behavior:

| Strategy | CS8601 eliminated? | Runtime null risk | Generated code complexity |
|----------|-------------------|-------------------|--------------------------|
| `NullForgiving` | Yes — `!` suppresses | Yes — if source is actually null | Minimal (single `!`) |
| `SkipNull` | Yes — flow analysis | Yes — if constructor doesn't initialize the property | Moderate (`if` guard) |
| `CoalesceToDefault` | Yes — `??` ensures non-null | **No** — always assigns a value | Moderate (`??` expression) |
| `ThrowException` | Yes — unreachable after throw | **No** — throws before null reaches target | Moderate (`if` + throw) |

### Default Value

`NullForgiving` is the default. Rationale:

1. **AutoMapper compatibility** — AutoMapper assigns null through by default (`AllowNullDestinationValues = true`). Since ForgeMap's primary migration path is from AutoMapper, matching this behavior reduces surprises during migration.
2. **Consistency** — The generator already uses `!` for `Nullable<T>` value types and null-conditional chains. Using the same approach for nullable references is internally consistent.
3. **Minimal codegen** — Appending `!` to an existing expression is the simplest change with no control flow overhead.

---

## Feature 2: Three-Tier Configuration

### Configuration Hierarchy

Settings are resolved in order: per-property > per-forger > assembly default. The most specific setting wins.

```
┌──────────────────────────────────────────────────┐
│  Assembly defaults   [ForgeMapDefaults]           │  Lowest priority
├──────────────────────────────────────────────────┤
│  Forger class        [ForgeMap]                   │
├──────────────────────────────────────────────────┤
│  Per-property        [ForgeProperty]              │  Highest priority
└──────────────────────────────────────────────────┘
```

### Tier 1: Assembly-Level Default

```csharp
[assembly: ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
```

**Attribute change:**

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapDefaultsAttribute : Attribute
{
    // Existing properties...
    public NullHandling NullHandling { get; set; } = NullHandling.ReturnNull;
    public bool GenerateCollectionMappings { get; set; } = true;
    public PropertyMatching PropertyMatching { get; set; } = PropertyMatching.ByName;

    // New in v1.2
    public NullPropertyHandling NullPropertyHandling { get; set; } = NullPropertyHandling.NullForgiving;
}
```

### Tier 2: Per-Forger Override

```csharp
[ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]
public partial class StrictForger
{
    public partial SnapshotDto Forge(Assessment source);
}
```

**Attribute change:**

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapAttribute : Attribute
{
    // Existing properties...
    public NullHandling NullHandling { get; set; } = NullHandling.ReturnNull;
    public PropertyMatching PropertyMatching { get; set; } = PropertyMatching.ByName;
    public string[]? SuppressDiagnostics { get; set; }

    // New in v1.2
    public NullPropertyHandling NullPropertyHandling { get; set; } = NullPropertyHandling.NullForgiving;
}
```

### Tier 3: Per-Property Override

```csharp
[ForgeMap]
public partial class AppForger
{
    [ForgeProperty(nameof(Assessment.MetaDataTags), nameof(SnapshotDto.MetaDataTags),
        NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
    [ForgeProperty(nameof(Assessment.AuditLog), nameof(SnapshotDto.AuditLog),
        NullPropertyHandling = NullPropertyHandling.ThrowException)]
    public partial SnapshotDto Forge(Assessment source);
}
```

**Attribute change:**

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ForgePropertyAttribute : Attribute
{
    public ForgePropertyAttribute(string sourceProperty, string destinationProperty)
    {
        SourceProperty = sourceProperty ?? throw new ArgumentNullException(nameof(sourceProperty));
        DestinationProperty = destinationProperty ?? throw new ArgumentNullException(nameof(destinationProperty));
    }

    public string SourceProperty { get; }
    public string DestinationProperty { get; }

    // New in v1.2 — overrides forger/assembly NullPropertyHandling for this property.
    // A value of -1 means "not set" (inherit from forger/assembly).
    public NullPropertyHandling NullPropertyHandling { get; set; } = (NullPropertyHandling)(-1);
}
```

The sentinel value `(NullPropertyHandling)(-1)` indicates "not set" and triggers inheritance from the forger or assembly level. This follows the same pattern used by the Roslyn compiler for optional enum attribute properties.

---

## Feature 3: Generated Code Patterns

### 3.1 `NullForgiving` (default)

```csharp
// Source: List<string>? MetaDataTags
// Dest:   List<string>  MetaDataTags
target.MetaDataTags = source.MetaDataTags!;
```

### 3.2 `SkipNull`

```csharp
if (source.MetaDataTags is { } metaDataTags)
{
    target.MetaDataTags = metaDataTags;
}
```

### 3.3 `CoalesceToDefault`

```csharp
// List<string>? → List<string>
target.MetaDataTags = source.MetaDataTags ?? new global::System.Collections.Generic.List<string>();

// string? → string
target.Name = source.Name ?? "";

// string[]? → string[]
target.Tags = source.Tags ?? global::System.Array.Empty<string>();

// CustomType? → CustomType (has parameterless ctor)
target.Config = source.Config ?? new global::MyApp.Models.Config();
```

#### Type-Aware Default Values

| Destination type | Generated default | Condition |
|-----------------|-------------------|-----------|
| `string` | `""` | `SpecialType == System_String` |
| `T[]` | `global::System.Array.Empty<T>()` | Array type |
| `List<T>` | `new global::System.Collections.Generic.List<T>()` | Named type with parameterless ctor |
| `Dictionary<K,V>` | `new global::System.Collections.Generic.Dictionary<K, V>()` | Named type with parameterless ctor |
| Any type with parameterless ctor | `new global::Fully.Qualified.TypeName()` | Has accessible parameterless constructor |
| No parameterless ctor | **Fall back to `NullForgiving`** | Report FM0007 (standard message) |

When `CoalesceToDefault` cannot find a suitable default (no parameterless constructor), the generator falls back to `NullForgiving` (`!`) and reports FM0007 using its existing descriptor message format (`"The nullable source '{0}.{1}' is mapped to non-nullable destination '{2}.{3}'"`).

### 3.4 `ThrowException`

```csharp
target.MetaDataTags = source.MetaDataTags
    ?? throw new global::System.ArgumentNullException(nameof(source.MetaDataTags),
        "Cannot assign null source property 'Assessment.MetaDataTags' to non-nullable destination 'SnapshotDto.MetaDataTags'.");
```

---

## Feature 4: FM0007 Diagnostic Activation

### Current State

FM0007 (`NullableToNonNullableMapping`) is defined in `DiagnosticDescriptors.cs` but never reported by the generator.

### New Behavior

The generator reports FM0007 for each direct property-to-property mapping where a nullable reference type source property is assigned to a non-nullable reference type destination property, regardless of the chosen `NullPropertyHandling` strategy. Properties mapped via `[ForgeFrom]` custom resolvers are exempt from FM0007 (see [Interaction with Existing Features](#interaction-with-existing-features)). This gives users visibility into nullable mismatches for standard mappings even when the strategy silently handles them.

### Diagnostic Definition (unchanged)

```csharp
public static readonly DiagnosticDescriptor NullableToNonNullableMapping = new(
    id: "FM0007",
    title: "Nullable to non-nullable mapping",
    messageFormat: "The nullable source '{0}.{1}' is mapped to non-nullable destination '{2}.{3}'",
    category: "ForgeMap",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

### Suppression

FM0007 can be suppressed via the existing `SuppressDiagnostics` mechanism:

```csharp
[ForgeMap(SuppressDiagnostics = new[] { "FM0007" })]
public partial class AppForger { ... }
```

Or at the file level via `#pragma`:

```csharp
#pragma warning disable FM0007
```

For project-wide suppression, use `<NoWarn>FM0007</NoWarn>` or `<WarningsNotAsErrors>FM0007</WarningsNotAsErrors>` in `.csproj` / `Directory.Build.props`. Assembly-level diagnostic suppression via `ForgeMapDefaults` is not included in v1.2.

---

## Interaction with Existing Features

### `[ForgeFrom]` resolvers

When a property has a `[ForgeFrom]` resolver, `NullPropertyHandling` does **not** apply — the resolver is responsible for its own null handling. The generator calls the resolver unconditionally. FM0007 is also suppressed for properties with explicit resolvers, since the user has taken ownership of the mapping.

### `[ForgeWith]` nested forge methods

When a property uses `[ForgeWith]`, the nested forge method's own `NullHandling` (source object null handling) applies. The `NullPropertyHandling` strategy is **not** applied on top — it would be redundant.

### `[ReverseForge]` auto-generated reverse methods

The `NullPropertyHandling` setting applies to reverse methods as well. The direction reverses: if the forward mapping has `string? → string`, the reverse has `string → string?`, which does not trigger `NullPropertyHandling` (non-nullable → nullable is always safe). This is the correct behavior — no special handling needed.

### `ForgeInto()` mutate-in-place

All four strategies work with `ForgeInto()`:
- `NullForgiving`: `target.X = source.X!;`
- `SkipNull`: `if (source.X is { } x) target.X = x;`
- `CoalesceToDefault`: `target.X = source.X ?? <default>;`
- `ThrowException`: `target.X = source.X ?? throw ...;`

### `[IncludeBaseForge]` configuration inheritance

Per-property `NullPropertyHandling` overrides set via `[ForgeProperty]` are inherited through `[IncludeBaseForge]`, following the same override semantics as other inherited configuration: explicit attributes on the derived method take precedence.

### Constructor mapping

When the destination uses constructor parameters (no parameterless constructor), `NullPropertyHandling` applies to the constructor argument expression:

```csharp
// NullForgiving:
return new Destination(source.Name!);

// CoalesceToDefault:
return new Destination(source.Name ?? "");

// ThrowException:
var name = source.Name
    ?? throw new global::System.ArgumentNullException(nameof(source.Name), ...);
return new Destination(name);

// SkipNull — not applicable to required ctor params; falls back to NullForgiving
return new Destination(source.Name!);
```

`SkipNull` cannot skip a required constructor parameter, so it falls back to `NullForgiving` for constructor-mapped properties and reports FM0007.

### Flattening

`NullPropertyHandling` applies to the leaf assignment in flattened property chains. The null-conditional chain (`source.Customer?.Address?.City`) already handles intermediate nulls; `NullPropertyHandling` governs the final assignment to the non-nullable destination.

### `[Ignore]` properties

Ignored properties have no generated assignment and are unaffected by `NullPropertyHandling`.

### `[BeforeForge]` / `[AfterForge]` hooks

`NullPropertyHandling` does not affect hook execution. Hooks run before and after the property assignment phase as documented in the core spec.

### Collection properties

`NullPropertyHandling` applies to collection-typed properties where the source is nullable and the destination is non-nullable (e.g., `List<string>?` → `List<string>`). The `CoalesceToDefault` strategy uses the defaults from the Type-Aware Default Values table. This is independent of collection element mapping — individual elements within the collection are not affected by this setting.

---

## Generator Implementation Notes

### `ForgerConfig` changes

```csharp
internal sealed class ForgerConfig
{
    // Existing...
    public int NullHandling { get; set; }
    public int PropertyMatching { get; set; }
    public bool GenerateCollectionMappings { get; set; } = true;
    public HashSet<string> SuppressDiagnostics { get; set; } = new(...);

    // New in v1.2
    /// <summary>0 = NullForgiving (default), 1 = SkipNull, 2 = CoalesceToDefault, 3 = ThrowException</summary>
    public int NullPropertyHandling { get; set; }
}
```

### Resolution logic in `ResolveForgerConfig()`

```csharp
case "NullPropertyHandling":
    config.NullPropertyHandling = (int)named.Value.Value!;
    break;
```

### Per-property resolution in `GetPropertyMappings()`

`GetPropertyMappings()` currently returns `Dictionary<string, string>` (dest → source name). This must be extended to also carry per-property `NullPropertyHandling` overrides. Options:

1. Change return type to `Dictionary<string, PropertyMappingInfo>` where `PropertyMappingInfo` contains `SourceProperty` and optional `NullPropertyHandling`.
2. Return a separate dictionary `Dictionary<string, int>` for per-property overrides.

Option 1 is cleaner and more extensible for future per-property settings.

### Assignment site changes

At each property assignment site in `GenerateMethod()` and `GenerateForgeIntoMethod()`, when `CanAssign()` detects a nullable ref → non-nullable ref assignment:

1. Resolve the effective `NullPropertyHandling`: per-property override > forger config > assembly default.
2. Report FM0007 (unless suppressed).
3. Emit code according to the resolved strategy.

### Detection of nullable ref → non-nullable ref

```csharp
// Existing helper already checks value types:
private static bool IsNullableToNonNullableValueType(ITypeSymbol source, ITypeSymbol dest) { ... }

// New helper for reference types:
private static bool IsNullableToNonNullableReferenceType(ITypeSymbol source, ITypeSymbol dest)
{
    return source.IsReferenceType
        && source.NullableAnnotation == NullableAnnotation.Annotated
        && dest.NullableAnnotation != NullableAnnotation.Annotated;
}
```

---

## Diagnostics Summary

| Code | Severity | Description | New/Changed |
|------|----------|-------------|-------------|
| `FM0007` | Warning | The nullable source '{0}.{1}' is mapped to non-nullable destination '{2}.{3}' | **Activated** (was defined but never reported) |

No new diagnostic IDs are introduced. FM0007 covers all nullable ref → non-nullable ref cases.

---

## Migration Guide

### Issue #53 Scenario (before)

```csharp
public class SelfAssessment
{
    public List<string>? MetaDataTags { get; set; }
    public List<string>? RelatedAssessments { get; set; }
}
public class SelfAssessmentSnapshot
{
    public List<string> MetaDataTags { get; set; }
    public List<string> RelatedAssessments { get; set; }
}

// Workaround required:
[ForgeMap]
public partial class Forger
{
    [Ignore(nameof(SelfAssessmentSnapshot.MetaDataTags))]
    [Ignore(nameof(SelfAssessmentSnapshot.RelatedAssessments))]
    public partial SelfAssessmentSnapshot ForgeSnapshotCore(SelfAssessment source);

    public SelfAssessmentSnapshot ForgeSnapshot(SelfAssessment source)
    {
        var result = ForgeSnapshotCore(source);
        result.MetaDataTags = source.MetaDataTags!;
        result.RelatedAssessments = source.RelatedAssessments!;
        return result;
    }
}
```

### After (v1.2 — default `NullForgiving`)

```csharp
// Just works — no workaround needed
[ForgeMap]
public partial class Forger
{
    public partial SelfAssessmentSnapshot ForgeSnapshot(SelfAssessment source);
}

// Generated code:
// target.MetaDataTags = source.MetaDataTags!;
// target.RelatedAssessments = source.RelatedAssessments!;
```

### After (v1.2 — `CoalesceToDefault` for safer migration from AutoMapper)

```csharp
[ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
public partial class Forger
{
    public partial SelfAssessmentSnapshot ForgeSnapshot(SelfAssessment source);
}

// Generated code (simplified; actual output uses fully qualified type names):
// target.MetaDataTags = source.MetaDataTags ?? new List<string>();
// target.RelatedAssessments = source.RelatedAssessments ?? new List<string>();
```

### AutoMapper equivalence

| AutoMapper | ForgeMap v1.2 |
|------------|---------------|
| Default (assigns null through) | `NullPropertyHandling.NullForgiving` (default) |
| `AllowNullCollections = false` (null → empty collection) | `NullPropertyHandling.CoalesceToDefault` |
| `.ForMember(d => d.X, o => o.NullSubstitute("default"))` | `[ForgeFrom]` resolver returning the default |

---

## Comparison with Mapperly

| Aspect | Mapperly | ForgeMap v1.2 |
|--------|---------|---------------|
| Configuration knobs | 3 booleans (`ThrowOnMappingNullMismatch`, `ThrowOnPropertyMappingNullMismatch`, `AllowNullPropertyAssignment`) | 1 enum (`NullPropertyHandling`) with 4 strategies |
| Default behavior | Skip assignment (if-guard) | Null-forgiving (`!`) |
| Per-property override | Not supported (mapper-wide only) | Supported via `[ForgeProperty(..., NullPropertyHandling = ...)]` |
| Diagnostics | RMG089 (Info) | FM0007 (Warning) |
| Type-aware defaults | Yes (string → `""`, ctor → `new T()`) | Yes (same approach, see §Type-Aware Default Values) |

---

## Implementation Phases

| Phase | Scope | Key Changes |
|-------|-------|-------------|
| **Phase 1** | Abstractions | Add `NullPropertyHandling` enum, add named property to `ForgeMapAttribute`, `ForgeMapDefaultsAttribute`, `ForgePropertyAttribute` |
| **Phase 2** | Generator core | Add `NullPropertyHandling` to `ForgerConfig`, update `ResolveForgerConfig()`, add `IsNullableToNonNullableReferenceType()`, refactor `GetPropertyMappings()` to return `PropertyMappingInfo` |
| **Phase 3** | Code emission | At each assignment site, resolve strategy and emit the appropriate pattern; wire up FM0007 |
| **Phase 4** | Tests | Unit tests for all 4 strategies, per-property override, assembly/class defaults, constructor mapping fallback, FM0007 reporting |

---

## Appendix: Attribute Reference (v1.2 additions)

```csharp
namespace ForgeMap
{
    /// <summary>
    /// Specifies how nullable source properties should be assigned to
    /// non-nullable destination properties.
    /// </summary>
    public enum NullPropertyHandling
    {
        NullForgiving,
        SkipNull,
        CoalesceToDefault,
        ThrowException
    }

    // Updated attributes (new property shown):

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class ForgeMapDefaultsAttribute : Attribute
    {
        // ... existing properties ...
        public NullPropertyHandling NullPropertyHandling { get; set; } = NullPropertyHandling.NullForgiving;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ForgeMapAttribute : Attribute
    {
        // ... existing properties ...
        public NullPropertyHandling NullPropertyHandling { get; set; } = NullPropertyHandling.NullForgiving;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class ForgePropertyAttribute : Attribute
    {
        public ForgePropertyAttribute(string sourceProperty, string destinationProperty);
        public string SourceProperty { get; }
        public string DestinationProperty { get; }
        public NullPropertyHandling NullPropertyHandling { get; set; } = (NullPropertyHandling)(-1); // "not set" sentinel
    }
}
```

---

*Specification Version: 1.2*
*License: MIT*

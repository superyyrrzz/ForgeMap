# Changelog

## v1.7.0

Closes three AutoMapper-parity gaps surfaced by the Duende.IdentityServer.Admin migration analysis (#125, #126, #127).

### New Features

- **Per-property LINQ projection (`SelectProperty`)** (#136) — New `ForgePropertyAttribute.SelectProperty` named property emits `source.Src?.Select(x => x.<member>).To<TDest>()` for the targeted property, eliminating the `[ForgeFrom]` boilerplate previously required for join-table-entity → primitive-collection mappings (e.g., `List<ApiResourceClaim>` → `List<string>`). Composes with v1.5 collection coercion and v1.6 string↔enum / built-in coercions on the projected element. Mutually exclusive with `ConvertWith`/`ConvertWithType` and `[ForgeFrom]`/`[ForgeWith]`. Adds FM0055–FM0059, FM0072, FM0073, and FM0075.

- **Conditional property assignment (`Condition` / `SkipWhen`)** (#137) — `ForgePropertyAttribute.Condition` and `SkipWhen` accept a predicate method name (`bool Predicate(TSource)` or `bool Predicate(TSource, TDestination)`) that gates whether the destination property is assigned. `SkipWhen` is the inverse of `Condition`; the two are mutually exclusive. Emits an `if` guard around the assignment so the destination retains its existing value when the predicate fails — ideal for nullable update DTOs that should not overwrite populated fields. Not supported on init-only or constructor-bound members. Adds FM0060–FM0064.

- **Entity↔primitive mapping (`[ExtractProperty]` / `[WrapProperty]`)** (#138) — Two new method-level attributes generate single-element projections between entity types and their primitive payloads. `[ExtractProperty(nameof(Source.Member))]` emits `source?.Member` (or value-type equivalent under the active `NullPropertyHandling`); `[WrapProperty(nameof(Dest.Member))]` wraps the source into `TDest` using either an object-initializer form (e.g. `new TDest { Member = source }`) or a constructor-based form, depending on destination member shape and constructor selection (`ConstructorPreference`/`[ForgeConstructor]`), with required-members validation. Complements `SelectProperty` for the single-element case. Mutually exclusive with `[ForgeFrom]`/`[ForgeWith]`/`[ConvertWith]` on the same method. Adds FM0065–FM0071 and FM0074.

### New Diagnostics

| Rule ID | Severity | Description |
|---------|----------|-------------|
| FM0055 | Error | `SelectProperty` source is not enumerable |
| FM0056 | Error | `SelectProperty` member not found on source element type |
| FM0057 | Error | `SelectProperty` element type incompatible with destination element type |
| FM0058 | Error | `SelectProperty` conflicts with `ConvertWith`/`ConvertWithType` |
| FM0059 | Disabled | `SelectProperty` projection applied |
| FM0060 | Error | `Condition` and `SkipWhen` cannot both be set |
| FM0061 | Error | Conditional predicate method invalid (not found / wrong signature / not bool-returning) |
| FM0062 | Error | Conditional assignment not supported on init-only or constructor-bound members |
| FM0063 | Error | Conditional assignment conflicts with `[ForgeFrom]`/`[ForgeWith]` |
| FM0064 | Disabled | Conditional assignment applied |
| FM0065 | Error | `[ExtractProperty]`/`[WrapProperty]` conflicts with `[ForgeFrom]`/`[ForgeWith]`/`[ConvertWith]` |
| FM0066 | Error | `[ExtractProperty]` member not found on source |
| FM0067 | Error | `[ExtractProperty]` member type incompatible with method return type |
| FM0068 | Error | `[WrapProperty]` member not found on destination |
| FM0069 | Error | `[WrapProperty]` member type incompatible with method parameter type |
| FM0070 | Error | `[ExtractProperty]`/`[WrapProperty]` invalid method signature |
| FM0071 | Error | `[WrapProperty]` destination has unsatisfied required members |
| FM0072 | Error | `SelectProperty` conflicts with `[ForgeFrom]`/`[ForgeWith]` |
| FM0073 | Error | `SelectProperty` destination is not enumerable |
| FM0074 | Disabled | `[ExtractProperty]`/`[WrapProperty]` value-type return under `ReturnNull` |
| FM0075 | Warning | `SelectProperty` not supported on `ForgeInto` methods |

## v1.6.0

### New Features

- **Enhanced constructor-based mapping** — New `[ForgeConstructor]` attribute and `ConstructorPreference` enum (`Auto`, `PreferParameterless`) on `[ForgeMap]`/`[ForgeMapDefaults]` for explicit constructor selection. Parameters are matched by name to source properties and `[ForgeProperty]` mappings; unmatched optional parameters receive their declared default values. Adds FM0046 (warning for unmatched parameters), FM0047 (error for specified constructor not found), and FM0048 (disabled routing info).

- **Null-safe string→enum conversion** — String-to-enum mappings now emit null-safe code. When the source is nullable, a null/empty guard returns the enum default instead of throwing. `StrictParse` mode is available for validation scenarios. Adds FM0049 (disabled info diagnostic).

- **Nullable-safe collection coercion** — Collection coercion now handles nullable element types correctly, avoiding CS8620 warnings. Supports `List<T?>` → `IReadOnlyList<T?>` and similar patterns. Adds FM0050 (disabled info) and FM0051 (warning for unsupported patterns).

- **Per-property ConvertWith** — `ForgePropertyAttribute.ConvertWith` allows specifying a converter method or type per property, supporting both method names and `ITypeConverter<TSource, TDest>` type references with DI resolution. Adds FM0052 (warning for method not found), FM0053 (warning for signature mismatch), and FM0054 (disabled info).

- **DateTimeOffset→DateTime auto-coercion** — Automatic conversion from `DateTimeOffset` to `DateTime` via `.UtcDateTime`, with full `NullPropertyHandling` support for nullable variants.

- **Multi-Roslyn targeting for .NET 8/9/10 SDK compatibility** — The NuGet package now ships three analyzer variants compiled against Roslyn 4.8.0, 4.12.0, and 5.0.0, so consumers on .NET 8, 9, or 10 SDKs automatically get the correct binary. Includes pack scripts (`build/pack.sh`, `build/pack.ps1`) for the multi-build workflow and an MSBuild guard against bare `dotnet pack`.

### Bug Fixes

- Fixed duplicate FM0014 emission for unmatched constructor parameters in explicit-constructor path
- Fixed culture-sensitive decimal separators in generated numeric literals (FormatLiteral now uses invariant culture)
- Fixed per-property ConvertWith being silently ignored in normal mappings
- Fixed DateTimeOffset? coercion bypassing NullPropertyHandling pipeline
- Fixed ConvertWith expressions for lifted value types using explicit cast instead of null-forgiving operator
- Fixed dictionary coercion for unsupported types (SortedDictionary, ConcurrentDictionary) returning null for proper FM0051 reporting
- Fixed character and string literal escaping using Roslyn's SymbolDisplay.FormatLiteral
- Fixed enum default expressions using FullyQualifiedFormat for cross-namespace resolution
- Inherited ConvertWith mappings through IncludeBaseForge with first-wins semantics
- Fixed `NullPropertyHandling` strategies (`CoalesceToNew`, `CoalesceToDefault`, `ThrowException`, `SkipNull`) being ignored for `Nullable<T>` → `T` value type mappings — previously always generated a forced unwrap that threw on null (#115)

### New Diagnostics

| Rule ID | Severity | Description |
|---------|----------|-------------|
| FM0046 | Warning | Unmatched constructor parameter |
| FM0047 | Error | Specified constructor not found |
| FM0048 | Disabled | Constructor mapping routing info |
| FM0049 | Disabled | Null-safe guard applied to string→enum conversion |
| FM0050 | Disabled | Nullable-safe collection coercion applied |
| FM0051 | Warning | Unsupported nullable collection coercion |
| FM0052 | Warning | Per-property converter method not found |
| FM0053 | Warning | Per-property converter signature mismatch |
| FM0054 | Disabled | Per-property converter applied |

## v1.5.0

### New Features

- **CoalesceToNew null-property handling** (Feature 1) — New `NullPropertyHandling.CoalesceToNew` strategy that coalesces null destination properties to `new T()` instead of skipping them. Includes FM0038 diagnostic for types lacking an accessible parameterless constructor.

- **Collection type coercion** (Feature 2) — The generator now automatically coerces collection types (e.g., `List<T>` to `IReadOnlyList<T>`, `Dictionary<K,V>` to `IDictionary<K,V>`) when the destination expects a compatible interface. Adds FM0039 (disabled informational) and FM0040 (warning) diagnostics for coercion reporting.

- **Standalone collection mapping methods** (Feature 3) — Support for dedicated collection-level forge methods (`IEnumerable<TDest> ForgeAll(IEnumerable<TSrc>)`) that are auto-discovered from element-level forge methods. Adds FM0041 and FM0042 diagnostics for missing or ambiguous element mappings.

- **[AfterForge] migration diagnostics** (Feature 4) — New diagnostics FM0043, FM0044, and FM0045 to guide users migrating `[AfterForge]` callbacks: method-not-found/wrong-signature, mutual exclusivity with `[ConvertWith]`, and inapplicability to collection methods.

### Bug Fixes

- Fixed FM0044 to check attribute presence rather than resolved hooks
- Fixed duplicate FM0018 emission on enum methods with both `[BeforeForge]` and `[AfterForge]`
- Fixed FM0044 path to only report `[BeforeForge]` as FM0018, not `[AfterForge]`
- Fixed dictionary coercion for constructor parameters and `IDictionary` destinations
- Tightened coercion to identical element types with improved constructor fallback
- Fixed required-members validation in the `ExistingTarget` new `T()` path
- Assembly-aware constructor accessibility for CoalesceToNew validation
- FM0030 is now correctly a warning, not a hard requirement for Sync methods

### New Diagnostics

| Rule ID | Severity | Description |
|---------|----------|-------------|
| FM0038 | Error | CoalesceToNew requires accessible parameterless constructor |
| FM0039 | Disabled | Collection type coerced (informational) |
| FM0040 | Warning | No known collection coercion |
| FM0041 | Error | Collection method has no matching element forge method |
| FM0042 | Error | Ambiguous collection method |
| FM0043 | Error | [AfterForge] method not found or has wrong signature |
| FM0044 | Error | [AfterForge] and [ConvertWith] are mutually exclusive |
| FM0045 | Error | [AfterForge] is not applicable to collection methods |

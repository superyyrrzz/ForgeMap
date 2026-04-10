# Changelog

## v1.6.0

### New Features

- **Enhanced constructor-based mapping** — New `[ForgeConstructor]` attribute and `ConstructorPreference` enum (`Auto`, `PreferParameterless`) on `[ForgeMap]`/`[ForgeMapDefaults]` for explicit constructor selection. Parameters are matched by name to source properties and `[ForgeProperty]` mappings; unmatched optional parameters receive their declared default values. Adds FM0046 (warning for unmatched parameters), FM0047 (error for specified constructor not found), and FM0048 (disabled routing info).

- **Null-safe string→enum conversion** — String-to-enum mappings now emit null-safe code. When the source is nullable, a null/empty guard returns the enum default instead of throwing. `StrictParse` mode is available for validation scenarios. Adds FM0049 (disabled info diagnostic).

- **Nullable-safe collection coercion** — Collection coercion now handles nullable element types correctly, avoiding CS8620 warnings. Supports `List<T?>` → `IReadOnlyList<T?>` and similar patterns. Adds FM0050 (disabled info) and FM0051 (warning for unsupported patterns).

- **Per-property ConvertWith** — `ForgePropertyAttribute.ConvertWith` allows specifying a converter method or type per property, supporting both method names and `ITypeConverter<TSource, TDest>` type references with DI resolution. Adds FM0052 (warning for method not found), FM0053 (warning for signature mismatch), and FM0054 (disabled info).

- **DateTimeOffset→DateTime auto-coercion** — Automatic conversion from `DateTimeOffset` to `DateTime` via `.UtcDateTime`, with full `NullPropertyHandling` support for nullable variants.

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

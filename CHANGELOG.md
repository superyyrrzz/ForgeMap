# Changelog

## v1.5.0

### New Features

- **CoalesceToNew null-property handling** (Feature 1) — New `NullPropertyHandling.CoalesceToNew` strategy that coalesces null destination properties to `new T()` instead of skipping them. Includes FM0038 diagnostic for types lacking an accessible parameterless constructor.

- **Collection type coercion** (Feature 2) — The generator now automatically coerces collection types (e.g., `List<T>` to `IReadOnlyList<T>`, `Dictionary<K,V>` to `IDictionary<K,V>`) when the destination expects a compatible interface. Adds FM0039 (info) and FM0040 (warning) diagnostics for coercion reporting.

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

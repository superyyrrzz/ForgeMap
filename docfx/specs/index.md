# Specifications

This section contains the internal design specifications for ForgeMap. These documents define the detailed behavior, architecture, and feature contracts for each version.

> [!NOTE]
> For user-facing documentation, see the [Docs](../articles/getting-started/quick-start.md) section. These specifications are primarily useful for contributors and advanced users who want to understand the generator's exact behavior.

## Version History

| Version | Specification | Key Features |
|---------|--------------|--------------|
| **v1.0** | [Core Specification](../../docs/SPEC.md) | Property matching, collections, enums, constructors, flattening, reverse mapping, hooks, DI |
| **v1.1** | [Inheritance](../../docs/SPEC-v1.1-inheritance.md) | `[IncludeBaseForge]`, `[ForgeAllDerived]` polymorphic dispatch |
| **v1.2** | [Null Property Handling](../../docs/SPEC-v1.2-null-property-handling.md) | 5 strategies, three-tier config |
| **v1.3** | [Auto-Wiring](../../docs/SPEC-v1.3-auto-wiring.md) | Auto-wire nested & collection mappings, abstract destination dispatch |
| **v1.4** | [Advanced Mapping](../../docs/SPEC-v1.4-advanced-mapping.md) | Nested existing-target, string-to-enum, `[ConvertWith]` |
| **v1.5** | [Advanced Mapping](../../docs/SPEC-v1.5-advanced-mapping.md) | CoalesceToNew, collection coercion, standalone collection methods |
| **v1.6** | [Migration Features (Planned)](../../docs/SPEC-v1.6-migration-features.md) | Per-property ConvertWith, type coercions, constructor preference |
| **Future** | [Advanced Mapping (Planned)](../../docs/SPEC-future-advanced-mapping.md) | Auto-flattening, dictionary mapping |

## Comparison

- [ForgeMap vs AutoMapper & Mapperly](../../docs/ForgeMap-vs-AutoMapper-and-Mapperly.md) — Feature-by-feature comparison with other mapping libraries

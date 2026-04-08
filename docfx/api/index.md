# ForgeMap API Reference

This section contains the auto-generated API documentation for `ForgeMap.Abstractions` — the public attributes, interfaces, and enums used to configure ForgeMap mappings.

## Attributes

### Class-Level

| Type | Description |
|------|-------------|
| <xref:ForgeMap.ForgeMapAttribute> | Marks a partial class as a forger |
| <xref:ForgeMap.ForgeMapDefaultsAttribute> | Assembly-level defaults for all forgers |

### Method-Level

| Type | Description |
|------|-------------|
| <xref:ForgeMap.ForgePropertyAttribute> | Map or rename a property |
| <xref:ForgeMap.ForgeFromAttribute> | Custom resolver for a destination property |
| <xref:ForgeMap.ForgeWithAttribute> | Explicit nested forging method |
| <xref:ForgeMap.IgnoreAttribute> | Ignore a destination property |
| <xref:ForgeMap.ReverseForgeAttribute> | Generate a reverse mapping method |
| <xref:ForgeMap.BeforeForgeAttribute> | Pre-mapping hook |
| <xref:ForgeMap.AfterForgeAttribute> | Post-mapping hook |
| <xref:ForgeMap.ConvertWithAttribute> | Delegate mapping to an `ITypeConverter` |
| <xref:ForgeMap.IncludeBaseForgeAttribute> | Inherit configuration from a base forge method |
| <xref:ForgeMap.ForgeAllDerivedAttribute> | Polymorphic dispatch across derived types |

### Parameter-Level

| Type | Description |
|------|-------------|
| <xref:ForgeMap.UseExistingValueAttribute> | Mark a parameter as existing target for mutation |

## Interfaces

| Type | Description |
|------|-------------|
| <xref:ForgeMap.ITypeConverter`2> | Custom type converter contract |

## Enums

| Type | Description |
|------|-------------|
| <xref:ForgeMap.NullHandling> | Source-object null behavior |
| <xref:ForgeMap.NullPropertyHandling> | Nullable-to-non-nullable property strategies |
| <xref:ForgeMap.PropertyMatching> | Property name matching mode |
| <xref:ForgeMap.StringToEnumConversion> | String-to-enum conversion strategy |
| <xref:ForgeMap.CollectionUpdateStrategy> | Collection sync strategy for mutation methods |

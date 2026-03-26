; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.3.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FM0024 | ForgeMap | Warning | [ForgeAllDerived] on abstract/interface destination
FM0025 | ForgeMap | Warning | Ambiguous auto-wire: multiple forge methods match
FM0026 | ForgeMap | Warning | Auto-wired property has no reverse forge method
FM0027 | ForgeMap | Disabled | Property auto-wired via forge method

## Release 1.2.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------

## Release 1.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FM0019 | ForgeMap | Error | [IncludeBaseForge] base forge method not found
FM0020 | ForgeMap | Error | [IncludeBaseForge] type mismatch
FM0021 | ForgeMap | Info | [IncludeBaseForge] inherited attribute overridden
FM0022 | ForgeMap | Warning | [ForgeAllDerived] found no derived forge methods
FM0023 | ForgeMap | Error | [ForgeAllDerived] cannot be combined with [ConvertWith]

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FM0001 | ForgeMap | Error | Forger class must be partial
FM0002 | ForgeMap | Error | Forging method must be partial
FM0003 | ForgeMap | Error | Source type has no accessible properties
FM0004 | ForgeMap | Error | Destination type has no accessible constructor
FM0005 | ForgeMap | Warning | Unmapped source property
FM0006 | ForgeMap | Warning | Unmapped destination property
FM0007 | ForgeMap | Warning | Nullable to non-nullable mapping
FM0008 | ForgeMap | Error | Resolver method not found
FM0009 | ForgeMap | Error | Invalid resolver method signature
FM0010 | ForgeMap | Error | Circular mapping dependency detected
FM0011 | ForgeMap | Disabled | Property mapped by convention
FM0012 | ForgeMap | Warning | [ForgeFrom] cannot be auto-reversed
FM0013 | ForgeMap | Error | Ambiguous constructor selection
FM0014 | ForgeMap | Error | Constructor parameter has no matching source
FM0015 | ForgeMap | Warning | [ForgeWith] nested method lacks [ReverseForge]
FM0016 | ForgeMap | Error | Hook method not found or has invalid signature
FM0017 | ForgeMap | Error | [UseExistingValue] on non-reference type or method returns non-void
FM0018 | ForgeMap | Warning | [BeforeForge]/[AfterForge] not supported on enum or collection forge methods

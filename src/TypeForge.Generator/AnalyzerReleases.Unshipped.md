; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TF0001 | TypeForge | Error | Forger class must be partial
TF0002 | TypeForge | Error | Forging method must be partial
TF0003 | TypeForge | Error | Source type has no accessible properties
TF0004 | TypeForge | Error | Destination type has no accessible constructor
TF0005 | TypeForge | Warning | Unmapped source property
TF0006 | TypeForge | Warning | Unmapped destination property
TF0007 | TypeForge | Warning | Nullable to non-nullable mapping
TF0008 | TypeForge | Error | Resolver method not found
TF0009 | TypeForge | Error | Invalid resolver method signature
TF0010 | TypeForge | Error | Circular mapping dependency detected
TF0011 | TypeForge | Disabled | Property mapped by convention
TF0012 | TypeForge | Warning | [ForgeFrom] cannot be auto-reversed
TF0013 | TypeForge | Error | Ambiguous constructor selection
TF0014 | TypeForge | Error | Constructor parameter has no matching source
TF0015 | TypeForge | Warning | [ForgeWith] nested method lacks [ReverseForge]
TF0016 | TypeForge | Error | Hook method not found or has invalid signature
TF0017 | TypeForge | Error | [UseExistingValue] on non-reference type or method returns non-void

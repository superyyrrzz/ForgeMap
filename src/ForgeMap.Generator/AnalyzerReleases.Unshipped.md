; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FM0028 | ForgeMap | Error | ExistingTarget = true is only valid on [UseExistingValue] mutation methods
FM0029 | ForgeMap | Error | Property has no getter for in-place update
FM0030 | ForgeMap | Warning | No matching ForgeInto method for nested existing-target
FM0031 | ForgeMap | Error | CollectionUpdateStrategy.Sync requires KeyProperty
FM0032 | ForgeMap | Error | KeyProperty not found on element type
FM0033 | ForgeMap | Disabled | Property auto-converted from string to enum
FM0034 | ForgeMap | Error | [ConvertWith] type does not implement ITypeConverter
FM0035 | ForgeMap | Error | [ConvertWith] converter type has no accessible parameterless ctor and no DI
FM0036 | ForgeMap | Warning | [ConvertWith] takes precedence over [ForgeProperty]/[ForgeFrom]
FM0037 | ForgeMap | Error | [ConvertWith] member not found or incompatible


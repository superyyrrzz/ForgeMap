; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FM0038 | ForgeMap | Error | CoalesceToNew requires accessible parameterless constructor
FM0039 | ForgeMap | Disabled | Collection type coerced
FM0040 | ForgeMap | Warning | No known collection coercion
FM0041 | ForgeMap | Error | Collection method has no matching element forge method
FM0042 | ForgeMap | Error | Ambiguous collection method
FM0043 | ForgeMap | Error | [AfterForge] method not found or has wrong signature
FM0044 | ForgeMap | Error | [AfterForge] and [ConvertWith] are mutually exclusive
FM0045 | ForgeMap | Error | [AfterForge] is not applicable to collection methods

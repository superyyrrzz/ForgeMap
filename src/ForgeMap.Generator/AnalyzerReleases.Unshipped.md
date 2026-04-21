; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FM0055 | ForgeMap | Error | SelectPropertySourceNotEnumerable
FM0056 | ForgeMap | Error | SelectPropertyMemberNotFound
FM0057 | ForgeMap | Error | SelectPropertyElementTypeIncompatible
FM0058 | ForgeMap | Error | SelectPropertyConflictsWithConverter
FM0059 | ForgeMap | Disabled | SelectPropertyApplied
FM0060 | ForgeMap | Error | ConditionAndSkipWhenBothSet
FM0061 | ForgeMap | Error | ConditionalPredicateInvalid
FM0062 | ForgeMap | Error | ConditionalNotSupportedOnInitOrCtor
FM0063 | ForgeMap | Error | ConditionalConflictsWithForgeFromOrWith
FM0064 | ForgeMap | Disabled | ConditionalAssignmentApplied
FM0072 | ForgeMap | Error | SelectPropertyConflictsWithForgeFromOrWith
FM0073 | ForgeMap | Error | SelectPropertyDestinationNotEnumerable
FM0075 | ForgeMap | Warning | SelectPropertyNotSupportedOnForgeInto
FM0065 | ForgeMap | Error | ExtractWrapConflictsWithMethodAttributes
FM0066 | ForgeMap | Error | ExtractPropertyNotFound
FM0067 | ForgeMap | Error | ExtractPropertyTypeIncompatible
FM0068 | ForgeMap | Error | WrapPropertyNotFound
FM0069 | ForgeMap | Error | WrapPropertyTypeIncompatible
FM0070 | ForgeMap | Error | ExtractWrapInvalidSignature
FM0071 | ForgeMap | Error | WrapPropertyRequiredMembersUnsatisfied
FM0074 | ForgeMap | Disabled | ExtractWrapValueTypeReturnUnderReturnNull

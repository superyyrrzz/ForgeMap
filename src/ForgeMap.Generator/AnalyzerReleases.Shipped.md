; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.7.0

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
FM0065 | ForgeMap | Error | ExtractWrapConflictsWithMethodAttributes
FM0066 | ForgeMap | Error | ExtractPropertyNotFound
FM0067 | ForgeMap | Error | ExtractPropertyTypeIncompatible
FM0068 | ForgeMap | Error | WrapPropertyNotFound
FM0069 | ForgeMap | Error | WrapPropertyTypeIncompatible
FM0070 | ForgeMap | Error | ExtractWrapInvalidSignature
FM0071 | ForgeMap | Error | WrapPropertyRequiredMembersUnsatisfied
FM0072 | ForgeMap | Error | SelectPropertyConflictsWithForgeFromOrWith
FM0073 | ForgeMap | Error | SelectPropertyDestinationNotEnumerable
FM0074 | ForgeMap | Disabled | ExtractWrapValueTypeReturnUnderReturnNull
FM0075 | ForgeMap | Warning | SelectPropertyNotSupportedOnForgeInto

## Release 1.6.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FM0046 | ForgeMap | Warning | Unmatched constructor parameter
FM0047 | ForgeMap | Error | Specified constructor not found
FM0048 | ForgeMap | Disabled | Constructor mapping routing info
FM0049 | ForgeMap | Disabled | Null-safe guard applied to string→enum conversion
FM0050 | ForgeMap | Disabled | Nullable-safe collection coercion applied
FM0051 | ForgeMap | Warning | Unsupported nullable collection coercion
FM0052 | ForgeMap | Warning | Per-property converter method not found
FM0053 | ForgeMap | Warning | Per-property converter signature mismatch
FM0054 | ForgeMap | Disabled | Per-property converter applied

## Release 1.5.0

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

## Release 1.4.0

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
FM0036 | ForgeMap | Warning | [ConvertWith] takes precedence over [ForgeProperty]/[ForgeFrom]/[ForgeWith]
FM0037 | ForgeMap | Error | [ConvertWith] member not found or incompatible

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



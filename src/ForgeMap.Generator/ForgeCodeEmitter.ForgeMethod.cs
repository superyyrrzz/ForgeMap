using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    /// <summary>
    /// Tries to generate an enum forging method. Returns null if source/dest are not enum/string combinations.
    /// Handles: enum→enum (cast by name), enum→string (.ToString()), string→enum (Enum.Parse).
    /// </summary>
    private string? TryGenerateEnumForgeMethod(IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol destinationType)
    {
        var isSourceEnum = sourceType.TypeKind == TypeKind.Enum;
        var isDestEnum = destinationType.TypeKind == TypeKind.Enum;
        var isSourceString = sourceType.SpecialType == SpecialType.System_String;
        var isDestString = destinationType.SpecialType == SpecialType.System_String;

        if (!isSourceEnum && !isDestEnum)
            return null;

        // At least one side must be enum; the other must be enum or string
        if (!isSourceEnum && !isSourceString)
            return null;
        if (!isDestEnum && !isDestString)
            return null;

        var sb = new StringBuilder();
        var sourceParam = method.Parameters[0].Name;
        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        var destDisplay = destinationType.ToDisplayString();
        var sourceDisplay = sourceType.ToDisplayString();

        sb.AppendLine($"        {accessibility} partial {destDisplay} {method.Name}({sourceDisplay} {sourceParam})");
        sb.AppendLine("        {");

        // Add null handling for reference-type inputs (string)
        if (isSourceString)
        {
            sb.AppendLine(GenerateNullCheck(sourceParam, "default"));
            sb.AppendLine();
        }

        if (isSourceEnum && isDestEnum)
        {
            // enum → enum: parse by name for safety (handles mismatched underlying values)
            sb.AppendLine($"            return ({destDisplay})global::System.Enum.Parse(typeof({destDisplay}), {sourceParam}.ToString());");
        }
        else if (isSourceEnum && isDestString)
        {
            // enum → string: .ToString()
            sb.AppendLine($"            return {sourceParam}.ToString();");
        }
        else if (isSourceString && isDestEnum)
        {
            // string → enum: Enum.Parse with case-insensitive matching
            sb.AppendLine($"            return ({destDisplay})global::System.Enum.Parse(typeof({destDisplay}), {sourceParam}, true);");
        }

        sb.AppendLine("        }");
        return sb.ToString();
    }

    private string GenerateForgeMethod(
        IMethodSymbol method,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol destinationType,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        if (sourceType == null)
            return string.Empty;

        // Check for [ForgeAllDerived] — polymorphic dispatch
        var hasForgeAllDerived = HasForgeAllDerivedAttribute(method);

        if (hasForgeAllDerived)
        {
            // FM0023: [ForgeAllDerived] cannot be combined with [ConvertWith]
            if (HasConvertWithAttribute(method))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ForgeAllDerivedWithConvertWith,
                    method.Locations.FirstOrDefault(),
                    method.Name);

                // Emit a minimal throwing body so the partial method still compiles
                var errAccessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
                var errSb = new StringBuilder();
                errSb.AppendLine($"        {errAccessibility} partial {destinationType.ToDisplayString()} {method.Name}({sourceType.ToDisplayString()} {method.Parameters[0].Name})");
                errSb.AppendLine("        {");
                errSb.AppendLine("            throw new global::System.NotSupportedException(\"[ForgeAllDerived] cannot be combined with [ConvertWith].\");");
                errSb.AppendLine("        }");
                return errSb.ToString();
            }
        }

        var sb = new StringBuilder();
        var sourceParam = method.Parameters[0].Name;

        // Resolve all attribute-based configuration
        var cfg = ResolveMethodConfig(method, sourceType, destinationType, forger, context);
        var ignoredProperties = cfg.IgnoredProperties;
        var propertyMappings = cfg.PropertyMappings;
        var resolverMappings = cfg.ResolverMappings;
        var forgeWithMappings = cfg.ForgeWithMappings;
        var beforeForgeHooks = cfg.BeforeForgeHooks;
        var afterForgeHooks = cfg.AfterForgeHooks;
        var nullPropertyHandlingOverrides = cfg.NullPropertyHandlingOverrides;

        // FM0028: ExistingTarget = true is only valid on [UseExistingValue] mutation methods
        if (cfg.ExistingTargetProperties.Count > 0)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExistingTargetOnNonMutationMethod,
                method.Locations.FirstOrDefault());
        }

        var hasAfterForge = afterForgeHooks.Count > 0;

        // Method signature
        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        sb.AppendLine($"        {accessibility} partial {destinationType.ToDisplayString()} {method.Name}({sourceType.ToDisplayString()} {sourceParam})");
        sb.AppendLine("        {");

        // Null check (only for reference types or nullable value types)
        if (sourceType.IsReferenceType || sourceType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var nullReturn = destinationType.IsValueType ? "default" : "null!";
            sb.AppendLine(GenerateNullCheck(sourceParam, nullReturn));
            sb.AppendLine();
        }

        // [ForgeAllDerived] — polymorphic dispatch is-cascade
        if (hasForgeAllDerived)
        {
            var derivedMethods = DiscoverDerivedForgeMethods(method, sourceType, destinationType, forger);
            var isAbstractOrInterface = destinationType.IsAbstract || destinationType.TypeKind == TypeKind.Interface;

            if (derivedMethods.Count == 0)
            {
                // FM0022: no derived forge methods found — use abstract-specific message when applicable
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ForgeAllDerivedNoDerivedMethods,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    isAbstractOrInterface
                        ? "dispatch-only body has no base-type fallback \u2014 all non-null inputs will throw NotSupportedException"
                        : "polymorphic dispatch will only map the base type");
            }
            else
            {
                sb.AppendLine("            // Polymorphic dispatch — most-derived types checked first");

                // Normalize identifiers so that `@foo` and `foo` are treated as the same name in C#
                static string NormalizeIdentifier(string name) =>
                    name.Length > 0 && name[0] == '@' ? name.Substring(1) : name;

                var usedNames = new HashSet<string>(StringComparer.Ordinal)
                {
                    NormalizeIdentifier(sourceParam),
                    NormalizeIdentifier("result")
                };
                foreach (var derived in derivedMethods)
                {
                    var derivedSourceDisplay = derived.Parameters[0].Type.ToDisplayString();
                    var displayName = GenerateSafeVariableName(derived.Parameters[0].Type);
                    var baseName = NormalizeIdentifier(displayName);
                    var varName = baseName;
                    if (!usedNames.Add(varName))
                    {
                        var suffix = 2;
                        do { varName = baseName + suffix++; } while (!usedNames.Add(varName));
                    }
                    // Re-apply @ escaping if the original name needed it and no suffix was added
                    var finalName = (displayName.Length > 0 && displayName[0] == '@' && varName == baseName)
                        ? displayName
                        : varName;
                    sb.AppendLine($"            if ({sourceParam} is {derivedSourceDisplay} {finalName}) return {method.Name}({finalName});");
                }
                sb.AppendLine();

                // FM0024: warn about abstract/interface destination — unmatched subtypes throw at runtime
                if (isAbstractOrInterface)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.ForgeAllDerivedAbstractDestination,
                        method.Locations.FirstOrDefault(),
                        destinationType.ToDisplayString());
                }
            }

            // Abstract/interface destinations: dispatch-only body with throw fallback — no base-type mapping
            if (isAbstractOrInterface)
            {
                var destDisplayName = destinationType.ToDisplayString();
                sb.AppendLine($"            throw new global::System.NotSupportedException(");
                sb.AppendLine($"                $\"No forge mapping for source type '{{({sourceParam}).GetType().FullName}}' \" +");
                sb.AppendLine($"                $\"to non-instantiable destination type '{destDisplayName}'.\");");
                sb.AppendLine("        }");
                return sb.ToString();
            }
        }

        // [BeforeForge] callbacks
        foreach (var hookName in beforeForgeHooks)
        {
            sb.AppendLine($"            {hookName}({sourceParam});");
        }
        if (beforeForgeHooks.Count > 0)
            sb.AppendLine();

        // Get mappable properties
        var sourceProperties = GetMappableProperties(sourceType);
        var destProperties = GetMappableProperties(destinationType);

        // Determine constructor strategy
        var (chosenCtor, ctorParamMappings) = ResolveConstructor(
            destinationType, sourceType, sourceProperties, propertyMappings, context, method, forger);

        // Track which destination properties are covered by constructor parameters
        var ctorCoveredDestProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ctorParamMappings != null)
        {
            foreach (var mapping in ctorParamMappings)
                ctorCoveredDestProps.Add(mapping.DestPropertyName);
        }

        if (chosenCtor != null && ctorParamMappings != null && chosenCtor.Parameters.Length > 0)
        {
            // Constructor mapping: generate new Dest(param1: expr1, param2: expr2) { Prop = value, ... }
            // Using object initializer syntax so init-only properties work too

            // Emit pre-construction blocks for inline collection ctor params
            foreach (var mapping in ctorParamMappings)
            {
                if (mapping.PreConstructionBlock != null)
                    sb.AppendLine(mapping.PreConstructionBlock);
            }

            // Collect remaining property assignments for object initializer
            var remainingDestProps = destProperties
                .Where(p => p.SetMethod != null && p.SetMethod.DeclaredAccessibility >= Accessibility.Internal && !ctorCoveredDestProps.Contains(p.Name))
                .ToList();

            var initAssignments = new List<(string Name, string Expr)>();
            var skipNullAssignmentsForCtor = new List<(string DestPropName, string SourceExpr, string LocalVarName, string? AssignExpr)>();
            var postConstructionCollectionsForCtor = new List<(string DestPropName, string Block)>();
            var preConstructionBlocksForCtor = new List<string>();
            foreach (var destProp in remainingDestProps)
            {
                if (ignoredProperties.Contains(destProp.Name))
                    continue;

                var assignment = GeneratePropertyAssignment(
                    destProp, sourceParam, sourceType, sourceProperties,
                    propertyMappings, resolverMappings, forgeWithMappings, ignoredProperties, forger, context, method,
                    nullPropertyHandlingOverrides, skipNullAssignmentsForCtor,
                    postConstructionCollectionsForCtor, preConstructionBlocksForCtor);

                if (assignment != null)
                    initAssignments.Add((destProp.Name, assignment));
            }

            // Emit pre-construction blocks for init-only collection properties
            foreach (var block in preConstructionBlocksForCtor)
                sb.AppendLine(block);

            sb.AppendLine($"            var result = new {destinationType.ToDisplayString()}(");

            for (int i = 0; i < ctorParamMappings.Count; i++)
            {
                var mapping = ctorParamMappings[i];
                var separator = i < ctorParamMappings.Count - 1 ? "," : "";
                var expr = GenerateCtorParamExpression(
                    mapping.SourceExpression, mapping.SourcePropertyType, mapping.DestPropertyType,
                    mapping.DestPropertyName, destinationType.ToDisplayString(),
                    nullPropertyHandlingOverrides, context, method);
                sb.AppendLine($"                {mapping.CtorParamName}: {expr}{separator}");
            }

            if (initAssignments.Count > 0)
            {
                sb.AppendLine("            )");
                sb.AppendLine("            {");
                foreach (var (name, expr) in initAssignments)
                    sb.AppendLine($"                {name} = {expr},");
                sb.AppendLine("            };");
            }
            else
            {
                sb.AppendLine("            );");
            }

            // SkipNull properties — emit separate if-guard statements after the initializer
            foreach (var (destName, srcExpr, localVar, assignExpr) in skipNullAssignmentsForCtor)
            {
                sb.AppendLine($"            if ({srcExpr} is {{ }} {localVar})");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                result.{destName} = {assignExpr ?? localVar};");
                sb.AppendLine($"            }}");
            }

            // Post-construction collection mappings
            foreach (var (destName, block) in postConstructionCollectionsForCtor)
                sb.AppendLine(block);

            // [AfterForge] callbacks
            foreach (var hookName in afterForgeHooks)
            {
                sb.AppendLine($"            {hookName}({sourceParam}, result);");
            }

            sb.AppendLine("            return result;");
        }
        else if (hasAfterForge)
        {
            // When AfterForge hooks exist, we need a variable to pass to the hooks
            var skipNullAssignmentsAfterForge = new List<(string DestPropName, string SourceExpr, string LocalVarName, string? AssignExpr)>();
            var postConstructionCollectionsAfterForge = new List<(string DestPropName, string Block)>();
            var preConstructionBlocksAfterForge = new List<string>();

            var afterForgeAssignments = new List<(string Name, string Expr)>();
            foreach (var destProp in destProperties.Where(p => p.SetMethod != null && p.SetMethod.DeclaredAccessibility >= Accessibility.Internal))
            {
                var assignment = GeneratePropertyAssignment(
                    destProp, sourceParam, sourceType, sourceProperties,
                    propertyMappings, resolverMappings, forgeWithMappings, ignoredProperties, forger, context, method,
                    nullPropertyHandlingOverrides, skipNullAssignmentsAfterForge,
                    postConstructionCollectionsAfterForge, preConstructionBlocksAfterForge);

                if (assignment != null)
                    afterForgeAssignments.Add((destProp.Name, assignment));
            }

            // Emit pre-construction blocks for init-only collection properties
            foreach (var block in preConstructionBlocksAfterForge)
                sb.AppendLine(block);

            sb.AppendLine($"            var result = new {destinationType.ToDisplayString()}");
            sb.AppendLine("            {");

            foreach (var (name, expr) in afterForgeAssignments)
                sb.AppendLine($"                {name} = {expr},");

            sb.AppendLine("            };");

            // SkipNull properties — emit separate if-guard statements
            foreach (var (destName, srcExpr, localVar, assignExpr) in skipNullAssignmentsAfterForge)
            {
                sb.AppendLine($"            if ({srcExpr} is {{ }} {localVar})");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                result.{destName} = {assignExpr ?? localVar};");
                sb.AppendLine($"            }}");
            }

            // Post-construction collection mappings
            foreach (var (destName, block) in postConstructionCollectionsAfterForge)
                sb.AppendLine(block);

            // [AfterForge] callbacks
            foreach (var hookName in afterForgeHooks)
            {
                sb.AppendLine($"            {hookName}({sourceParam}, result);");
            }

            sb.AppendLine("            return result;");
        }
        else
        {
            // Object initializer pattern
            var skipNullAssignmentsPlain = new List<(string DestPropName, string SourceExpr, string LocalVarName, string? AssignExpr)>();
            var postConstructionCollectionsPlain = new List<(string DestPropName, string Block)>();
            var preConstructionBlocksPlain = new List<string>();
            var plainAssignments = new List<(string Name, string Expr)>();

            foreach (var destProp in destProperties.Where(p => p.SetMethod != null && p.SetMethod.DeclaredAccessibility >= Accessibility.Internal))
            {
                var assignment = GeneratePropertyAssignment(
                    destProp, sourceParam, sourceType, sourceProperties,
                    propertyMappings, resolverMappings, forgeWithMappings, ignoredProperties, forger, context, method,
                    nullPropertyHandlingOverrides, skipNullAssignmentsPlain,
                    postConstructionCollectionsPlain, preConstructionBlocksPlain);

                if (assignment != null)
                    plainAssignments.Add((destProp.Name, assignment));
            }

            var needsResultVar = skipNullAssignmentsPlain.Count > 0 ||
                postConstructionCollectionsPlain.Count > 0 ||
                preConstructionBlocksPlain.Count > 0;

            if (needsResultVar)
            {
                // Emit pre-construction blocks for init-only collection properties
                foreach (var block in preConstructionBlocksPlain)
                    sb.AppendLine(block);

                sb.AppendLine($"            var result = new {destinationType.ToDisplayString()}");
                sb.AppendLine("            {");
                foreach (var (name, expr) in plainAssignments)
                    sb.AppendLine($"                {name} = {expr},");
                sb.AppendLine("            };");

                foreach (var (destName, srcExpr, localVar, assignExpr) in skipNullAssignmentsPlain)
                {
                    sb.AppendLine($"            if ({srcExpr} is {{ }} {localVar})");
                    sb.AppendLine($"            {{");
                    sb.AppendLine($"                result.{destName} = {assignExpr ?? localVar};");
                    sb.AppendLine($"            }}");
                }

                // Post-construction collection mappings
                foreach (var (destName, block) in postConstructionCollectionsPlain)
                    sb.AppendLine(block);

                sb.AppendLine("            return result;");
            }
            else
            {
                sb.AppendLine($"            return new {destinationType.ToDisplayString()}");
                sb.AppendLine("            {");
                foreach (var (name, expr) in plainAssignments)
                    sb.AppendLine($"                {name} = {expr},");
                sb.AppendLine("            };");
            }
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Resolves the constructor to use for destination type instantiation.
    /// Returns (null, null) if a parameterless constructor should be used (object initializer pattern).
    /// </summary>
    private (IMethodSymbol? Constructor, List<CtorParamMapping>? Mappings) ResolveConstructor(
        INamedTypeSymbol destinationType,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        SourceProductionContext context,
        IMethodSymbol method,
        ForgerInfo? forger = null)
    {
        var constructors = destinationType.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        // If a parameterless constructor exists, prefer it (object initializer pattern)
        var parameterlessCtor = constructors.FirstOrDefault(c => c.Parameters.Length == 0);
        if (parameterlessCtor != null)
            return (null, null);

        if (constructors.Count == 0)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.DestinationTypeHasNoConstructor,
                method.Locations.FirstOrDefault(),
                destinationType.Name);
            return (null, null);
        }

        // Build a reverse map: dest property name → source expression
        var sourcePropertiesList = sourceProperties.ToList();
        var destToSourceExpr = BuildDestToSourceMap(sourceType, sourcePropertiesList, propertyMappings);

        // Score each constructor by how many parameters can be satisfied
        var scoredCtors = new List<(IMethodSymbol Ctor, List<CtorParamMapping> Mappings, int Score, List<Action> DeferredDiagnostics)>();

        foreach (var ctor in constructors)
        {
            var mappings = new List<CtorParamMapping>();
            var deferredDiagnostics = new List<Action>();
            var allMatched = true;

            foreach (var param in ctor.Parameters)
            {
                var paramName = param.Name;

                // Try case-insensitive match against destination property names first
                // (constructor parameter matching is always case-insensitive per spec)
                string? matchedDestPropName = null;
                string? sourceExpr = null;
                ITypeSymbol? sourcePropType = null;

                // Check if any dest prop matches this ctor param (case-insensitive)
                foreach (var kvp in destToSourceExpr)
                {
                    if (string.Equals(kvp.Key, paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedDestPropName = kvp.Key;
                        sourceExpr = kvp.Value.Expression;
                        sourcePropType = kvp.Value.Type;
                        break;
                    }
                }

                // Also try direct source property match (case-insensitive)
                if (sourceExpr == null)
                {
                    var directMatch = sourcePropertiesList.FirstOrDefault(sp =>
                        string.Equals(sp.Name, paramName, StringComparison.OrdinalIgnoreCase));
                    if (directMatch != null)
                    {
                        matchedDestPropName = paramName;
                        sourceExpr = $"source.{directMatch.Name}";
                        sourcePropType = directMatch.Type;
                    }
                }

                if (sourceExpr != null && sourcePropType != null &&
                    (CanAssign(sourcePropType, param.Type) || IsCompatibleEnumPair(sourcePropType, param.Type)
                     || (_config.StringToEnum != 2 && IsStringToEnumPair(sourcePropType, param.Type))
                     || IsEnumToStringPair(sourcePropType, param.Type)))
                {
                    mappings.Add(new CtorParamMapping(param.Name, matchedDestPropName!, sourceExpr, sourcePropType, param.Type));
                }
                else if (sourceExpr != null && sourcePropType != null &&
                    _config.AutoWireNestedMappings && forger != null &&
                    !IsScalarType(sourcePropType) && !IsScalarType(param.Type))
                {
                    // Try inline collection auto-wire for ctor parameters first
                    var srcElemType = GetCollectionElementType(sourcePropType);
                    var destElemType = GetCollectionElementType(param.Type);
                    if (srcElemType != null && destElemType != null)
                    {
                        var collLevelCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, sourcePropType, param.Type);
                        if (collLevelCandidates.Count == 0)
                        {
                            var elemCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, srcElemType, destElemType);
                            if (elemCandidates.Count == 1)
                            {
                                var elemMethod = elemCandidates[0];
                                var capturedDestPropName = matchedDestPropName!;
                                var capturedMethodName = elemMethod.Name;
                                deferredDiagnostics.Add(() => ReportDiagnosticIfNotSuppressed(context,
                                    DiagnosticDescriptors.PropertyAutoWired,
                                    method.Locations.FirstOrDefault(),
                                    capturedDestPropName, capturedMethodName));

                                var collLocal = $"__coll_{param.Name}";
                                var inlineResult = GenerateInlineCollectionCode(
                                    sourcePropType, param.Type, destElemType,
                                    collLocal, param.Name, elemMethod);

                                if (inlineResult != null)
                                {
                                    var (ctorCollKind, ctorCollCode) = inlineResult.Value;
                                    if (ctorCollKind == CollectionInlineKind.SingleExpression)
                                    {
                                        var nullFallback = param.Type.IsValueType ? "default" : "null!";
                                        var ctorCollExpr = $"{sourceExpr} is {{ }} {collLocal} ? {ctorCollCode} : {nullFallback}";
                                        mappings.Add(new CtorParamMapping(param.Name, matchedDestPropName!, ctorCollExpr, param.Type, param.Type));
                                    }
                                    else
                                    {
                                        // Multi-statement: build into local variable, use local in ctor expression
                                        var resultVar = $"__collResult_{param.Name}";
                                        var preBlock = new StringBuilder();
                                        var destTypeDisplay = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                                        preBlock.AppendLine($"            {destTypeDisplay}? __collInit_{param.Name} = null;");
                                        preBlock.AppendLine($"            if ({sourceExpr} is {{ }} {collLocal})");
                                        preBlock.AppendLine($"            {{");
                                        preBlock.AppendLine(ctorCollCode);
                                        preBlock.AppendLine($"                __collInit_{param.Name} = {resultVar};");
                                        preBlock.Append($"            }}");

                                        var mapping = new CtorParamMapping(param.Name, matchedDestPropName!, $"__collInit_{param.Name}!", param.Type, param.Type);
                                        mapping.PreConstructionBlock = preBlock.ToString();
                                        mappings.Add(mapping);
                                    }
                                    continue;
                                }
                            }
                            else if (elemCandidates.Count == 0 &&
                                SymbolEqualityComparer.Default.Equals(srcElemType, destElemType))
                            {
                                // Pure container coercion for ctor parameters (same element types)
                                var collLocal = $"__coll_{param.Name}";
                                var coercionExpr = TryGenerateSequenceCoercion(sourcePropType, param.Type, srcElemType, collLocal);
                                if (coercionExpr != null)
                                {
                                    var nullFallback = param.Type.IsValueType ? "default" : "null!";
                                    var ctorCollExpr = $"{sourceExpr} is {{ }} {collLocal} ? {coercionExpr} : {nullFallback}";
                                    mappings.Add(new CtorParamMapping(param.Name, matchedDestPropName!, ctorCollExpr, param.Type, param.Type));
                                    continue;
                                }
                            }
                            else if (elemCandidates.Count > 1)
                            {
                                var capturedDestPropName2 = matchedDestPropName!;
                                deferredDiagnostics.Add(() => ReportDiagnosticIfNotSuppressed(context,
                                    DiagnosticDescriptors.AmbiguousAutoWire,
                                    method.Locations.FirstOrDefault(),
                                    capturedDestPropName2, destinationType.Name));
                                allMatched = false;
                                break;
                            }
                        }
                    }

                    // Try dictionary coercion for ctor parameters (dictionaries have 2 type args, not in SupportedCollectionTypes)
                    var srcDictTypes = GetDictionaryKeyValueTypes(sourcePropType);
                    var destDictTypes = GetDictionaryKeyValueTypes(param.Type);
                    if (srcDictTypes != null && destDictTypes != null &&
                        SymbolEqualityComparer.Default.Equals(srcDictTypes.Value.KeyType, destDictTypes.Value.KeyType) &&
                        SymbolEqualityComparer.Default.Equals(srcDictTypes.Value.ValueType, destDictTypes.Value.ValueType) &&
                        !CanAssign(sourcePropType, param.Type))
                    {
                        var dictCollLocal = $"__coll_{param.Name}";
                        var dictCoercionExpr = TryGenerateDictionaryCoercion(sourcePropType, param.Type,
                            srcDictTypes.Value.KeyType, srcDictTypes.Value.ValueType, dictCollLocal, param.Name);
                        if (dictCoercionExpr != null)
                        {
                            var nullFallback = param.Type.IsValueType ? "default" : "null!";
                            var ctorDictExpr = $"{sourceExpr} is {{ }} {dictCollLocal} ? {dictCoercionExpr} : {nullFallback}";
                            mappings.Add(new CtorParamMapping(param.Name, matchedDestPropName!, ctorDictExpr, param.Type, param.Type));
                            continue;
                        }
                    }

                    // Try auto-wiring for ctor parameters with non-assignable complex types
                    var candidates = FindAutoWireForgeMethodCandidates(forger.Symbol, sourcePropType, param.Type);
                    if (candidates.Count == 1)
                    {
                        var matchedMethod = candidates[0];
                        // Defer FM0027 (info, off by default) — only report for winning ctor
                        var capturedDestPropName = matchedDestPropName!;
                        var capturedMethodName = matchedMethod.Name;
                        deferredDiagnostics.Add(() => ReportDiagnosticIfNotSuppressed(context,
                            DiagnosticDescriptors.PropertyAutoWired,
                            method.Locations.FirstOrDefault(),
                            capturedDestPropName, capturedMethodName));
                        // Generate auto-wire expression (same form as [ForgeWith] ctor params)
                        string autoWireExpr;
                        if (sourcePropType.IsReferenceType)
                        {
                            var localVarName = $"__autoWire_{param.Name}";
                            var nullFallback = param.Type.IsValueType ? "default" : "null!";
                            autoWireExpr = $"{sourceExpr} is {{ }} {localVarName} ? {matchedMethod.Name}({localVarName}) : {nullFallback}";
                        }
                        else
                        {
                            autoWireExpr = $"{matchedMethod.Name}({sourceExpr})";
                        }
                        mappings.Add(new CtorParamMapping(param.Name, matchedDestPropName!, autoWireExpr, param.Type, param.Type));
                    }
                    else
                    {
                        if (candidates.Count > 1)
                        {
                            // FM0025: ambiguous — defer until ctor selection is finalized
                            var capturedDestPropName2 = matchedDestPropName!;
                            deferredDiagnostics.Add(() => ReportDiagnosticIfNotSuppressed(context,
                                DiagnosticDescriptors.AmbiguousAutoWire,
                                method.Locations.FirstOrDefault(),
                                capturedDestPropName2, destinationType.Name));
                        }
                        allMatched = false;
                        break;
                    }
                }
                else
                {
                    allMatched = false;
                    break;
                }
            }

            if (allMatched)
            {
                scoredCtors.Add((ctor, mappings, ctor.Parameters.Length, deferredDiagnostics));
            }
        }

        if (scoredCtors.Count == 0)
        {
            // No fully-matched ctor. Report FM0014 for the constructor with the most params.
            var bestCtor = constructors.OrderByDescending(c => c.Parameters.Length).First();
            foreach (var param in bestCtor.Parameters)
            {
                var found = destToSourceExpr.Keys.Any(k => string.Equals(k, param.Name, StringComparison.OrdinalIgnoreCase))
                    || sourcePropertiesList.Any(sp => string.Equals(sp.Name, param.Name, StringComparison.OrdinalIgnoreCase));
                if (!found)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.ConstructorParameterNotMatched,
                        method.Locations.FirstOrDefault(),
                        param.Name,
                        destinationType.Name);
                }
            }
            return (null, null);
        }

        // Pick best (most parameters matched)
        var maxScore = scoredCtors.Max(s => s.Score);
        var bestMatches = scoredCtors.Where(s => s.Score == maxScore).ToList();

        if (bestMatches.Count > 1)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.AmbiguousConstructor,
                method.Locations.FirstOrDefault(),
                destinationType.Name);
            return (null, null);
        }

        // Emit deferred diagnostics only for the winning constructor
        foreach (var deferredDiag in bestMatches[0].DeferredDiagnostics)
            deferredDiag();

        return (bestMatches[0].Ctor, bestMatches[0].Mappings);
    }

    /// <summary>
    /// Builds a mapping from destination property name → source expression info.
    /// Includes [ForgeProperty] mappings and direct name matches.
    /// </summary>
    private Dictionary<string, (string Expression, ITypeSymbol? Type)> BuildDestToSourceMap(
        INamedTypeSymbol sourceType,
        List<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings)
    {
        var map = new Dictionary<string, (string Expression, ITypeSymbol? Type)>(StringComparer.OrdinalIgnoreCase);

        // Add all direct source property matches by name
        foreach (var sp in sourceProperties)
        {
            map[sp.Name] = ($"source.{sp.Name}", sp.Type);
        }

        // Overlay [ForgeProperty] mappings (dest name → source path)
        foreach (var kvp in propertyMappings)
        {
            var destPropName = kvp.Key;
            var sourcePath = kvp.Value;
            var leafType = ResolvePathLeafType(sourcePath, sourceType);

            if (sourcePath.Contains("."))
            {
                var (expr, _) = GenerateSourceExpressionWithNullInfo("source", sourcePath, sourceType);
                map[destPropName] = (expr, leafType);
            }
            else
            {
                // Use symbol name when available for defense-in-depth
                var simpleProp = sourceProperties.FirstOrDefault(p => p.Name == sourcePath);
                var safeName = simpleProp?.Name ?? sourcePath;
                map[destPropName] = ($"source.{safeName}", leafType);
            }
        }

        return map;
    }

    /// <summary>
    /// Generates a forge method body that delegates to a [ConvertWith] converter.
    /// </summary>
    private string GenerateConvertWithMethod(
        IMethodSymbol method,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol destinationType,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        var convertWithInfo = GetConvertWithInfo(method);
        if (convertWithInfo == null)
            return string.Empty;

        var info = convertWithInfo.Value;
        var sourceParam = method.Parameters[0].Name;
        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        var destDisplay = destinationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var sourceDisplay = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // FM0036: warn if [ForgeProperty]/[ForgeFrom]/[ForgeWith] are also present
        var hasPropertyAttrs = GetPropertyMappings(method).Count > 0 ||
                               GetResolverMappings(method).Count > 0 ||
                               GetForgeWithMappings(method).Count > 0;
        if (hasPropertyAttrs)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ConvertWithIgnoresPropertyAttributes,
                method.Locations.FirstOrDefault(),
                method.Name);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"        {accessibility} partial {destDisplay} {method.Name}({sourceDisplay} {sourceParam})");
        sb.AppendLine("        {");

        // Null check for nullable inputs (reference types and Nullable<T>)
        if (sourceType.IsReferenceType || sourceType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var nullReturn = destinationType.IsValueType ? "default" : "null!";
            sb.AppendLine(GenerateNullCheck(sourceParam, nullReturn));
            sb.AppendLine();
        }

        if (info.ConverterType != null)
        {
            // Type-based converter path
            var converterType = info.ConverterType;
            var converterDisplay = converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Validate ITypeConverter<TSource, TDest>
            if (!ImplementsITypeConverter(converterType, sourceType, destinationType))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ConvertWithTypeDoesNotImplementInterface,
                    method.Locations.FirstOrDefault(),
                    converterDisplay,
                    sourceDisplay,
                    destDisplay);

                sb.AppendLine($"            throw new global::System.NotSupportedException(\"[ConvertWith] type does not implement ITypeConverter.\");");
                sb.AppendLine("        }");
                return sb.ToString();
            }

            // Detect DI capability on the forger
            string? scopeFactoryField = _iServiceScopeFactorySymbol != null
                ? FindFieldByType(forger.Symbol, _iServiceScopeFactorySymbol)
                : null;
            string? serviceProviderField = _iServiceProviderSymbol != null
                ? FindFieldByType(forger.Symbol, _iServiceProviderSymbol)
                : null;

            if (scopeFactoryField != null)
            {
                // IServiceScopeFactory DI — scoped resolution
                sb.AppendLine($"            using var __scope = this.{scopeFactoryField}.CreateScope();");
                sb.AppendLine($"            return (({converterDisplay})global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(__scope.ServiceProvider, typeof({converterDisplay}))).Convert({sourceParam});");
            }
            else if (serviceProviderField != null)
            {
                // IServiceProvider DI — direct resolution via IServiceProvider.GetService (BCL method, no DI package required)
                sb.AppendLine($"            var __converter = ({converterDisplay})this.{serviceProviderField}.GetService(typeof({converterDisplay}));");
                sb.AppendLine($"            if (__converter == null) throw new global::System.InvalidOperationException(\"No service for type '\" + typeof({converterDisplay}).FullName + \"' has been registered.\");");
                sb.AppendLine($"            return __converter.Convert({sourceParam});");
            }
            else
            {
                // No DI — require public parameterless constructor (must be public for cross-assembly access)
                var hasParameterlessCtor = converterType.InstanceConstructors.Any(c =>
                    c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

                if (!hasParameterlessCtor)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.ConvertWithNoParameterlessConstructor,
                        method.Locations.FirstOrDefault(),
                        converterDisplay);

                    sb.AppendLine($"            throw new global::System.NotSupportedException(\"[ConvertWith] converter has no accessible parameterless constructor.\");");
                    sb.AppendLine("        }");
                    return sb.ToString();
                }

                sb.AppendLine($"            return new {converterDisplay}().Convert({sourceParam});");
            }
        }
        else if (info.MemberName != null)
        {
            // Member-based converter path — find field/property by name
            var memberName = info.MemberName;
            ITypeSymbol? memberType = null;

            foreach (var member in forger.Symbol.GetMembers())
            {
                if (member is IFieldSymbol field && !field.IsStatic && field.Name == memberName)
                {
                    memberType = field.Type;
                    break;
                }
                if (member is IPropertySymbol prop && !prop.IsStatic && prop.Name == memberName)
                {
                    memberType = prop.Type;
                    break;
                }
            }

            if (memberType == null || !ImplementsITypeConverter(memberType, sourceType, destinationType))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ConvertWithMemberNotFound,
                    method.Locations.FirstOrDefault(),
                    memberName,
                    sourceDisplay,
                    destDisplay);

                sb.AppendLine($"            throw new global::System.NotSupportedException(\"[ConvertWith] member not found or incompatible.\");");
                sb.AppendLine("        }");
                return sb.ToString();
            }

            sb.AppendLine($"            return this.{memberName}.Convert({sourceParam});");
        }

        sb.AppendLine("        }");
        return sb.ToString();
    }

    private sealed class CtorParamMapping
    {
        public CtorParamMapping(string ctorParamName, string destPropertyName, string sourceExpression, ITypeSymbol? sourcePropertyType, ITypeSymbol destPropertyType)
        {
            CtorParamName = ctorParamName;
            DestPropertyName = destPropertyName;
            SourceExpression = sourceExpression;
            SourcePropertyType = sourcePropertyType;
            DestPropertyType = destPropertyType;
        }

        public string CtorParamName { get; }
        public string DestPropertyName { get; }
        public string SourceExpression { get; }
        public ITypeSymbol? SourcePropertyType { get; }
        public ITypeSymbol DestPropertyType { get; }
        public string? PreConstructionBlock { get; set; }
    }
}

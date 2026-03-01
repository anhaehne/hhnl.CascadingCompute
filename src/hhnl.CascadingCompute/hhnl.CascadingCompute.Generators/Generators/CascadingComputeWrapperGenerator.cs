using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace hhnl.CascadingCompute.Generators.Generators;

[Generator]
public sealed class CascadingComputeWrapperGenerator : IIncrementalGenerator
{
    private static readonly Type[] _ignoredParameterTypes = [typeof(CancellationToken)];
    private const string AttributeMetadataName = "hhnl.CascadingCompute.Shared.Attributes.CascadingComputeAttribute";
    private const string CacheEntryLifetimeObserverAttributeMetadataName = "hhnl.CascadingCompute.Shared.Attributes.CacheEntryLifetimeObserverAttribute";
    private const string CacheContextProviderInterfaceNamespace = "hhnl.CascadingCompute.Shared.Interfaces";
    private const string CacheContextProviderInterfaceName = "ICacheContextProvider";
    private const string IgnoreParameterAttributeMetadataName = "hhnl.CascadingCompute.Attributes.CascadingComputeIgnoreParameterAttribute";
    private const string IgnoreAttributeMetadataName = "hhnl.CascadingCompute.Attributes.CascadingComputeIgnoreAttribute";

    private static readonly DiagnosticDescriptor ClassMustBePartial = new(
        "CCG001",
        "Cascading compute wrapper requires partial class",
        "Class '{0}' must be declared partial to generate CascadingComputeWrapper",
        "CascadingCompute",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMethod = new(
        "CCG002",
        "Cascading compute wrapper method unsupported",
        "Method '{0}' is not supported for CascadingCompute wrapper generation",
        "CascadingCompute",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InterfaceMustBePartial = new(
        "CCG003",
        "Cascading compute wrapper requires partial interface",
        "Interface '{0}' must be declared partial to generate CascadingCompute wrappers",
        "CascadingCompute",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MultipleCascadingInterfacesNotSupported = new(
        "CCG004",
        "Multiple cascading compute interfaces not supported",
        "Class '{0}' implements multiple interfaces with cascading compute methods; only one is supported",
        "CascadingCompute",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly IEqualityComparer<INamedTypeSymbol> NamedTypeSymbolComparer = new NamedTypeSymbolEqualityComparer();

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataName,
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => (IMethodSymbol)ctx.TargetSymbol);

        var classes = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node) as INamedTypeSymbol)
            .Where(static symbol => symbol is not null)!
            .Select(static (symbol, _) => symbol!);

        context.RegisterSourceOutput(methods.Collect().Combine(classes.Collect()), static (spc, data) => Execute(spc, data.Left, data.Right));
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<IMethodSymbol> methods, ImmutableArray<INamedTypeSymbol> classes)
    {
        if (methods.IsDefaultOrEmpty)
            return;

        var interfaceMethodsByType = methods
            .Where(method => method.ContainingType is { TypeKind: TypeKind.Interface })
            .GroupBy<IMethodSymbol, INamedTypeSymbol>(method => method.ContainingType, NamedTypeSymbolComparer)
            .ToDictionary(group => group.Key, group => group.ToList(), NamedTypeSymbolComparer);

        var validInterfaces = new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>(NamedTypeSymbolComparer);
        foreach (var kvp in interfaceMethodsByType)
        {
            var interfaceSymbol = kvp.Key;
            var interfaceMethods = kvp.Value;
            if (!IsPartial(interfaceSymbol, out var nonPartialLocation))
            {
                context.ReportDiagnostic(Diagnostic.Create(InterfaceMustBePartial, nonPartialLocation, interfaceSymbol.ToDisplayString()));
                continue;
            }

            var supportedInterfaceMethods = new List<IMethodSymbol>();
            foreach (var method in interfaceMethods)
            {
                if (IsSupportedMethod(method))
                {
                    supportedInterfaceMethods.Add(method);
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedMethod, method.Locations.FirstOrDefault(), method.ToDisplayString()));
                }
            }

            if (supportedInterfaceMethods.Count == 0)
                continue;

            validInterfaces[interfaceSymbol] = supportedInterfaceMethods;

            if (!HasCascadingComputeProperty(interfaceSymbol))
            {
                var interfaceSource = GenerateInterfaceSource(interfaceSymbol, supportedInterfaceMethods);
                context.AddSource(GetInterfaceHintName(interfaceSymbol), SourceText.From(interfaceSource, Encoding.UTF8));
            }
        }

        var classMethodsByType = methods
            .Where(method => method.ContainingType is { TypeKind: TypeKind.Class })
            .GroupBy<IMethodSymbol, INamedTypeSymbol>(method => method.ContainingType, NamedTypeSymbolComparer)
            .ToDictionary(group => group.Key, group => group.ToList(), NamedTypeSymbolComparer);

        var distinctClasses = classes.Distinct(NamedTypeSymbolComparer).ToArray();
        foreach (var classSymbol in distinctClasses)
        {
            classMethodsByType.TryGetValue(classSymbol, out var classMethods);
            classMethods ??= [];

            var interfaceTypes = classSymbol.AllInterfaces
                .Where(interfaceSymbol => validInterfaces.ContainsKey((INamedTypeSymbol)interfaceSymbol.OriginalDefinition))
                .ToArray();

            if (classMethods.Count == 0 && interfaceTypes.Length == 0)
                continue;

            if (HasWrapper(classSymbol))
                continue;

            if (!IsPartial(classSymbol, out var nonPartialLocation))
            {
                context.ReportDiagnostic(Diagnostic.Create(ClassMustBePartial, nonPartialLocation, classSymbol.ToDisplayString()));
                continue;
            }

            var methodSet = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var method in classMethods)
            {
                if (IsSupportedMethod(method))
                {
                    methodSet.Add(method);
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedMethod, method.Locations.FirstOrDefault(), method.ToDisplayString()));
                }
            }

            foreach (var interfaceSymbol in interfaceTypes)
            {
                var interfaceDefinition = (INamedTypeSymbol)interfaceSymbol.OriginalDefinition;
                foreach (var interfaceMethod in validInterfaces[interfaceDefinition])
                {
                    var interfaceMember = GetInterfaceMethod(interfaceSymbol, interfaceMethod);
                    if (interfaceMember is not null
                        && classSymbol.FindImplementationForInterfaceMember(interfaceMember) is IMethodSymbol implementation)
                        methodSet.Add(implementation);
                }
            }

            if (methodSet.Count == 0)
                continue;

            INamedTypeSymbol? wrapperInterface = null;
            if (interfaceTypes.Length > 1)
                context.ReportDiagnostic(Diagnostic.Create(MultipleCascadingInterfacesNotSupported, classSymbol.Locations.FirstOrDefault(), classSymbol.ToDisplayString()));
            if (interfaceTypes.Length > 0)
                wrapperInterface = interfaceTypes[0];

            var source = GenerateWrapperSource(classSymbol, methodSet, HasCascadingComputeProperty(classSymbol), wrapperInterface);
            context.AddSource(GetHintName(classSymbol), SourceText.From(source, Encoding.UTF8));
        }
    }

    private static bool IsSupportedMethod(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Ordinary)
            return false;

        if (method.ReturnsVoid)
            return false;

        if (method.IsStatic)
            return false;

        return method.Parameters.All(parameter => parameter.RefKind == RefKind.None);
    }

    private static bool HasWrapper(INamedTypeSymbol typeSymbol)
        => typeSymbol.GetMembers("CascadingComputeWrapper").OfType<INamedTypeSymbol>().Any();

    private static bool HasCascadingComputeProperty(INamedTypeSymbol typeSymbol)
        => typeSymbol.GetMembers("CascadingCompute").OfType<IPropertySymbol>().Any();

    private static bool IsPartial(INamedTypeSymbol typeSymbol, out Location? nonPartialLocation)
    {
        foreach (var syntaxReference in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is TypeDeclarationSyntax typeDeclaration
                && !typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                nonPartialLocation = typeDeclaration.Identifier.GetLocation();
                return false;
            }
        }

        nonPartialLocation = typeSymbol.Locations.FirstOrDefault();
        return true;
    }

    private static string GenerateWrapperSource(INamedTypeSymbol typeSymbol, IEnumerable<IMethodSymbol> methods, bool hasProperty, INamedTypeSymbol? interfaceSymbol)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8600");
        sb.AppendLine("#pragma warning disable CS8603");
        sb.AppendLine("#pragma warning disable CS8604");
        sb.AppendLine("#pragma warning disable CS8605");

        if (!typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            sb.Append("namespace ");
            sb.AppendLine(typeSymbol.ContainingNamespace.ToDisplayString());
            sb.AppendLine("{");
        }

        var indent = string.Empty;
        foreach (var containingType in GetContainingTypes(typeSymbol))
        {
            sb.Append(indent);
            sb.Append(GetAccessibility(containingType.DeclaredAccessibility));
            sb.Append(" partial ");
            sb.Append(GetTypeKeyword(containingType));
            sb.Append(' ');
            sb.Append(containingType.Name);
            sb.Append(GetTypeParameters(containingType));
            sb.AppendLine();
            var constraints = GetTypeConstraints(containingType);
            if (constraints.Count > 0)
            {
                foreach (var constraint in constraints)
                {
                    sb.Append(indent);
                    sb.Append("    ");
                    sb.AppendLine(constraint);
                }
            }
            sb.Append(indent);
            sb.AppendLine("{");
            indent += "    ";
        }

        if (!hasProperty)
        {
            sb.Append(indent);
            if (interfaceSymbol is null)
            {
                sb.AppendLine("public CascadingComputeWrapper CascadingCompute => field ??= new CascadingComputeWrapper(this);");
            }
            else
            {
                sb.Append("public ");
                sb.Append(GetWrapperInterfaceTypeName(interfaceSymbol));
                sb.AppendLine(" CascadingCompute => field ??= new CascadingComputeWrapper(this);");
            }
            sb.AppendLine();
        }

        sb.Append(indent);
        sb.Append("public class CascadingComputeWrapper(");
        sb.Append(GetContainingTypeName(typeSymbol));
        sb.Append(" implementation)");
        if (interfaceSymbol is not null)
        {
            sb.Append(" : ");
            sb.Append(GetWrapperInterfaceTypeName(interfaceSymbol));
        }
        sb.AppendLine();
        sb.Append(indent);
        sb.AppendLine("{");

        var innerIndent = indent + "    ";
        var cacheFields = new List<string>();
        foreach (var method in methods)
        {
            var includedParameters = GetIncludedParameters(method);
            var cacheKeyType = GetCacheKeyType(method, includedParameters);
            var fieldName = GetCacheFieldName(method);
            cacheFields.Add(fieldName);

            var observerFieldName = fieldName + "Observers";
            var observerInstantiationExpressions = GetCacheEntryLifetimeObserverAttributes(method)
                .Select(CreateAttributeInstantiationExpression)
                .Where(static expression => expression is not null)
                .ToList();
            sb.Append(innerIndent);
            sb.Append("private readonly global::hhnl.CascadingCompute.Shared.Attributes.CacheEntryLifetimeObserverAttribute[] ");
            sb.Append(observerFieldName);
            if (observerInstantiationExpressions.Count == 0)
            {
                sb.AppendLine(" = global::System.Array.Empty<global::hhnl.CascadingCompute.Shared.Attributes.CacheEntryLifetimeObserverAttribute>();");
            }
            else
            {
                sb.Append(" = [");
                sb.Append(string.Join(", ", observerInstantiationExpressions!));
                sb.AppendLine("];\n");
            }

            sb.Append(innerIndent);
            sb.Append("private readonly global::hhnl.CascadingCompute.Caching.ValueCache<");
            sb.Append(GetContainingTypeName(typeSymbol));
            sb.Append(", ");
            sb.Append(cacheKeyType);
            sb.Append(", ");
            sb.Append(GetCacheResultType(method));
            sb.Append('>');
            sb.Append(' ');
            sb.Append(fieldName);
            sb.AppendLine(" = new();");
            sb.AppendLine();

            var parameters = GetParameterList(method);
            var invalidateParameters = GetParameterList(includedParameters);
            var cacheKeyExpression = GetCacheKeyExpression(method, includedParameters);
            var invocationArguments = GetInvocationArguments(method, includedParameters);
            var useStaticFactory = includedParameters.Count == method.Parameters.Length;
            var methodAccessibility = GetAccessibility(method.DeclaredAccessibility);
            var methodTypeParameters = GetMethodTypeParameters(method);
            var methodConstraints = GetMethodConstraints(method);
            var invocationTypeArguments = GetMethodTypeArguments(method);

            sb.Append(innerIndent);
            sb.Append(methodAccessibility);
            sb.Append(' ');
            sb.Append(method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            sb.Append(' ');
            sb.Append(method.Name);
            sb.Append(methodTypeParameters);
            sb.Append('(');
            sb.Append(parameters);
            sb.AppendLine(")");
            foreach (var constraint in methodConstraints)
            {
                sb.Append(innerIndent);
                sb.Append("    ");
                sb.AppendLine(constraint);
            }
            sb.Append(innerIndent);
            sb.AppendLine("{");
            sb.Append(innerIndent);
            sb.Append("    return ");
            if (method.IsGenericMethod)
            {
                sb.Append('(');
                sb.Append(method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                sb.Append(')');
            }
            sb.Append(fieldName);
            sb.Append(".GetOrAdd(implementation, ");
            sb.Append(cacheKeyExpression);
            sb.Append(", ");
            if (useStaticFactory)
                sb.Append("static ");
            sb.Append("(s, p) => ");
            if (method.IsGenericMethod)
                sb.Append("(object)");
            sb.Append("s.");
            sb.Append(method.Name);
            sb.Append(invocationTypeArguments);
            sb.Append('(');
            sb.Append(invocationArguments);
            sb.Append("), ");
            sb.Append(observerFieldName);
            sb.AppendLine(");");
            sb.Append(innerIndent);
            sb.AppendLine("}");
            sb.AppendLine();

            sb.Append(innerIndent);
            sb.Append(methodAccessibility);
            sb.Append(" void Invalidate");
            sb.Append(method.Name);
            sb.Append(methodTypeParameters);
            sb.Append('(');
            sb.Append(invalidateParameters);
            sb.AppendLine(")");
            foreach (var constraint in methodConstraints)
            {
                sb.Append(innerIndent);
                sb.Append("    ");
                sb.AppendLine(constraint);
            }
            sb.Append(innerIndent);
            sb.AppendLine("{");
            sb.Append(innerIndent);
            sb.Append("    ");
            sb.Append(fieldName);
            sb.Append(".Invalidate(");
            sb.Append(cacheKeyExpression);
            sb.AppendLine(");");
            sb.Append(innerIndent);
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.Append(innerIndent);
        sb.AppendLine("public void InvalidateAll()");
        sb.Append(innerIndent);
        sb.AppendLine("{");
        foreach (var fieldName in cacheFields)
        {
            sb.Append(innerIndent);
            sb.Append("    ");
            sb.Append(fieldName);
            sb.AppendLine(".InvalidateAll();");
        }
        sb.Append(innerIndent);
        sb.AppendLine("}");
        sb.AppendLine();

        sb.Append(indent);
        sb.AppendLine("}");

        for (var i = 0; i < GetContainingTypeCount(typeSymbol); i++)
        {
            indent = indent.Length >= 4 ? indent.Substring(0, indent.Length - 4) : string.Empty;
            sb.Append(indent);
            sb.AppendLine("}");
        }

        if (!typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string GetInvocationArguments(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters)
    {
        if (method.Parameters.Length == 0)
            return string.Empty;

        var includedParameterNames = new HashSet<string>(includedParameters.Select(parameter => parameter.Name), StringComparer.Ordinal);
        var usesTuple = GetCacheKeyElementCount(method, includedParameters) > 1;
        return string.Join(", ", method.Parameters.Select(parameter => GetInvocationArgument(method, parameter, usesTuple, includedParameterNames.Contains(parameter.Name))));
    }

    private static string GetParameterTupleExpression(IReadOnlyList<string> elements)
    {
        if (elements.Count == 0)
            return "default";

        return $"({string.Join(", ", elements)})";
    }

    private static string GetParameterTupleType(IReadOnlyList<string> elements)
    {
        if (elements.Count == 0)
            return "global::System.ValueTuple";

        return $"({string.Join(", ", elements)})";
    }

    private static string GetCacheKeyExpression(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters)
    {
        var elements = GetCacheKeyExpressionElements(method, includedParameters).ToList();
        if (elements.Count == 0)
            return "default";
        if (elements.Count == 1)
            return elements[0];

        return GetParameterTupleExpression(elements);
    }

    private static string GetCacheKeyType(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters)
    {
        var elementTypes = GetCacheKeyTypeElements(method, includedParameters, includeNames: false).ToList();
        if (elementTypes.Count == 0)
            return "global::System.ValueTuple";
        if (elementTypes.Count == 1)
            return elementTypes[0];

        var namedElements = GetCacheKeyTypeElements(method, includedParameters, includeNames: true).ToList();
        return GetParameterTupleType(namedElements);
    }

    private static IEnumerable<string> GetCacheKeyExpressionElements(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters)
    {
        foreach (var typeSymbol in GetGenericMethodTypeSymbols(method))
            yield return $"typeof({typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";

        foreach (var parameter in includedParameters)
        {
            if (ContainsTypeParameter(parameter.Type))
                yield return $"((object){EscapeIdentifier(parameter.Name)})!";
            else
                yield return EscapeIdentifier(parameter.Name);
        }

        foreach (var cacheContextElement in GetCacheContextElements(method))
            yield return cacheContextElement.Expression;
    }

    private static IEnumerable<string> GetCacheKeyTypeElements(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters, bool includeNames)
    {
        foreach (var _ in GetGenericMethodTypeSymbols(method))
            yield return "global::System.Type";

        foreach (var parameter in includedParameters)
        {
            var typeName = ContainsTypeParameter(parameter.Type)
                ? "global::System.Object"
                : parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (includeNames)
                yield return $"{typeName} {EscapeIdentifier(parameter.Name)}";
            else
                yield return typeName;
        }

        foreach (var cacheContextElement in GetCacheContextElements(method))
        {
            if (includeNames)
                yield return $"{cacheContextElement.TypeName} {cacheContextElement.Name}";
            else
                yield return cacheContextElement.TypeName;
        }
    }

    private static int GetCacheKeyElementCount(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters)
        => method.TypeParameters.Length + includedParameters.Count + GetCacheContextElements(method).Count;

    private static string GetInvocationArgument(IMethodSymbol method, IParameterSymbol parameter, bool usesTuple, bool isIncludedInCacheKey)
    {
        if (!isIncludedInCacheKey)
            return EscapeIdentifier(parameter.Name);

        var expression = usesTuple ? $"p.{EscapeIdentifier(parameter.Name)}" : "p";
        if (method.IsGenericMethod || ContainsTypeParameter(parameter.Type))
        {
            var parameterType = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({parameterType}){expression}!";
        }

        return expression;
    }

    private static bool ContainsMethodTypeParameter(ITypeSymbol typeSymbol, IMethodSymbol method)
    {
        if (typeSymbol is ITypeParameterSymbol typeParameter)
            return SymbolEqualityComparer.Default.Equals(typeParameter.DeclaringMethod, method);

        return typeSymbol switch
        {
            IArrayTypeSymbol arrayType => ContainsMethodTypeParameter(arrayType.ElementType, method),
            INamedTypeSymbol namedType => namedType.TypeArguments.Any(arg => ContainsMethodTypeParameter(arg, method)),
            _ => false
        };
    }

    private static IEnumerable<ITypeSymbol> GetGenericMethodTypeSymbols(IMethodSymbol method)
        => method.TypeParameters.Length > 0 ? method.TypeParameters : method.TypeArguments;

    private static string GetCacheResultType(IMethodSymbol method)
        => method.IsGenericMethod ? "global::System.Object" : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static bool ContainsTypeParameter(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is ITypeParameterSymbol)
            return true;

        return typeSymbol switch
        {
            IArrayTypeSymbol arrayType => ContainsTypeParameter(arrayType.ElementType),
            INamedTypeSymbol namedType => namedType.TypeArguments.Any(ContainsTypeParameter),
            _ => false
        };
    }

    private static IReadOnlyList<CacheContextElement> GetCacheContextElements(IMethodSymbol method)
    {
        var elements = new List<CacheContextElement>();
        var index = 0;

        foreach (var member in method.ContainingType.GetMembers())
        {
            if (member is IFieldSymbol fieldSymbol
                && !fieldSymbol.IsStatic
                && !fieldSymbol.IsImplicitlyDeclared
                && TryGetCacheContextType(fieldSymbol.Type, out var fieldContextType))
            {
                elements.Add(new CacheContextElement(
                    $"cacheContext{index++}",
                    $"implementation.{EscapeIdentifier(fieldSymbol.Name)}.GetCacheContext()",
                    fieldContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                continue;
            }

            if (member is IPropertySymbol propertySymbol
                && !propertySymbol.IsStatic
                && propertySymbol.GetMethod is not null
                && TryGetCacheContextType(propertySymbol.Type, out var propertyContextType))
            {
                elements.Add(new CacheContextElement(
                    $"cacheContext{index++}",
                    $"implementation.{EscapeIdentifier(propertySymbol.Name)}.GetCacheContext()",
                    propertyContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
        }

        return elements;
    }

    private static bool TryGetCacheContextType(ITypeSymbol typeSymbol, out ITypeSymbol cacheContextType)
    {
        if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
        {
            if (IsCacheContextProviderInterface(namedTypeSymbol))
            {
                cacheContextType = namedTypeSymbol.TypeArguments[0];
                return true;
            }

            foreach (var interfaceSymbol in namedTypeSymbol.AllInterfaces)
            {
                if (IsCacheContextProviderInterface(interfaceSymbol))
                {
                    cacheContextType = interfaceSymbol.TypeArguments[0];
                    return true;
                }
            }
        }

        cacheContextType = null!;
        return false;
    }

    private static bool IsCacheContextProviderInterface(INamedTypeSymbol typeSymbol)
        => typeSymbol is { Name: CacheContextProviderInterfaceName, Arity: 1 }
            && typeSymbol.ContainingNamespace.ToDisplayString() == CacheContextProviderInterfaceNamespace;

    private sealed record CacheContextElement(string Name, string Expression, string TypeName);

    private static IReadOnlyList<IParameterSymbol> GetIncludedParameters(IMethodSymbol method)
    {
        var ignoredParameterNames = GetIgnoredParameterNames(method);
        if (ignoredParameterNames.Count == 0)
            return method.Parameters;

        return method.Parameters
            .Where(parameter => !ignoredParameterNames.Contains(parameter.Name))
            .ToList();
    }

    private static HashSet<string> GetIgnoredParameterNames(IMethodSymbol method)
    {
        var ignoredParameterNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in method.Parameters)
        {
            if (IsIgnoredParameterType(parameter.Type))
                ignoredParameterNames.Add(parameter.Name);

            foreach (var attribute in parameter.GetAttributes())
                ApplyIgnoreAttribute(attribute, method, ignoredParameterNames, parameter);
        }

        foreach (var interfaceMethod in method.ExplicitInterfaceImplementations)
            ApplyInterfaceParameterIgnores(method, interfaceMethod, ignoredParameterNames);

        var containingType = method.ContainingType;
        foreach (var interfaceSymbol in containingType.AllInterfaces)
        {
            foreach (var interfaceMember in interfaceSymbol.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (!SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(interfaceMember), method))
                    continue;

                ApplyInterfaceParameterIgnores(method, interfaceMember, ignoredParameterNames);
            }
        }

        foreach (var attribute in CollectIgnoreAttributes(method))
            ApplyIgnoreAttribute(attribute, method, ignoredParameterNames, parameter: null);

        return ignoredParameterNames;
    }

    private static void ApplyInterfaceParameterIgnores(IMethodSymbol method, IMethodSymbol interfaceMethod, HashSet<string> ignoredParameterNames)
    {
        var parameterCount = Math.Min(method.Parameters.Length, interfaceMethod.Parameters.Length);
        for (var index = 0; index < parameterCount; index++)
        {
            var methodParameter = method.Parameters[index];
            var interfaceParameter = interfaceMethod.Parameters[index];
            foreach (var attribute in interfaceParameter.GetAttributes())
                ApplyIgnoreAttribute(attribute, method, ignoredParameterNames, methodParameter);
        }
    }

    private static IEnumerable<AttributeData> CollectIgnoreAttributes(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
            yield return attribute;

        foreach (var attribute in method.OriginalDefinition.GetAttributes())
            yield return attribute;

        foreach (var interfaceMethod in method.ExplicitInterfaceImplementations)
        {
            foreach (var attribute in interfaceMethod.GetAttributes())
                yield return attribute;
        }

        var containingType = method.ContainingType;
        foreach (var interfaceSymbol in containingType.AllInterfaces)
        {
            foreach (var interfaceAttribute in interfaceSymbol.GetAttributes())
                yield return interfaceAttribute;

            foreach (var interfaceMember in interfaceSymbol.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (!SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(interfaceMember), method))
                    continue;

                foreach (var attribute in interfaceMember.GetAttributes())
                    yield return attribute;
            }
        }

        foreach (var typeAttribute in containingType.GetAttributes())
            yield return typeAttribute;

        foreach (var assemblyAttribute in method.ContainingAssembly.GetAttributes())
            yield return assemblyAttribute;
    }

    private static void ApplyIgnoreAttribute(AttributeData attribute, IMethodSymbol method, HashSet<string> ignoredParameterNames, IParameterSymbol? parameter)
    {
        if (IsIgnoreAttribute(attribute))
        {
            if (parameter is not null)
                ignoredParameterNames.Add(parameter.Name);

            return;
        }

        if (!IsIgnoreParameterAttribute(attribute))
            return;

        var rule = GetIgnoreRule(attribute);
        if (!rule.HasFilter && parameter is not null)
        {
            ignoredParameterNames.Add(parameter.Name);
            return;
        }

        foreach (var methodParameter in method.Parameters)
        {
            if (MatchesIgnoreRule(methodParameter, rule))
                ignoredParameterNames.Add(methodParameter.Name);
        }
    }

    private static IgnoreRule GetIgnoreRule(AttributeData attribute)
    {
        ITypeSymbol? typeFilter = null;
        string? nameFilter = null;

        foreach (var constructorArgument in attribute.ConstructorArguments)
        {
            if (constructorArgument.Kind == TypedConstantKind.Type && constructorArgument.Value is ITypeSymbol typeSymbol)
                typeFilter = typeSymbol;
            else if (constructorArgument.Kind == TypedConstantKind.Primitive && constructorArgument.Value is string parameterName)
                nameFilter = parameterName;
        }

        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument is { Key: "Type", Value.Kind: TypedConstantKind.Type } && namedArgument.Value.Value is ITypeSymbol typeSymbol)
                typeFilter = typeSymbol;
            else if (namedArgument is { Key: "ParameterName", Value.Kind: TypedConstantKind.Primitive } && namedArgument.Value.Value is string parameterName)
                nameFilter = parameterName;
        }

        return new IgnoreRule(typeFilter, nameFilter, typeFilter is not null || !string.IsNullOrEmpty(nameFilter));
    }

    private static bool MatchesIgnoreRule(IParameterSymbol parameter, IgnoreRule rule)
    {
        if (!rule.HasFilter)
            return true;

        if (rule.NameFilter is not null && !string.Equals(parameter.Name, rule.NameFilter, StringComparison.Ordinal))
            return false;

        if (rule.TypeFilter is null)
            return true;

        return SymbolEqualityComparer.Default.Equals(parameter.Type, rule.TypeFilter)
               || parameter.Type is INamedTypeSymbol parameterNamedType
                   && rule.TypeFilter is INamedTypeSymbol ruleNamedType
                   && SymbolEqualityComparer.Default.Equals(parameterNamedType.OriginalDefinition, ruleNamedType.OriginalDefinition);
    }

    private static bool IsIgnoreAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == IgnoreAttributeMetadataName;

    private static bool IsIgnoreParameterAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == IgnoreParameterAttributeMetadataName;

    private static bool IsIgnoredParameterType(ITypeSymbol typeSymbol)
    {
        var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);
        return _ignoredParameterTypes.Any(ignoredType => string.Equals(ignoredType.FullName, typeName, StringComparison.Ordinal));
    }

    private sealed record IgnoreRule(ITypeSymbol? TypeFilter, string? NameFilter, bool HasFilter);

    private static IReadOnlyList<AttributeData> GetCacheEntryLifetimeObserverAttributes(IMethodSymbol method)
    {
        var attributes = new List<AttributeData>();

        attributes.AddRange(method.GetAttributes().Where(IsCacheEntryLifetimeObserverAttribute));

        foreach (var interfaceMethod in method.ExplicitInterfaceImplementations)
            attributes.AddRange(interfaceMethod.GetAttributes().Where(IsCacheEntryLifetimeObserverAttribute));

        var containingType = method.ContainingType;
        foreach (var interfaceSymbol in containingType.AllInterfaces)
        {
            foreach (var interfaceMember in interfaceSymbol.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (!SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(interfaceMember), method))
                    continue;

                attributes.AddRange(interfaceMember.GetAttributes().Where(IsCacheEntryLifetimeObserverAttribute));
            }
        }

        return attributes;
    }

    private static bool IsCacheEntryLifetimeObserverAttribute(AttributeData attributeData)
        => InheritsFrom(attributeData.AttributeClass, CacheEntryLifetimeObserverAttributeMetadataName);

    private static bool InheritsFrom(INamedTypeSymbol? typeSymbol, string metadataName)
    {
        while (typeSymbol is not null)
        {
            if (typeSymbol.ToDisplayString() == metadataName)
                return true;
            typeSymbol = typeSymbol.BaseType;
        }

        return false;
    }

    private static string? CreateAttributeInstantiationExpression(AttributeData attributeData)
    {
        if (attributeData.AttributeClass is null)
            return null;

        var constructorArguments = new List<string>(attributeData.ConstructorArguments.Length);
        foreach (var constructorArgument in attributeData.ConstructorArguments)
        {
            var expression = GetTypedConstantExpression(constructorArgument);
            if (expression is null)
                return null;
            constructorArguments.Add(expression);
        }

        var expressionBuilder = new StringBuilder();
        expressionBuilder.Append("new ");
        expressionBuilder.Append(attributeData.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        expressionBuilder.Append('(');
        expressionBuilder.Append(string.Join(", ", constructorArguments));
        expressionBuilder.Append(')');

        if (attributeData.NamedArguments.Length > 0)
        {
            var namedArguments = new List<string>(attributeData.NamedArguments.Length);
            foreach (var namedArgument in attributeData.NamedArguments)
            {
                var expression = GetTypedConstantExpression(namedArgument.Value);
                if (expression is null)
                    return null;
                namedArguments.Add($"{namedArgument.Key} = {expression}");
            }

            expressionBuilder.Append(" { ");
            expressionBuilder.Append(string.Join(", ", namedArguments));
            expressionBuilder.Append(" }");
        }

        return expressionBuilder.ToString();
    }

    private static string? GetTypedConstantExpression(TypedConstant typedConstant)
    {
        if (typedConstant.IsNull)
            return "null";

        return typedConstant.Kind switch
        {
            TypedConstantKind.Primitive => SymbolDisplay.FormatPrimitive(typedConstant.Value, quoteStrings: true, useHexadecimalNumbers: false),
            TypedConstantKind.Type => $"typeof({((ITypeSymbol)typedConstant.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})",
            TypedConstantKind.Enum => GetEnumValueExpression(typedConstant),
            TypedConstantKind.Array => GetArrayExpression(typedConstant),
            _ => null
        };
    }

    private static string? GetEnumValueExpression(TypedConstant typedConstant)
    {
        if (typedConstant.Type is not INamedTypeSymbol enumType)
            return null;

        var enumValue = typedConstant.Value;
        var enumMember = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(member => member.HasConstantValue && Equals(member.ConstantValue, enumValue));

        if (enumMember is not null)
            return $"{enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{enumMember.Name}";

        return $"({enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){SymbolDisplay.FormatPrimitive(enumValue, quoteStrings: true, useHexadecimalNumbers: false)}";
    }

    private static string? GetArrayExpression(TypedConstant typedConstant)
    {
        if (typedConstant.IsNull)
            return "null";

        if (typedConstant.Type is not IArrayTypeSymbol arrayType)
            return null;

        var values = new List<string>(typedConstant.Values.Length);
        foreach (var value in typedConstant.Values)
        {
            var valueExpression = GetTypedConstantExpression(value);
            if (valueExpression is null)
                return null;
            values.Add(valueExpression);
        }

        return $"new {arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}[] {{ {string.Join(", ", values)} }}";
    }

    private static string GetParameterList(IMethodSymbol method)
    {
        return GetParameterList(method.Parameters);
    }

    private static string GetParameterList(IReadOnlyList<IParameterSymbol> parameters)
    {
        if (parameters.Count == 0)
            return string.Empty;

        return string.Join(", ", parameters.Select(parameter =>
        {
            var modifier = parameter.IsParams ? "params " : string.Empty;
            var typeName = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var defaultValue = parameter.HasExplicitDefaultValue ? " = " + GetDefaultValueExpression(parameter) : string.Empty;
            return $"{modifier}{typeName} {EscapeIdentifier(parameter.Name)}{defaultValue}";
        }));
    }

    private static string GetMethodTypeParameters(IMethodSymbol method)
    {
        if (method.TypeParameters.Length == 0)
            return string.Empty;

        return $"<{string.Join(", ", method.TypeParameters.Select(parameter => parameter.Name))}>";
    }

    private static string GetMethodTypeArguments(IMethodSymbol method)
    {
        if (method.TypeParameters.Length == 0)
            return string.Empty;

        return $"<{string.Join(", ", method.TypeParameters.Select(parameter => parameter.Name))}>";
    }

    private static IReadOnlyList<string> GetMethodConstraints(IMethodSymbol method)
    {
        if (method.TypeParameters.Length == 0)
            return Array.Empty<string>();

        var constraints = new List<string>();
        foreach (var typeParameter in method.TypeParameters)
        {
            var parts = new List<string>();
            if (typeParameter.HasNotNullConstraint)
                parts.Add("notnull");
            if (typeParameter.HasReferenceTypeConstraint)
                parts.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            if (typeParameter.HasUnmanagedTypeConstraint)
                parts.Add("unmanaged");
            if (typeParameter.HasValueTypeConstraint)
                parts.Add("struct");

            parts.AddRange(typeParameter.ConstraintTypes.Select(constraintType => constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

            if (typeParameter.HasConstructorConstraint)
                parts.Add("new()");

            if (parts.Count == 0)
                continue;

            constraints.Add($"where {typeParameter.Name} : {string.Join(", ", parts)}");
        }

        return constraints;
    }

    private static string GetCacheFieldName(IMethodSymbol method)
    {
        var typeSuffix = method.Parameters.Length == 0
            ? "NoArgs"
            : string.Concat(method.Parameters.Select(parameter => GetTypeNameForIdentifier(parameter.Type)));
        if (method.IsGenericMethod)
            typeSuffix += "Of" + string.Concat(method.TypeParameters.Select(parameter => parameter.Name));
        var methodName = method.Name.Length > 1
            ? method.Name.Substring(0, 1).ToLowerInvariant() + method.Name.Substring(1)
            : method.Name.ToLowerInvariant();
        return $"_{methodName}{typeSuffix}Cache";
    }

    private static string GetTypeNameForIdentifier(ITypeSymbol typeSymbol)
    {
        return typeSymbol switch
        {
            IArrayTypeSymbol arrayType => GetTypeNameForIdentifier(arrayType.ElementType) + "Array",
            INamedTypeSymbol namedType when namedType.TypeArguments.Length > 0 => namedType.Name + string.Concat(namedType.TypeArguments.Select(GetTypeNameForIdentifier)),
            INamedTypeSymbol namedType => namedType.Name,
            _ => typeSymbol.Name
        };
    }

    private static string GetAccessibility(Accessibility accessibility)
        => accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal"
        };

    private static string GetTypeKeyword(INamedTypeSymbol typeSymbol)
        => typeSymbol.TypeKind == TypeKind.Interface ? "interface" : typeSymbol.IsRecord ? "record" : "class";

    private static string GetTypeParameters(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeParameters.Length == 0)
            return string.Empty;

        return $"<{string.Join(", ", typeSymbol.TypeParameters.Select(parameter => parameter.Name))}>";
    }

    private static IReadOnlyList<string> GetTypeConstraints(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeParameters.Length == 0)
            return Array.Empty<string>();

        var constraints = new List<string>();
        foreach (var typeParameter in typeSymbol.TypeParameters)
        {
            var parts = new List<string>();
            if (typeParameter.HasNotNullConstraint)
                parts.Add("notnull");
            if (typeParameter.HasReferenceTypeConstraint)
                parts.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            if (typeParameter.HasUnmanagedTypeConstraint)
                parts.Add("unmanaged");
            if (typeParameter.HasValueTypeConstraint)
                parts.Add("struct");

            parts.AddRange(typeParameter.ConstraintTypes.Select(constraintType => constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

            if (typeParameter.HasConstructorConstraint)
                parts.Add("new()");

            if (parts.Count == 0)
                continue;

            constraints.Add($"where {typeParameter.Name} : {string.Join(", ", parts)}");
        }

        return constraints;
    }

    private static string GetContainingTypeName(INamedTypeSymbol typeSymbol)
    {
        var name = typeSymbol.Name;
        var typeParameters = GetTypeParameters(typeSymbol);
        return name + typeParameters;
    }

    private static string GetHintName(INamedTypeSymbol typeSymbol)
    {
        var name = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace('.', '_');
        return $"{name}.CascadingComputeWrapper.g.cs";
    }

    private static string EscapeIdentifier(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
    }

    private static string GetDefaultValueExpression(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
            return string.Empty;

        if (parameter.ExplicitDefaultValue is null)
            return "null";

        if (parameter.Type.TypeKind == TypeKind.Enum && parameter.Type is INamedTypeSymbol enumType)
        {
            var enumMember = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(member => member.HasConstantValue && Equals(member.ConstantValue, parameter.ExplicitDefaultValue));

            if (enumMember is not null)
                return $"{enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{enumMember.Name}";

            return $"({enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false)}";
        }

        return SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false)
            ?? throw new InvalidOperationException($"Unable to format default value of parameter {parameter}");
    }

    private static IEnumerable<INamedTypeSymbol> GetContainingTypes(INamedTypeSymbol typeSymbol)
    {
        var stack = new Stack<INamedTypeSymbol>();
        var current = typeSymbol;
        while (current is not null)
        {
            stack.Push(current);
            current = current.ContainingType;
        }

        return stack;
    }

    private static int GetContainingTypeCount(INamedTypeSymbol typeSymbol)
    {
        var count = 0;
        var current = typeSymbol;
        while (current is not null)
        {
            count++;
            current = current.ContainingType;
        }

        return count;
    }

    private static string GenerateInterfaceSource(INamedTypeSymbol interfaceSymbol, IReadOnlyList<IMethodSymbol> interfaceMethods)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");

        if (!interfaceSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            sb.Append("namespace ");
            sb.AppendLine(interfaceSymbol.ContainingNamespace.ToDisplayString());
            sb.AppendLine("{");
        }

        var indent = string.Empty;
        var containingTypes = GetContainingTypes(interfaceSymbol).ToList();
        for (var index = 0; index < containingTypes.Count - 1; index++)
        {
            var containingType = containingTypes[index];
            sb.Append(indent);
            sb.Append(GetAccessibility(containingType.DeclaredAccessibility));
            sb.Append(" partial ");
            sb.Append(GetTypeKeyword(containingType));
            sb.Append(' ');
            sb.Append(containingType.Name);
            sb.Append(GetTypeParameters(containingType));
            sb.AppendLine();
            var constraints = GetTypeConstraints(containingType);
            if (constraints.Count > 0)
            {
                foreach (var constraint in constraints)
                {
                    sb.Append(indent);
                    sb.Append("    ");
                    sb.AppendLine(constraint);
                }
            }
            sb.Append(indent);
            sb.AppendLine("{");
            indent += "    ";
        }

        sb.Append(indent);
        sb.Append(GetAccessibility(interfaceSymbol.DeclaredAccessibility));
        sb.Append(" partial interface ");
        sb.Append(interfaceSymbol.Name);
        sb.Append(GetTypeParameters(interfaceSymbol));
        sb.AppendLine();
        var interfaceConstraints = GetTypeConstraints(interfaceSymbol);
        if (interfaceConstraints.Count > 0)
        {
            foreach (var constraint in interfaceConstraints)
            {
                sb.Append(indent);
                sb.Append("    ");
                sb.AppendLine(constraint);
            }
        }
        sb.Append(indent);
        sb.AppendLine("{");

        var interfaceIndent = indent + "    ";
        var wrapperInterfaceName = GetWrapperInterfaceName(interfaceSymbol);
        sb.Append(interfaceIndent);
        sb.Append(wrapperInterfaceName);
        sb.AppendLine(" CascadingCompute { get; }");
        sb.AppendLine();
        sb.Append(interfaceIndent);
        sb.Append("interface ");
        sb.Append(wrapperInterfaceName);
        sb.AppendLine();
        sb.Append(interfaceIndent);
        sb.AppendLine("{");

        var memberIndent = interfaceIndent + "    ";
        foreach (var method in interfaceMethods)
        {
            var parameters = GetParameterList(method);
            var includedParameters = GetIncludedParameters(method);
            var invalidateParameters = GetParameterList(includedParameters);
            var methodTypeParameters = GetMethodTypeParameters(method);
            var methodConstraints = GetMethodConstraints(method);
            sb.Append(memberIndent);
            sb.Append(method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            sb.Append(' ');
            sb.Append(method.Name);
            sb.Append(methodTypeParameters);
            sb.Append('(');
            sb.Append(parameters);
            sb.AppendLine(")");
            foreach (var constraint in methodConstraints)
            {
                sb.Append(memberIndent);
                sb.Append("    ");
                sb.AppendLine(constraint);
            }
            sb.Append(memberIndent);
            sb.AppendLine(";");

            sb.Append(memberIndent);
            sb.Append("void Invalidate");
            sb.Append(method.Name);
            sb.Append(methodTypeParameters);
            sb.Append('(');
            sb.Append(invalidateParameters);
            sb.AppendLine(")");
            foreach (var constraint in methodConstraints)
            {
                sb.Append(memberIndent);
                sb.Append("    ");
                sb.AppendLine(constraint);
            }
            sb.Append(memberIndent);
            sb.AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(memberIndent);
        sb.AppendLine("void InvalidateAll();");
        sb.Append(interfaceIndent);
        sb.AppendLine("}");
        sb.Append(indent);
        sb.AppendLine("}");

        for (var i = 0; i < containingTypes.Count - 1; i++)
        {
            indent = indent.Length >= 4 ? indent.Substring(0, indent.Length - 4) : string.Empty;
            sb.Append(indent);
            sb.AppendLine("}");
        }

        if (!interfaceSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string GetInterfaceHintName(INamedTypeSymbol interfaceSymbol)
    {
        var name = interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace('.', '_');
        return $"{name}.CascadingComputeInterface.g.cs";
    }

    private static string GetWrapperInterfaceName(INamedTypeSymbol interfaceSymbol)
        => "CascadingComputeWrapper";

    private static string GetWrapperInterfaceTypeName(INamedTypeSymbol interfaceSymbol)
    {
        var typeName = interfaceSymbol.Name + GetTypeArgumentsForReference(interfaceSymbol) + "." + GetWrapperInterfaceName(interfaceSymbol);
        var builder = new StringBuilder("global::");

        if (!interfaceSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            builder.Append(interfaceSymbol.ContainingNamespace.ToDisplayString());
            builder.Append('.');
        }

        var containingTypes = new Stack<INamedTypeSymbol>();
        var current = interfaceSymbol.ContainingType;
        while (current is not null)
        {
            containingTypes.Push(current);
            current = current.ContainingType;
        }

        foreach (var containingType in containingTypes)
        {
            builder.Append(containingType.Name);
            builder.Append(GetTypeArgumentsForReference(containingType));
            builder.Append('.');
        }

        builder.Append(typeName);
        return builder.ToString();
    }

    private static IMethodSymbol? GetInterfaceMethod(INamedTypeSymbol interfaceSymbol, IMethodSymbol interfaceMethodDefinition)
    {
        return interfaceSymbol
            .GetMembers(interfaceMethodDefinition.Name)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(member => SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, interfaceMethodDefinition.OriginalDefinition));
    }

    private static string GetTypeArgumentsForReference(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeArguments.Length == 0)
            return string.Empty;

        return $"<{string.Join(", ", typeSymbol.TypeArguments.Select(argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))}>";
    }

    private sealed class NamedTypeSymbolEqualityComparer : IEqualityComparer<INamedTypeSymbol>
    {
        public bool Equals(INamedTypeSymbol? x, INamedTypeSymbol? y)
            => SymbolEqualityComparer.Default.Equals(x, y);

        public int GetHashCode(INamedTypeSymbol obj)
            => SymbolEqualityComparer.Default.GetHashCode(obj);
    }
}

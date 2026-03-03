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
    private const string EnabledAttributeMetadataName = "hhnl.CascadingCompute.Attributes.CascadingComputeEnabledAttribute";
    private static readonly SymbolDisplayFormat NullableFullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

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
        var interfaceMethodsByType = methods.IsDefaultOrEmpty
            ? new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>(NamedTypeSymbolComparer)
            : methods
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

        var classMethodsByType = methods.IsDefaultOrEmpty
            ? new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>(NamedTypeSymbolComparer)
            : methods
                .Where(method => method.ContainingType is { TypeKind: TypeKind.Class })
                .GroupBy<IMethodSymbol, INamedTypeSymbol>(method => method.ContainingType, NamedTypeSymbolComparer)
                .ToDictionary(group => group.Key, group => group.ToList(), NamedTypeSymbolComparer);

        var distinctClasses = classes.Distinct(NamedTypeSymbolComparer).ToArray();
        foreach (var classSymbol in distinctClasses)
        {
            classMethodsByType.TryGetValue(classSymbol, out var classMethods);
            classMethods ??= [];

            var interfaceMethods = new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>(NamedTypeSymbolComparer);
            foreach (var interfaceSymbol in classSymbol.AllInterfaces)
            {
                var interfaceDefinition = (INamedTypeSymbol)interfaceSymbol.OriginalDefinition;
                if (interfaceMethods.ContainsKey(interfaceDefinition))
                    continue;

                if (validInterfaces.TryGetValue(interfaceDefinition, out var knownMethods))
                {
                    interfaceMethods[interfaceDefinition] = knownMethods;
                    continue;
                }

                var discoveredMethods = GetSupportedCascadingInterfaceMethods(interfaceDefinition);
                if (discoveredMethods.Count > 0)
                    interfaceMethods[interfaceDefinition] = discoveredMethods;
            }

            var interfaceTypes = classSymbol.AllInterfaces
                .Where(interfaceSymbol => interfaceMethods.ContainsKey((INamedTypeSymbol)interfaceSymbol.OriginalDefinition))
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
                foreach (var interfaceMethod in interfaceMethods[interfaceDefinition])
                {
                    var interfaceMember = GetInterfaceMethod(interfaceSymbol, interfaceMethod);
                    if (interfaceMember is not null
                        && classSymbol.FindImplementationForInterfaceMember(interfaceMember) is IMethodSymbol implementation)
                        methodSet.Add(implementation);
                }
            }

            if (methodSet.Count == 0)
                continue;

            INamedTypeSymbol? wrapperInterface = interfaceTypes.Length > 0 ? interfaceTypes[0] : null;
            var passthroughInterfaceMethods = GetPassthroughInterfaceMethods(classSymbol, methodSet);
            var implementedInterfaces = classSymbol.AllInterfaces
                .Distinct(NamedTypeSymbolComparer)
                .ToArray();

            var source = GenerateWrapperSource(
                classSymbol,
                methodSet,
                HasCascadingComputeProperty(classSymbol),
                HasInvalidationProperty(classSymbol),
                wrapperInterface,
                implementedInterfaces,
                interfaceTypes,
                passthroughInterfaceMethods);
            context.AddSource(GetHintName(classSymbol), SourceText.From(source, Encoding.UTF8));
        }
    }

    private static IReadOnlyList<InterfacePassthroughMethod> GetPassthroughInterfaceMethods(INamedTypeSymbol classSymbol, HashSet<IMethodSymbol> cachedMethods)
    {
        var methods = new List<InterfacePassthroughMethod>();
        var seenSignatures = new HashSet<string>(StringComparer.Ordinal);
        var cascadingClassMethodSignatures = new HashSet<string>(
            classSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(HasCascadingComputeAttribute)
                .Select(GetMethodSignatureKey),
            StringComparer.Ordinal);

        foreach (var interfaceSymbol in classSymbol.AllInterfaces)
        {
            foreach (var interfaceMethod in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (interfaceMethod.MethodKind != MethodKind.Ordinary || interfaceMethod.IsStatic)
                    continue;

                if (classSymbol.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementationMethod)
                    continue;

                if (cachedMethods.Contains(implementationMethod)
                    || HasCascadingComputeAttribute(interfaceMethod)
                    || HasCascadingComputeAttribute(implementationMethod))
                    continue;

                var signature = GetMethodSignatureKey(interfaceMethod);
                if (cascadingClassMethodSignatures.Contains(signature))
                    continue;

                if (!seenSignatures.Add(signature))
                    continue;

                methods.Add(new InterfacePassthroughMethod((INamedTypeSymbol)interfaceMethod.ContainingType, interfaceMethod));
            }
        }

        return methods;
    }

    private static string GetMethodSignatureKey(IMethodSymbol method)
        => $"{method.Name}|{method.Arity}|{method.ReturnType.ToDisplayString(NullableFullyQualifiedFormat)}|{string.Join(",", method.Parameters.Select(parameter => $"{parameter.RefKind}:{parameter.Type.ToDisplayString(NullableFullyQualifiedFormat)}"))}";

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

    private static List<IMethodSymbol> GetSupportedCascadingInterfaceMethods(INamedTypeSymbol interfaceSymbol)
    {
        var methods = new List<IMethodSymbol>();
        foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (!HasCascadingComputeAttribute(method))
                continue;

            if (IsSupportedMethod(method))
                methods.Add(method);
        }

        return methods;
    }

    private static bool HasCascadingComputeAttribute(IMethodSymbol method)
        => method.GetAttributes().Any(IsCascadingComputeAttribute);

    private static bool IsCascadingComputeAttribute(AttributeData attributeData)
        => InheritsFrom(attributeData.AttributeClass, AttributeMetadataName);

    private static bool HasWrapper(INamedTypeSymbol typeSymbol)
        => typeSymbol.GetMembers("CascadingComputeWrapper").OfType<INamedTypeSymbol>().Any();

    private static bool HasCascadingComputeEnabledAttribute(INamedTypeSymbol typeSymbol)
        => typeSymbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == EnabledAttributeMetadataName);

    private static bool HasCascadingComputeProperty(INamedTypeSymbol typeSymbol)
        => typeSymbol.GetMembers("CascadingCompute").OfType<IPropertySymbol>().Any();

    private static bool HasInvalidationProperty(INamedTypeSymbol typeSymbol)
        => typeSymbol.GetMembers("Invalidation").OfType<IPropertySymbol>().Any();

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

    private static string GenerateWrapperSource(
        INamedTypeSymbol typeSymbol,
        IEnumerable<IMethodSymbol> methods,
        bool hasProperty,
        bool hasInvalidationProperty,
        INamedTypeSymbol? interfaceSymbol,
        IReadOnlyList<INamedTypeSymbol> implementedInterfaces,
        IReadOnlyList<INamedTypeSymbol> cascadingInterfaces,
        IReadOnlyList<InterfacePassthroughMethod> passthroughInterfaceMethods)
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
        var hasEnabledAttribute = HasCascadingComputeEnabledAttribute(typeSymbol);
        foreach (var containingType in GetContainingTypes(typeSymbol))
        {
            if (!hasEnabledAttribute
                && SymbolEqualityComparer.Default.Equals(containingType, typeSymbol))
            {
                sb.Append(indent);
                sb.AppendLine("[global::hhnl.CascadingCompute.Attributes.CascadingComputeEnabledAttribute]");
            }

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
                sb.AppendLine("private CascadingComputeWrapper CascadingComputeInternal => field ??= new CascadingComputeWrapper(this);");
                sb.Append(indent);
                sb.Append("public ");
                sb.Append(GetWrapperInterfaceTypeName(interfaceSymbol));
                sb.AppendLine(" CascadingCompute => CascadingComputeInternal;");
            }
            sb.AppendLine();
        }

        if (!hasInvalidationProperty)
        {
            sb.Append(indent);
            sb.Append("private CascadingComputeWrapper.Invalidation Invalidation => field ??= new CascadingComputeWrapper.Invalidation(");
            if (!hasProperty && interfaceSymbol is not null)
                sb.Append("CascadingComputeInternal");
            else
                sb.Append("(CascadingComputeWrapper)CascadingCompute");
            sb.AppendLine(");");
            sb.AppendLine();
        }

        var primaryConstructorCacheContexts = GetPrimaryConstructorCacheContextParameters(typeSymbol);
        foreach (var primaryConstructorCacheContext in primaryConstructorCacheContexts)
        {
            sb.Append(indent);
            sb.Append("private ");
            sb.Append(primaryConstructorCacheContext.ContextTypeName);
            sb.Append(' ');
            sb.Append(primaryConstructorCacheContext.HelperMethodName);
            sb.Append("() => ");
            sb.Append(EscapeIdentifier(primaryConstructorCacheContext.ParameterName));
            sb.AppendLine(".GetCacheContext();");
            sb.AppendLine();
        }

        sb.Append(indent);
        sb.Append("public class CascadingComputeWrapper(");
        sb.Append(GetContainingTypeName(typeSymbol));
        sb.Append(" implementation)");
        var wrapperInterfaces = new List<string>();
        if (interfaceSymbol is not null)
            wrapperInterfaces.Add(GetWrapperInterfaceTypeName(interfaceSymbol));

        wrapperInterfaces.AddRange(
            cascadingInterfaces
                .Select(GetWrapperInterfaceTypeName));

        wrapperInterfaces.AddRange(implementedInterfaces.Select(interfaceType => interfaceType.ToDisplayString(NullableFullyQualifiedFormat)));
        if (wrapperInterfaces.Count > 0)
        {
            sb.Append(" : ");
            sb.Append(string.Join(", ", wrapperInterfaces.Distinct(StringComparer.Ordinal)));
        }
        sb.AppendLine();
        sb.Append(indent);
        sb.AppendLine("{");

        var innerIndent = indent + "    ";
        sb.Append(innerIndent);
        sb.Append("private readonly ");
        sb.Append(GetContainingTypeName(typeSymbol));
        sb.AppendLine(" _implementation = implementation;");
        sb.Append(innerIndent);
        sb.AppendLine("private static readonly object __nullKey = new();");
        sb.AppendLine();

        foreach (var implementedInterface in cascadingInterfaces.Distinct(NamedTypeSymbolComparer))
        {
            sb.Append(innerIndent);
            sb.Append(GetWrapperInterfaceTypeName(implementedInterface));
            sb.Append(' ');
            sb.Append(implementedInterface.ToDisplayString(NullableFullyQualifiedFormat));
            sb.AppendLine(".CascadingCompute => this;");
            sb.AppendLine();
        }

        var cacheFields = new List<string>();
        var invalidationMembers = new StringBuilder();
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
            var cacheKeyExpression = GetCacheKeyExpression(method, includedParameters).Replace("implementation.", "_implementation.");
            var taintsExpression = GetCacheContextTaintsExpression(method).Replace("implementation.", "_implementation.");
            var invalidationTaintsExpression = taintsExpression.Replace("_implementation.", "cascadingCompute._implementation.");
            var invocationArguments = GetInvocationArguments(method, includedParameters);
            var includeCacheContextInInvalidateByParameters = false;
            var invalidateParameters = GetInvalidationParameterList(method, includedParameters, includeCacheContextInInvalidateByParameters);
            var invalidationCacheKeyExpression = GetInvalidationCacheKeyExpression(method, includedParameters, includeCacheContextInInvalidateByParameters)
                .Replace("implementation.", "cascadingCompute._implementation.")
                .Replace("__nullKey", "CascadingComputeWrapper.__nullKey");
            var includeCacheContextInInvalidateByPredicate = true;
            var predicateDelegateType = GetPredicateDelegateType(method, includedParameters, includeCacheContextInInvalidateByPredicate);
            var predicateInvocationArguments = GetPredicateInvocationArguments(method, includedParameters, includeCacheContextInInvalidateByPredicate);
            var invalidationPredicateInvocationArguments = predicateInvocationArguments.Replace("__nullKey", "CascadingComputeWrapper.__nullKey");
            var useStaticFactory = includedParameters.Count == method.Parameters.Length;
            var methodAccessibility = GetAccessibility(method.DeclaredAccessibility);
            var methodTypeParameters = GetMethodTypeParameters(method);
            var methodConstraints = GetMethodConstraints(method);
            var invocationTypeArguments = GetMethodTypeArguments(method);
            var invalidationCacheContextElements = includeCacheContextInInvalidateByParameters ? GetCacheContextElements(method) : [];

            sb.Append(innerIndent);
            sb.Append(methodAccessibility);
            sb.Append(' ');
            sb.Append(method.ReturnType.ToDisplayString(NullableFullyQualifiedFormat));
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
                sb.Append(method.ReturnType.ToDisplayString(NullableFullyQualifiedFormat));
                sb.Append(')');
            }
            sb.Append(fieldName);
            sb.Append(".GetOrAdd(_implementation, ");
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
            sb.Append(", ");
            sb.Append(taintsExpression);
            sb.AppendLine(");");
            sb.Append(innerIndent);
            sb.AppendLine("}");
            sb.AppendLine();

            var invalidationInnerIndent = innerIndent + "    ";
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append("/// <summary>");
            invalidationMembers.AppendLine();
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append("/// Invalidates the cached result of ");
            invalidationMembers.Append(method.Name);
            invalidationMembers.AppendLine(" for the specified cache key.");
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append("/// </summary>");
            invalidationMembers.AppendLine();
            foreach (var parameter in includedParameters)
            {
                invalidationMembers.Append(invalidationInnerIndent);
                invalidationMembers.Append("/// <param name=\"");
                invalidationMembers.Append(parameter.Name);
                invalidationMembers.Append("\">Cache key value for ");
                invalidationMembers.Append(parameter.Name);
                invalidationMembers.AppendLine(".</param>");
            }
            foreach (var cacheContextElement in invalidationCacheContextElements)
            {
                invalidationMembers.Append(invalidationInnerIndent);
                invalidationMembers.Append("/// <param name=\"");
                invalidationMembers.Append(cacheContextElement.Name);
                invalidationMembers.Append("\">Cache context value for ");
                invalidationMembers.Append(cacheContextElement.Name);
                invalidationMembers.AppendLine(".</param>");
            }
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append(methodAccessibility);
            invalidationMembers.Append(" void Invalidate");
            invalidationMembers.Append(method.Name);
            invalidationMembers.Append(methodTypeParameters);
            invalidationMembers.Append('(');
            invalidationMembers.Append(invalidateParameters);
            invalidationMembers.AppendLine(")");
            foreach (var constraint in methodConstraints)
            {
                invalidationMembers.Append(invalidationInnerIndent);
                invalidationMembers.Append("    ");
                invalidationMembers.AppendLine(constraint);
            }
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.AppendLine("{");
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append("    cascadingCompute.");
            invalidationMembers.Append(fieldName);
            invalidationMembers.Append(".Invalidate(");
            invalidationMembers.Append(invalidationCacheKeyExpression);
            invalidationMembers.Append(", ");
            invalidationMembers.Append(invalidationTaintsExpression);
            invalidationMembers.AppendLine(");");
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.AppendLine("}");
            invalidationMembers.AppendLine();

            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append("/// <summary>");
            invalidationMembers.AppendLine();
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append("/// Invalidates cached results of ");
            invalidationMembers.Append(method.Name);
            invalidationMembers.AppendLine(" matching the specified predicate.");
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append("/// </summary>");
            invalidationMembers.AppendLine();
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.AppendLine("/// <param name=\"predicate\">Predicate used to select cache entries to invalidate.</param>");
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append(methodAccessibility);
            invalidationMembers.Append(" void Invalidate");
            invalidationMembers.Append(method.Name);
            invalidationMembers.Append(methodTypeParameters);
            invalidationMembers.Append("(");
            invalidationMembers.Append(predicateDelegateType);
            invalidationMembers.AppendLine(" predicate)");
            foreach (var constraint in methodConstraints)
            {
                invalidationMembers.Append(invalidationInnerIndent);
                invalidationMembers.Append("    ");
                invalidationMembers.AppendLine(constraint);
            }
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.AppendLine("{");
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.Append("    cascadingCompute.");
            invalidationMembers.Append(fieldName);
            invalidationMembers.Append(".InvalidateWhere(p => ");
            if (string.IsNullOrEmpty(predicateInvocationArguments))
            {
                invalidationMembers.Append("predicate()");
            }
            else
            {
                invalidationMembers.Append("predicate(");
                invalidationMembers.Append(invalidationPredicateInvocationArguments);
                invalidationMembers.Append(')');
            }
            invalidationMembers.AppendLine(");");
            invalidationMembers.Append(invalidationInnerIndent);
            invalidationMembers.AppendLine("}");
            invalidationMembers.AppendLine();

        }

        foreach (var passthroughMethod in passthroughInterfaceMethods)
        {
            var method = passthroughMethod.Method;
            var interfaceTypeName = passthroughMethod.InterfaceType.ToDisplayString(NullableFullyQualifiedFormat);
            var methodTypeParameters = GetMethodTypeParameters(method);
            var methodConstraints = GetMethodConstraints(method);
            var invocationTypeArguments = GetMethodTypeArguments(method);
            var parameterList = GetPassthroughParameterList(method);
            var invocationArguments = GetPassthroughInvocationArguments(method);

            sb.Append(innerIndent);
            sb.Append("public ");
            sb.Append(method.ReturnType.ToDisplayString(NullableFullyQualifiedFormat));
            sb.Append(' ');
            sb.Append(method.Name);
            sb.Append(methodTypeParameters);
            sb.Append('(');
            sb.Append(parameterList);
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
            if (!method.ReturnsVoid)
                sb.Append("return ");
            sb.Append("((");
            sb.Append(interfaceTypeName);
            sb.Append(")_implementation).");
            sb.Append(method.Name);
            sb.Append(invocationTypeArguments);
            sb.Append('(');
            sb.Append(invocationArguments);
            sb.AppendLine(");");
            sb.Append(innerIndent);
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.Append(innerIndent);
        sb.AppendLine("public class Invalidation(CascadingComputeWrapper cascadingCompute)");
        sb.Append(innerIndent);
        sb.AppendLine("{");
        sb.Append(invalidationMembers);
        sb.Append(innerIndent);
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Invalidates all cached results for all generated cascading compute methods.");
        sb.AppendLine("    /// </summary>");
        sb.Append(innerIndent);
        sb.AppendLine("    public void InvalidateAll()");
        sb.Append(innerIndent);
        sb.AppendLine("    {");
        foreach (var fieldName in cacheFields)
        {
            sb.Append(innerIndent);
            sb.Append("        cascadingCompute.");
            sb.Append(fieldName);
            sb.AppendLine(".InvalidateAll();");
        }
        sb.Append(innerIndent);
        sb.AppendLine("    }");
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

    private static string GetPredicateDelegateType(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters, bool includeCacheContext)
    {
        var contextElements = includeCacheContext ? GetCacheContextElements(method) : [];
        if (includedParameters.Count == 0 && contextElements.Count == 0)
            return "global::System.Func<bool>";

        var typeArguments = includedParameters
            .Select(parameter => parameter.Type.ToDisplayString(NullableFullyQualifiedFormat))
            .Concat(contextElements.Select(contextElement => contextElement.TypeName))
            .Concat(["bool"]);

        return $"global::System.Func<{string.Join(", ", typeArguments)}>";
    }

    private static string GetPredicateInvocationArguments(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters, bool includeCacheContext)
    {
        var contextElements = includeCacheContext ? GetCacheContextElements(method) : [];
        if (includedParameters.Count == 0 && contextElements.Count == 0)
            return string.Empty;

        var arguments = new List<string>();
        var usesTuple = GetCacheKeyElementCount(method, includedParameters) > 1;
        arguments.AddRange(includedParameters.Select(parameter => GetPredicateInvocationArgument(method, parameter, usesTuple)));

        if (contextElements.Count > 0)
        {
            if (usesTuple)
            {
                arguments.AddRange(contextElements.Select(contextElement => $"p.{contextElement.Name}"));
            }
            else
            {
                arguments.Add("p");
            }
        }

        return string.Join(", ", arguments);
    }

    private static string GetPredicateInvocationArgument(IMethodSymbol method, IParameterSymbol parameter, bool usesTuple)
    {
        var parameterType = parameter.Type.ToDisplayString(NullableFullyQualifiedFormat);
        var expression = usesTuple ? $"p.{EscapeIdentifier(parameter.Name)}" : "p";
        if (IsNullableReferenceType(parameter.Type))
            return $"global::System.Object.ReferenceEquals({expression}, __nullKey) ? null : ({parameterType}){expression}!";

        if (method.IsGenericMethod || ContainsTypeParameter(parameter.Type) || IsNullableReferenceType(parameter.Type))
            return $"({parameterType}){expression}!";

        return expression;
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

    private static string GetCacheContextTaintsExpression(IMethodSymbol method)
    {
        var taints = GetCacheContextTaints(method).ToList();
        if (taints.Count == 0)
            return "global::System.Array.Empty<(string Key, object Value)>()";

        var elements = taints.Select(taint =>
            $"(\"{EscapeStringLiteral(taint.Key)}\", (object){taint.ValueExpression}!)");
        return $"[{string.Join(", ", elements)}]";
    }

    private static IEnumerable<CacheContextTaint> GetCacheContextTaints(IMethodSymbol method)
    {
        foreach (var member in method.ContainingType.GetMembers())
        {
            if (member is IFieldSymbol fieldSymbol
                && !fieldSymbol.IsStatic
                && !fieldSymbol.IsImplicitlyDeclared
                && TryGetCacheContextType(fieldSymbol.Type, out var fieldContextType))
            {
                yield return new CacheContextTaint(
                    GetCacheContextTaintKey(fieldSymbol.Type, fieldContextType),
                    $"implementation.{EscapeIdentifier(fieldSymbol.Name)}.GetCacheContext()");
                continue;
            }

            if (member is IPropertySymbol propertySymbol
                && !propertySymbol.IsStatic
                && propertySymbol.GetMethod is not null
                && TryGetCacheContextType(propertySymbol.Type, out var propertyContextType))
            {
                yield return new CacheContextTaint(
                    GetCacheContextTaintKey(propertySymbol.Type, propertyContextType),
                    $"implementation.{EscapeIdentifier(propertySymbol.Name)}.GetCacheContext()");
            }
        }

        foreach (var primaryConstructorCacheContext in GetPrimaryConstructorCacheContextParameters(method.ContainingType))
        {
            yield return new CacheContextTaint(
                GetCacheContextTaintKey(primaryConstructorCacheContext.ProviderTypeName, primaryConstructorCacheContext.ContextTypeName),
                $"implementation.{primaryConstructorCacheContext.HelperMethodName}()");
        }
    }

    private static string GetCacheContextTaintKey(ITypeSymbol providerType, ITypeSymbol contextType)
        => GetCacheContextTaintKey(
            providerType.ToDisplayString(NullableFullyQualifiedFormat),
            contextType.ToDisplayString(NullableFullyQualifiedFormat));

    private static string GetCacheContextTaintKey(string providerTypeName, string contextTypeName)
        => providerTypeName + "|" + contextTypeName;

    private static string EscapeStringLiteral(string value)
        => value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");

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

    private static string GetInvalidationParameterList(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters, bool includeCacheContext)
    {
        var parameters = new List<string>();
        parameters.AddRange(includedParameters.Select(parameter =>
        {
            var modifier = parameter.IsParams ? "params " : string.Empty;
            return $"{modifier}{parameter.Type.ToDisplayString(NullableFullyQualifiedFormat)} {EscapeIdentifier(parameter.Name)}";
        }));

        if (includeCacheContext)
            parameters.AddRange(GetCacheContextElements(method).Select(cacheContextElement => $"{cacheContextElement.TypeName} {cacheContextElement.Name}"));

        return string.Join(", ", parameters);
    }

    private static string GetInvalidationCacheKeyExpression(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters, bool includeCacheContext)
    {
        var elements = new List<string>();

        foreach (var typeSymbol in GetGenericMethodTypeSymbols(method))
            elements.Add($"typeof({typeSymbol.ToDisplayString(NullableFullyQualifiedFormat)})");

        foreach (var parameter in includedParameters)
        {
            if (ContainsTypeParameter(parameter.Type))
                elements.Add($"((object){EscapeIdentifier(parameter.Name)})!");
            else if (IsNullableReferenceType(parameter.Type))
                elements.Add($"((object?){EscapeIdentifier(parameter.Name)}) ?? __nullKey");
            else
                elements.Add(EscapeIdentifier(parameter.Name));
        }

        foreach (var cacheContextElement in GetCacheContextElements(method))
            elements.Add(includeCacheContext ? cacheContextElement.Name : cacheContextElement.Expression);

        if (elements.Count == 0)
            return "default";

        if (elements.Count == 1)
            return elements[0];

        return GetParameterTupleExpression(elements);
    }

    private static IEnumerable<string> GetCacheKeyExpressionElements(IMethodSymbol method, IReadOnlyList<IParameterSymbol> includedParameters)
    {
        foreach (var typeSymbol in GetGenericMethodTypeSymbols(method))
            yield return $"typeof({typeSymbol.ToDisplayString(NullableFullyQualifiedFormat)})";

        foreach (var parameter in includedParameters)
        {
            if (ContainsTypeParameter(parameter.Type))
                yield return $"((object){EscapeIdentifier(parameter.Name)})!";
            else if (IsNullableReferenceType(parameter.Type))
                yield return $"((object?){EscapeIdentifier(parameter.Name)}) ?? __nullKey";
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
                || IsNullableReferenceType(parameter.Type)
                ? "global::System.Object"
                : parameter.Type.ToDisplayString(NullableFullyQualifiedFormat);
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
        if (IsNullableReferenceType(parameter.Type))
        {
            var parameterType = parameter.Type.ToDisplayString(NullableFullyQualifiedFormat);
            return $"global::System.Object.ReferenceEquals({expression}, __nullKey) ? null : ({parameterType}){expression}!";
        }

        if (method.IsGenericMethod || ContainsTypeParameter(parameter.Type) || IsNullableReferenceType(parameter.Type))
        {
            var parameterType = parameter.Type.ToDisplayString(NullableFullyQualifiedFormat);
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

    private static bool IsNullableReferenceType(ITypeSymbol typeSymbol)
        => !typeSymbol.IsValueType && typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;

    private static IEnumerable<ITypeSymbol> GetGenericMethodTypeSymbols(IMethodSymbol method)
        => method.TypeParameters.Length > 0 ? method.TypeParameters : method.TypeArguments;

    private static string GetCacheResultType(IMethodSymbol method)
        => method.IsGenericMethod ? "global::System.Object" : method.ReturnType.ToDisplayString(NullableFullyQualifiedFormat);

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
                    fieldContextType.ToDisplayString(NullableFullyQualifiedFormat)));
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
                    propertyContextType.ToDisplayString(NullableFullyQualifiedFormat)));
            }
        }

        foreach (var primaryConstructorCacheContext in GetPrimaryConstructorCacheContextParameters(method.ContainingType))
        {
            elements.Add(new CacheContextElement(
                $"cacheContext{index++}",
                $"implementation.{primaryConstructorCacheContext.HelperMethodName}()",
                primaryConstructorCacheContext.ContextTypeName));
        }

        return elements;
    }

    private static IReadOnlyList<PrimaryConstructorCacheContextParameter> GetPrimaryConstructorCacheContextParameters(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind != TypeKind.Class)
            return [];

        var primaryConstructor = typeSymbol.InstanceConstructors
            .FirstOrDefault(ctor => ctor.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<ClassDeclarationSyntax>()
                .Any(classDeclaration => classDeclaration.ParameterList is not null));

        if (primaryConstructor is null)
            return [];

        var parameters = new List<PrimaryConstructorCacheContextParameter>();
        var index = 0;
        foreach (var parameter in primaryConstructor.Parameters)
        {
            if (!TryGetCacheContextType(parameter.Type, out var cacheContextType))
                continue;

            if (IsPrimaryConstructorParameterAssignedToCacheContextMember(typeSymbol, parameter))
                continue;

            parameters.Add(new PrimaryConstructorCacheContextParameter(
                parameter.Name,
                parameter.Type.ToDisplayString(NullableFullyQualifiedFormat),
                cacheContextType.ToDisplayString(NullableFullyQualifiedFormat),
                $"__GetPrimaryConstructorCacheContext{index++}"));
        }

        return parameters;
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

    private static bool IsPrimaryConstructorParameterAssignedToCacheContextMember(INamedTypeSymbol typeSymbol, IParameterSymbol parameter)
    {
        foreach (var field in typeSymbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsStatic || field.IsImplicitlyDeclared || !TryGetCacheContextType(field.Type, out _))
                continue;

            foreach (var syntaxReference in field.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: IdentifierNameSyntax identifier }
                    && string.Equals(identifier.Identifier.ValueText, parameter.Name, StringComparison.Ordinal))
                    return true;
            }
        }

        foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsImplicitlyDeclared || !TryGetCacheContextType(property.Type, out _))
                continue;

            foreach (var syntaxReference in property.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is PropertyDeclarationSyntax { Initializer.Value: IdentifierNameSyntax identifier }
                    && string.Equals(identifier.Identifier.ValueText, parameter.Name, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static bool IsCacheContextProviderInterface(INamedTypeSymbol typeSymbol)
        => typeSymbol is { Name: CacheContextProviderInterfaceName, Arity: 1 }
            && typeSymbol.ContainingNamespace.ToDisplayString() == CacheContextProviderInterfaceNamespace;

    private sealed record CacheContextElement(string Name, string Expression, string TypeName);
    private sealed record PrimaryConstructorCacheContextParameter(string ParameterName, string ProviderTypeName, string ContextTypeName, string HelperMethodName);
    private sealed record CacheContextTaint(string Key, string ValueExpression);
    private sealed record InterfacePassthroughMethod(INamedTypeSymbol InterfaceType, IMethodSymbol Method);

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
        var typeName = typeSymbol.ToDisplayString(NullableFullyQualifiedFormat).Replace("global::", string.Empty);
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
        expressionBuilder.Append(attributeData.AttributeClass.ToDisplayString(NullableFullyQualifiedFormat));
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
            TypedConstantKind.Type => $"typeof({((ITypeSymbol)typedConstant.Value!).ToDisplayString(NullableFullyQualifiedFormat)})",
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
            return $"{enumType.ToDisplayString(NullableFullyQualifiedFormat)}.{enumMember.Name}";

        return $"({enumType.ToDisplayString(NullableFullyQualifiedFormat)}){SymbolDisplay.FormatPrimitive(enumValue, quoteStrings: true, useHexadecimalNumbers: false)}";
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

        return $"new {arrayType.ElementType.ToDisplayString(NullableFullyQualifiedFormat)}[] {{ {string.Join(", ", values)} }}";
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
            var typeName = parameter.Type.ToDisplayString(NullableFullyQualifiedFormat);
            var defaultValue = parameter.HasExplicitDefaultValue ? " = " + GetDefaultValueExpression(parameter) : string.Empty;
            return $"{modifier}{typeName} {EscapeIdentifier(parameter.Name)}{defaultValue}";
        }));
    }

    private static string GetPassthroughParameterList(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(parameter =>
        {
            var refModifier = parameter.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => string.Empty
            };
            var paramsModifier = parameter.IsParams ? "params " : string.Empty;
            var typeName = parameter.Type.ToDisplayString(NullableFullyQualifiedFormat);
            var defaultValue = parameter.HasExplicitDefaultValue ? " = " + GetDefaultValueExpression(parameter) : string.Empty;
            return $"{refModifier}{paramsModifier}{typeName} {EscapeIdentifier(parameter.Name)}{defaultValue}";
        }));
    }

    private static string GetPassthroughInvocationArguments(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(parameter =>
        {
            var modifier = parameter.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => string.Empty
            };

            return modifier + EscapeIdentifier(parameter.Name);
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

            parts.AddRange(typeParameter.ConstraintTypes.Select(constraintType => constraintType.ToDisplayString(NullableFullyQualifiedFormat)));

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

            parts.AddRange(typeParameter.ConstraintTypes.Select(constraintType => constraintType.ToDisplayString(NullableFullyQualifiedFormat)));

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
        var name = typeSymbol.ToDisplayString(NullableFullyQualifiedFormat)
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
            return parameter.Type.IsValueType
                ? $"default({parameter.Type.ToDisplayString(NullableFullyQualifiedFormat)})"
                : "null";

        if (parameter.Type.TypeKind == TypeKind.Enum && parameter.Type is INamedTypeSymbol enumType)
        {
            var enumMember = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(member => member.HasConstantValue && Equals(member.ConstantValue, parameter.ExplicitDefaultValue));

            if (enumMember is not null)
                return $"{enumType.ToDisplayString(NullableFullyQualifiedFormat)}.{enumMember.Name}";

            return $"({enumType.ToDisplayString(NullableFullyQualifiedFormat)}){SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false)}";
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
        if (!HasCascadingComputeEnabledAttribute(interfaceSymbol))
        {
            sb.Append(indent);
            sb.AppendLine("[global::hhnl.CascadingCompute.Attributes.CascadingComputeEnabledAttribute]");
        }

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
            var methodTypeParameters = GetMethodTypeParameters(method);
            var methodConstraints = GetMethodConstraints(method);
            sb.Append(memberIndent);
            sb.Append(method.ReturnType.ToDisplayString(NullableFullyQualifiedFormat));
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
            sb.AppendLine();
        }
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
        var name = interfaceSymbol.ToDisplayString(NullableFullyQualifiedFormat)
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

        return $"<{string.Join(", ", typeSymbol.TypeArguments.Select(argument => argument.ToDisplayString(NullableFullyQualifiedFormat)))}>";
    }

    private sealed class NamedTypeSymbolEqualityComparer : IEqualityComparer<INamedTypeSymbol>
    {
        public bool Equals(INamedTypeSymbol? x, INamedTypeSymbol? y)
            => SymbolEqualityComparer.Default.Equals(x, y);

        public int GetHashCode(INamedTypeSymbol obj)
            => SymbolEqualityComparer.Default.GetHashCode(obj);
    }
}


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
    private const string AttributeMetadataName = "hhnl.CascadingCompute.Shared.Attributes.CascadingComputeAttribute";
    private const string CacheEntryLifetimeObserverAttributeMetadataName = "hhnl.CascadingCompute.Shared.Attributes.CacheEntryLifetimeObserverAttribute";

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
            var cacheKeyType = GetCacheKeyType(method);
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
            var cacheKeyExpression = GetCacheKeyExpression(method);
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
            sb.Append(", static (s, p) => ");
            if (method.IsGenericMethod)
                sb.Append("(object)");
            sb.Append("s.");
            sb.Append(method.Name);
            sb.Append(invocationTypeArguments);
            sb.Append('(');
            sb.Append(GetInvocationArguments(method));
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

    private static string GetInvocationArguments(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return string.Empty;

        var usesTuple = GetCacheKeyElementCount(method) > 1;
        return string.Join(", ", method.Parameters.Select(parameter => GetInvocationArgument(method, parameter, usesTuple)));
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

    private static string GetCacheKeyExpression(IMethodSymbol method)
    {
        var elements = GetCacheKeyExpressionElements(method).ToList();
        if (elements.Count == 0)
            return "default";
        if (elements.Count == 1)
            return elements[0];

        return GetParameterTupleExpression(elements);
    }

    private static string GetCacheKeyType(IMethodSymbol method)
    {
        var elementTypes = GetCacheKeyTypeElements(method, includeNames: false).ToList();
        if (elementTypes.Count == 0)
            return "global::System.ValueTuple";
        if (elementTypes.Count == 1)
            return elementTypes[0];

        var namedElements = GetCacheKeyTypeElements(method, includeNames: true).ToList();
        return GetParameterTupleType(namedElements);
    }

    private static IEnumerable<string> GetCacheKeyExpressionElements(IMethodSymbol method)
    {
        foreach (var typeSymbol in GetGenericMethodTypeSymbols(method))
            yield return $"typeof({typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";

        foreach (var parameter in method.Parameters)
        {
            if (ContainsMethodTypeParameter(parameter.Type, method))
                yield return $"(object){EscapeIdentifier(parameter.Name)}";
            else
                yield return EscapeIdentifier(parameter.Name);
        }
    }

    private static IEnumerable<string> GetCacheKeyTypeElements(IMethodSymbol method, bool includeNames)
    {
        foreach (var _ in GetGenericMethodTypeSymbols(method))
            yield return "global::System.Type";

        foreach (var parameter in method.Parameters)
        {
            var typeName = ContainsMethodTypeParameter(parameter.Type, method)
                ? "global::System.Object"
                : parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (includeNames)
                yield return $"{typeName} {EscapeIdentifier(parameter.Name)}";
            else
                yield return typeName;
        }
    }

    private static int GetCacheKeyElementCount(IMethodSymbol method)
        => method.TypeParameters.Length + method.Parameters.Length;

    private static string GetInvocationArgument(IMethodSymbol method, IParameterSymbol parameter, bool usesTuple)
    {
        var expression = usesTuple ? $"p.{EscapeIdentifier(parameter.Name)}" : "p";
        if (method.IsGenericMethod || ContainsMethodTypeParameter(parameter.Type, method))
        {
            var parameterType = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({parameterType}){expression}";
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
        if (method.Parameters.Length == 0)
            return string.Empty;

        return string.Join(", ", method.Parameters.Select(parameter =>
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

        return SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false);
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

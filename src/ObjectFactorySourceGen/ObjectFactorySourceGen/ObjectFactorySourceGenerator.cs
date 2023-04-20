using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ObjectFactorySourceGen;

[Generator]
public class ObjectFactorySourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            //Debugger.Launch();
        }
#endif
        context.RegisterForSyntaxNotifications(() => new ObjectFactorySyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not ObjectFactorySyntaxReceiver receiver)
        {
            return;
        }

        var dic = _semanticModelCache;

        var sw = Stopwatch.StartNew();

        ExecuteInternal(context, receiver);
        sw.Stop();

        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor(
                "ObjectFactorySourceGenerator",
                "Source Generator Performance",
                "ObjectFactorySourceGenerator took {0}ms to execute.",
                "ObjectFactorySourceGenerator",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true),
            Location.None,
            sw.Elapsed.TotalMilliseconds);

        context.ReportDiagnostic(diagnostic);
    }

    private void ExecuteInternal(GeneratorExecutionContext context, ObjectFactorySyntaxReceiver receiver)
    {
        foreach (var factoryClass in receiver.FactoryClasses)
        {
            // var semanticModel = context.Compilation.GetSemanticModel(factoryClass.Declaration.SyntaxTree);
            var semanticModel = GetSemanticModel(context.Compilation, factoryClass.Declaration.SyntaxTree);
            var factorySymbol = semanticModel.GetDeclaredSymbol(factoryClass.Declaration);
            var relayFactoryAttributes = factorySymbol.GetAttributes().Where(attr => attr.AttributeClass.Name.StartsWith("RelayFactoryOf"));

            var serviceProviderSymbol = context.Compilation.GetTypeByMetadataName("System.IServiceProvider");
            var serviceProviderFieldOrProperty = factorySymbol.GetMembers().FirstOrDefault(m => m.Kind == SymbolKind.Field || m.Kind == SymbolKind.Property && (m as ITypeSymbol).Equals(serviceProviderSymbol));

            if (serviceProviderFieldOrProperty == null)
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "ObjectFactorySourceGenerator",
                        "No IServiceProvider field or property found",
                        "The {0} class must contain an IServiceProvider field or property.",
                        "ObjectFactorySourceGenerator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    factorySymbol.Locations[0],
                    factorySymbol.Name);

                context.ReportDiagnostic(diagnostic);
                continue;
            }

            var serviceProviderName = serviceProviderFieldOrProperty.Name;
            var generatedCode = new StringBuilder();
            generatedCode.AppendLine("using System;");
            generatedCode.AppendLine("using System.Collections.Generic;");
            generatedCode.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            generatedCode.AppendLine();

            var namespaceSymbol = factorySymbol.ContainingNamespace;
            generatedCode.AppendLine($"namespace {namespaceSymbol}");
            generatedCode.AppendLine("{");

            var factoryClassName = factorySymbol.Name;
            generatedCode.AppendLine($"partial class {factoryClassName}");
            generatedCode.AppendLine("{");

            var interceptorMethodSymbols = factorySymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.Name == "Intercept" && m.IsStatic == false && m.DeclaredAccessibility == Accessibility.Private)
                .ToList();

            var interceptorMethodReturnsVoid = interceptorMethodSymbols.FirstOrDefault(m => m.ReturnType.SpecialType == SpecialType.System_Void);
            var interceptorMethodReturnsObject = interceptorMethodSymbols.FirstOrDefault(m => m.ReturnType.SpecialType == SpecialType.System_Object);

            // Generate the CreateXXX(...) methods
            foreach (ConstructorDeclarationSyntax constructor in receiver.CommandTypeConstructors)
            {
                var constructorModel = GetSemanticModel(context.Compilation, constructor.SyntaxTree);
                IMethodSymbol constructorSymbol = constructorModel.GetDeclaredSymbol(constructor);
                INamedTypeSymbol containingTypeSymbol = constructorSymbol.ContainingType;

                var inherits = InheritsFromBaseType(containingTypeSymbol, factoryClass.BaseTypes, semanticModel);
                if (!inherits)
                {
                    continue;
                }

                foreach (var relayFactoryAttribute in relayFactoryAttributes)
                {
                    var commandTypeBase = relayFactoryAttribute.ConstructorArguments[0].Value;

                    if (commandTypeBase.Equals(containingTypeSymbol.BaseType))
                    {
                        var returnType = containingTypeSymbol.Name;
                        var parameters = constructorSymbol.Parameters.Where(p => !HasFromServicesAttribute(p));

                        generatedCode.AppendLine($"public {returnType} Create{returnType}({string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"))})");
                        generatedCode.AppendLine("{");

                        GenerateParameterResolution(serviceProviderName, generatedCode, constructorSymbol.Parameters);

                       

                        generatedCode.AppendLine($"var result = new {returnType}({string.Join(", ", constructorSymbol.Parameters.Select(p => p.Name))});");

                        var interceptorMethod = interceptorMethodSymbols.FirstOrDefault(m => m.ReturnType.Equals(containingTypeSymbol));
                        if (interceptorMethod != null)
                        {
                            var interceptCall = $"Intercept(result)";
                            if (!interceptorMethod.ReturnType.SpecialType.Equals(SpecialType.System_Void))
                            {
                                generatedCode.AppendLine($"result = {interceptCall};");
                            }
                            else
                            {
                                generatedCode.AppendLine(interceptCall + ";");
                            }
                        }

                        if (interceptorMethodReturnsObject != null)
                        {
                            generatedCode.AppendLine($"result = Intercept(result as Object) as {returnType};");
                        }
                        else if (interceptorMethodReturnsVoid != null)
                        {
                            generatedCode.AppendLine($"Intercept(result);");
                        }

                        generatedCode.AppendLine($"return result;");
                        generatedCode.AppendLine("}");
                        generatedCode.AppendLine();
                    }
                }
            }

            generatedCode.AppendLine("}");
            generatedCode.AppendLine("}");

            var fileName = $"{factoryClassName}_GeneratedFactoryMethods.generated.cs";
            var stringCode = generatedCode.ToString();

            context.AddSource(fileName, SourceText.From(stringCode, Encoding.UTF8));
        }
    }

    private static void GenerateParameterResolution(string serviceProviderName, StringBuilder generatedCode, ImmutableArray<IParameterSymbol> parameters)
    {
        var parametersFromService = parameters
            .Where(HasFromServicesAttribute)
            .GroupBy(x => x.Type, SymbolEqualityComparer.Default);

        foreach (var fromServicesParameters in parametersFromService)
        {
            var parameterType = fromServicesParameters.Key;
            var parametersX = fromServicesParameters.ToList();
            if (parametersX.Count <= 1)
            {
                var fromServicesParameter = parametersX[0];
                generatedCode.AppendLine($"{fromServicesParameter.Type} {fromServicesParameter.Name} = {serviceProviderName}.GetRequiredService<{fromServicesParameter.Type}>();");
            }
            else
            {
                //parameterType.Name make first letter lowercase

                var name = parameterType.Name.MakeFirstCharLowercase();

                generatedCode.AppendLine($"var {name}Instances = {serviceProviderName}.GetRequiredService<IEnumerable<{parameterType.Name}>>();");
                generatedCode.AppendLine($"using var {name}Enumerator = {name}Instances.GetEnumerator();");

                bool first = true;
                foreach (var fromServicesParameter in parametersX)
                {
                    generatedCode.AppendLine($"if (!{name}Enumerator.MoveNext())");
                    generatedCode.AppendLine("{");
                    if (!first)
                    {
                        generatedCode.AppendLine($"{name}Enumerator.Reset();");
                    }
                    else
                    {
                        generatedCode.AppendLine($"throw new InvalidOperationException(\"No service for type '{parameterType}' has been registered.\");");
                    }
                    generatedCode.AppendLine("}");
                    generatedCode.AppendLine($"{fromServicesParameter.Type} {fromServicesParameter.Name} = {name}Enumerator.Current;");
                    first = false;
                }
            }
        }
    }

    public bool InheritsFromBaseType(INamedTypeSymbol containingTypeSymbol, IEnumerable<TypeSyntax> baseTypes, SemanticModel semanticModel)
    {
        foreach (var baseTypeSyntax in baseTypes)
        {
            var baseType = semanticModel.GetTypeInfo(baseTypeSyntax).Type as INamedTypeSymbol;
            if (baseType != null && containingTypeSymbol.InheritsFrom(baseType))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ConstructorInheritsFromBaseType(ConstructorDeclarationSyntax constructor, ClassDeclarationSyntax factoryClass, SemanticModel semanticModel)
    {
        var baseTypes = factoryClass.BaseList.Types.Select(t => semanticModel.GetTypeInfo(t.Type)).ToList();
        var constructorSymbol = semanticModel.GetDeclaredSymbol(constructor);
        var containingTypeSymbol = constructorSymbol.ContainingType;

        foreach (var baseType in baseTypes)
        {
            var currentType = containingTypeSymbol.BaseType;

            while (currentType != null)
            {
                if (SymbolEqualityComparer.Default.Equals(currentType, baseType.Type))
                {
                    return true;
                }

                currentType = currentType.BaseType;
            }
        }

        return false;
    }

    private static bool HasFromServicesAttribute(IParameterSymbol parameterSymbol)
    {
        return parameterSymbol.GetAttributes().Any(attr => attr.AttributeClass.Name.StartsWith("FromServices"));
    }

    private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModelCache = new Dictionary<SyntaxTree, SemanticModel>();

    private SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        if (!_semanticModelCache.TryGetValue(syntaxTree, out var semanticModel))
        {
            semanticModel = compilation.GetSemanticModel(syntaxTree);
            _semanticModelCache[syntaxTree] = semanticModel;
        }

        return semanticModel;
    }

}

public static class Extensions
{
    public static bool InheritsFrom(this INamedTypeSymbol derivedType, INamedTypeSymbol baseType)
    {
        if (derivedType == null || baseType == null)
        {
            return false;
        }

        if (derivedType.BaseType == null)
        {
            return false;
        }

        if (derivedType.BaseType.Equals(baseType))
        {
            return true;
        }

        return derivedType.BaseType.InheritsFrom(baseType);
    }

    public static string MakeFirstCharLowercase(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }
        var strSpan = str.AsSpan();
        var buffer = strSpan.Length <= 256 ? stackalloc char[strSpan.Length] : new char[strSpan.Length];
        strSpan.CopyTo(buffer);
        buffer[0] = char.ToLower(buffer[0]);
        return buffer.ToString();
    }

}

public class ObjectFactorySyntaxReceiver : ISyntaxReceiver
{
    internal List<FactoryInfo> FactoryClasses { get; } = new();
    public List<ConstructorDeclarationSyntax> CommandTypeConstructors { get; } = new();

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is ClassDeclarationSyntax classDeclaration
            && classDeclaration.Modifiers.Any(m => m.Text == "partial")
            )
        {
            VisitPartialDeclarations(classDeclaration);
        }

        if (syntaxNode is ConstructorDeclarationSyntax constructorDeclaration &&
            constructorDeclaration.Parent is ClassDeclarationSyntax parentClass &&
            parentClass.BaseList != null)
        {
            CommandTypeConstructors.Add(constructorDeclaration);
        }
    }

    private void VisitPartialDeclarations(ClassDeclarationSyntax classDeclaration)
    {
        var factoryAttributes = classDeclaration.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .Where(attr => attr.Name.ToString().StartsWith("RelayFactoryOf"))
                        .ToList();

        if (factoryAttributes.Count < 1)
        {
            return;
        }

        var factoryInfo = new FactoryInfo();
        factoryInfo.Declaration = classDeclaration;

        FactoryClasses.Add(factoryInfo);
        foreach (var factoryAttribute in factoryAttributes)
        {
            if (factoryAttribute.ArgumentList.Arguments.FirstOrDefault().Expression is TypeOfExpressionSyntax typeOfExpression)
            {
                factoryInfo.BaseTypes.Add(typeOfExpression.Type);
            }
        }
    }
}

internal class FactoryInfo
{
    public ClassDeclarationSyntax Declaration { get; set; }
    public List<TypeSyntax> BaseTypes { get; set; } = new();
}
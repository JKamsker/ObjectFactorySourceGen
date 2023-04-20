using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ObjectFactorySourceGen;

[Generator]
public class ObjectFactorySourceGenerator : ISourceGenerator
{
    private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModelCache = new Dictionary<SyntaxTree, SemanticModel>();

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

                var inherits = Helper.InheritsFromBaseType(containingTypeSymbol, factoryClass.BaseTypes, semanticModel);
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
                        var parameters = constructorSymbol.Parameters.Where(p => !Helper.HasFromServicesAttribute(p));

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
            .Where(Helper.HasFromServicesAttribute)
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
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

/// <summary>
/// TODO:
/// - Async Intialize
/// - Maybe basefactory instead of partial
/// -- Advantages:
/// --- No need to add the partial keyword
/// --- Intellisense hints for Hooks
/// </summary>

[Generator]
public partial class ObjectFactorySourceGenerator : ISourceGenerator
{
    private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModelCache = new Dictionary<SyntaxTree, SemanticModel>();

    public void Initialize(GeneratorInitializationContext context)
    {
#if DEBUG
        LaunchDebugger();
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
        try
        {
            ExecuteInternal(context, receiver);
        }
        catch (System.Exception ex)
        {
            var diag = Diagnostic.Create(
                new DiagnosticDescriptor(
                "ObjectFactorySourceGenerator",
                "Source Generator Exception",
                ex.ToString(),
                "ObjectFactorySourceGenerator",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true),
                Location.None);

            context.ReportDiagnostic(diag);

            //LaunchDebugger();
        }

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
        if (!receiver.FactoryClasses.Any())
        {
            //LaunchDebugger();
        }
        foreach (var factoryClass in receiver.FactoryClasses)
        {
            var semanticModel = GetSemanticModel(context.Compilation, factoryClass.Declaration.SyntaxTree);
            var factorySymbol = semanticModel.GetDeclaredSymbol(factoryClass.Declaration);

            var serviceProviderFieldOrProperty = FindServiceProviderFieldOrProperty(context, factorySymbol);
            if (serviceProviderFieldOrProperty == null)
            {
                ReportNoServiceProviderDiagnostic(context, factorySymbol);
                continue;
            }

            var serviceProviderName = serviceProviderFieldOrProperty.Name;
            var generatedCode = GenerateFactoryCode(context, receiver, factoryClass, factorySymbol, serviceProviderName);

            var fileName = $"{factorySymbol.Name}_GeneratedFactoryMethods.generated.cs";

            if (!string.IsNullOrEmpty(generatedCode))
            {
                if (!generatedCode.Contains("CreateCommandType"))
                {
                    //LaunchDebugger();
                    var diagnostic = Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "ObjectFactorySourceGenerator",
                            "No Factory  methods",
                            "No factory methods generated",
                            "ObjectFactorySourceGenerator",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        factorySymbol.Locations[0]);

                    context.ReportDiagnostic(diagnostic);
                    //throw new System.Exception("No Factory  methods");
                    //LaunchDebugger();
                    continue;
                }

                context.AddSource(fileName, SourceText.From(generatedCode, Encoding.UTF8));
            }
            else
            {
                Debugger.Break();
            }

            //var outputDir = @"C:\Users\W31rd0\source\repos\Random\ObjectFactorySourceGen\output";
            //if (Directory.Exists(outputDir))
            //{
            //    var path = Path.Combine(outputDir, fileName);
            //    File.WriteAllText(path, generatedCode);
            //    var number = 1;

            //    var current = path + "." + number;
            //    while (File.Exists(current))
            //    {
            //        number++;
            //        current = path + "." + number;
            //    }
            //    File.WriteAllText(current, generatedCode);
            //}
        }
    }

    [DebuggerStepThrough]
    private static void LaunchDebugger()
    {
        if (!Debugger.IsAttached)
        {
            Debugger.Launch();
        }
        else
        {
            Debugger.Break();
        }
    }

    private IFieldSymbol FindServiceProviderFieldOrProperty(GeneratorExecutionContext context, INamedTypeSymbol factorySymbol)
    {
        var serviceProviderSymbol = context.Compilation.GetTypeByMetadataName("System.IServiceProvider");
        return factorySymbol.GetMembers().FirstOrDefault(m =>
            m.Kind == SymbolKind.Field
            || m.Kind == SymbolKind.Property && ((m as ITypeSymbol)?.Equals(serviceProviderSymbol, SymbolEqualityComparer.Default) ?? false)
        ) as IFieldSymbol;
    }

    private void ReportNoServiceProviderDiagnostic(GeneratorExecutionContext context, INamedTypeSymbol factorySymbol)
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
    }

    private string GenerateFactoryCode(GeneratorExecutionContext context, ObjectFactorySyntaxReceiver receiver, FactoryInfo factoryClass, INamedTypeSymbol factorySymbol, string serviceProviderName)
    {
        var generatedCode = new StringBuilder();

        AppendUsingDirectives(generatedCode);
        AppendNamespaceDeclaration(generatedCode, factorySymbol.ContainingNamespace);

        AppendPartialClassDeclaration(generatedCode, factorySymbol.Name);
        var hasCreated = AppendCreateMethods(context, receiver, factoryClass, factorySymbol, serviceProviderName, generatedCode);
        if (!hasCreated)
        {
            return string.Empty;
        }

        generatedCode.AppendLine("}");
        generatedCode.AppendLine("}");

        return generatedCode.ToString();
    }

    private void AppendUsingDirectives(StringBuilder generatedCode)
    {
        generatedCode.AppendLine("using System;");
        generatedCode.AppendLine("using System.Collections.Generic;");
        generatedCode.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        generatedCode.AppendLine();
    }

    private void AppendNamespaceDeclaration(StringBuilder generatedCode, INamespaceSymbol namespaceSymbol)
    {
        generatedCode.AppendLine($"namespace {namespaceSymbol}");
        generatedCode.AppendLine("{");
    }

    private void AppendPartialClassDeclaration(StringBuilder generatedCode, string factoryClassName)
    {
        generatedCode.AppendLine($"partial class {factoryClassName}");
        generatedCode.AppendLine("{");
    }

    private bool AppendCreateMethods(GeneratorExecutionContext context, ObjectFactorySyntaxReceiver receiver, FactoryInfo factoryClass, INamedTypeSymbol factorySymbol, string serviceProviderName, StringBuilder generatedCode)
    {
        var hasCreateMethods = false;
        var interceptorMethodSymbols = GetInterceptorMethods(factorySymbol);

        foreach (ConstructorDeclarationSyntax constructor in receiver.CommandTypeConstructors)
        {
            var constructorModel = GetSemanticModel(context.Compilation, constructor.SyntaxTree);
            IMethodSymbol constructorSymbol = constructorModel.GetDeclaredSymbol(constructor);
            INamedTypeSymbol containingTypeSymbol = constructorSymbol.ContainingType;

            //var inherits = Helper.InheritsFromBaseType(containingTypeSymbol, factoryClass.BaseTypes, constructorModel);
            var inherits = InheritsFromBaseType(containingTypeSymbol, factoryClass.BaseTypes, context.Compilation);
            if (!inherits)
            {
                continue;
            }

            foreach (var relayFactoryAttribute in GetRelayFactoryAttributes(factorySymbol))
            {
                var constructorArguments = relayFactoryAttribute.ConstructorArguments;
                if (constructorArguments.Length < 1)
                {
                    // diagnostic
                    var diagnostic = Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "ObjectFactorySourceGenerator",
                            "RelayFactoryOf attribute has no constructor arguments",
                            "The {0} class must contain an IServiceProvider field or property.",
                            "ObjectFactorySourceGenerator",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        factorySymbol.Locations[0],
                        factorySymbol.Name);

                    context.ReportDiagnostic(diagnostic);
                    //
                    continue;
                }

                Debugger.Break();
                var commandTypeBase = relayFactoryAttribute.ConstructorArguments[0].Value as INamedTypeSymbol;
                if (commandTypeBase == null)
                {
                    // diagnostic
                    var diagnostic = Diagnostic.Create(
                                               new DiagnosticDescriptor(
                                                "ObjectFactorySourceGenerator",
                                                "RelayFactoryOf attribute has no constructor arguments",
                                                "The {0} class must contain an IServiceProvider field or property.",
                                                "ObjectFactorySourceGenerator",
                                                DiagnosticSeverity.Error,
                                                isEnabledByDefault: true),
                                                factorySymbol.Locations[0],
                                        factorySymbol.Name);

                    context.ReportDiagnostic(diagnostic);
                    //
                    continue;
                }

                var baseTypesEqual = commandTypeBase.Equals(containingTypeSymbol.BaseType, SymbolEqualityComparer.Default)
                    || string.Equals(commandTypeBase.ToString(), containingTypeSymbol.BaseType.ToString(), System.StringComparison.OrdinalIgnoreCase);

                if (baseTypesEqual)
                {
                    AppendCreateMethod(generatedCode, containingTypeSymbol, constructorSymbol, serviceProviderName, interceptorMethodSymbols);
                    hasCreateMethods = true;
                }
            }
        }

        return hasCreateMethods;
    }

    public bool InheritsFromBaseType(INamedTypeSymbol containingTypeSymbol, IEnumerable<TypeSyntax> baseTypes, Compilation compilation)
    {
        foreach (var baseTypeSyntax in baseTypes)
        {
            var semanticModel = GetSemanticModel(compilation, baseTypeSyntax.SyntaxTree);
            var baseType = semanticModel.GetTypeInfo(baseTypeSyntax).Type as INamedTypeSymbol;
            if (baseType != null && containingTypeSymbol.InheritsFrom(baseType))
            {
                return true;
            }
        }
        return false;
    }

    private IEnumerable<AttributeData> GetRelayFactoryAttributes(INamedTypeSymbol factorySymbol)
    {
        return factorySymbol.GetAttributes().Where(attr => attr.AttributeClass.Name.StartsWith("RelayFactoryOf"));
    }

    private List<IMethodSymbol> GetInterceptorMethods(INamedTypeSymbol factorySymbol)
    {
        return factorySymbol.GetMembers()
        .OfType<IMethodSymbol>()
        .Where(m => m.Name == "Intercept" && m.IsStatic == false && m.DeclaredAccessibility == Accessibility.Private)
        .ToList();
    }

    private void AppendCreateMethod(StringBuilder generatedCode, INamedTypeSymbol containingTypeSymbol, IMethodSymbol constructorSymbol, string serviceProviderName, List<IMethodSymbol> interceptorMethodSymbols)
    {
        var returnType = containingTypeSymbol.Name;
        var parameters = constructorSymbol.Parameters.Where(p => !Helper.HasFromServicesAttribute(p));
        generatedCode.AppendLine($"public {returnType} Create{returnType}({string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"))})");
        generatedCode.AppendLine("{");

        GenerateParameterResolution(serviceProviderName, generatedCode, constructorSymbol.Parameters);

        generatedCode.AppendLine($"var result = new {returnType}({string.Join(", ", constructorSymbol.Parameters.Select(p => p.Name))});");

        var interceptorMethod = interceptorMethodSymbols.FirstOrDefault(m => m.ReturnType.Equals(containingTypeSymbol, SymbolEqualityComparer.Default));
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

        var interceptorMethodReturnsObject = interceptorMethodSymbols.FirstOrDefault(m => m.ReturnType.SpecialType == SpecialType.System_Object);
        var interceptorMethodReturnsVoid = interceptorMethodSymbols.FirstOrDefault(m => m.ReturnType.SpecialType == SpecialType.System_Void);

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
                        generatedCode.AppendLine($"{name}Enumerator.MoveNext();");
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
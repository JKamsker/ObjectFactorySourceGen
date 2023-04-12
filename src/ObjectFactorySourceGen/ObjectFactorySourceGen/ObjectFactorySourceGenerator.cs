using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ObjectFactorySourceGen;

[Generator]
public class ObjectFactorySourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new ObjectFactorySyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not ObjectFactorySyntaxReceiver receiver)
        {
            return;
        }

        foreach (var factoryClass in receiver.FactoryClasses)
        {
            var semanticModel = context.Compilation.GetSemanticModel(factoryClass.SyntaxTree);
            var factorySymbol = semanticModel.GetDeclaredSymbol(factoryClass);
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
            foreach (var constructor in receiver.CommandTypeConstructors)
            {
                var constructorSymbol = semanticModel.GetDeclaredSymbol(constructor);
                var containingTypeSymbol = constructorSymbol.ContainingType;

                foreach (var relayFactoryAttribute in relayFactoryAttributes)
                {
                    var commandTypeBase = relayFactoryAttribute.ConstructorArguments[0].Value;

                    if (commandTypeBase.Equals(containingTypeSymbol.BaseType))
                    {
                        var returnType = containingTypeSymbol.Name;
                        var parameters = constructorSymbol.Parameters.Where(p => !HasFromServicesAttribute(p));

                        generatedCode.AppendLine($"public {returnType} Create{returnType}({string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"))})");
                        generatedCode.AppendLine("{");

                        foreach (var fromServicesParameter in constructorSymbol.Parameters.Where(HasFromServicesAttribute))
                        {
                            generatedCode.AppendLine($"{fromServicesParameter.Type} {fromServicesParameter.Name} = {serviceProviderName}.GetRequiredService<{fromServicesParameter.Type}>();");
                        }

                        //generatedCode.AppendLine($"return new {returnType}({string.Join(", ", constructorSymbol.Parameters.Select(p => p.Name))});");
                        generatedCode.AppendLine($"var result = new {returnType}({string.Join(", ", constructorSymbol.Parameters.Select(p => p.Name))});");


                        var interceptorMethod = interceptorMethodSymbols.FirstOrDefault(m => m.ReturnType.Equals(containingTypeSymbol));
                        //var interceptorMethod = interceptorMethodSymbols.FirstOrDefault(m => SymbolEqualityComparer.Default.Equals((ISymbol)m.ReturnType, (ISymbol)commandTypeBase));
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
            context.AddSource(fileName, SourceText.From(generatedCode.ToString(), Encoding.UTF8));
        }
    }
    private static bool HasFromServicesAttribute(IParameterSymbol parameterSymbol)
    {
        return parameterSymbol.GetAttributes().Any(attr => attr.AttributeClass.Name.StartsWith("FromServices"));
    }
}

public class ObjectFactorySyntaxReceiver : ISyntaxReceiver
{
    public List<ClassDeclarationSyntax> FactoryClasses { get; } = new();
    public List<ConstructorDeclarationSyntax> CommandTypeConstructors { get; } = new();
    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is ClassDeclarationSyntax classDeclaration &&
            classDeclaration.Modifiers.Any(m => m.Text == "partial") &&
            classDeclaration.AttributeLists.Any(a => a.Attributes.Any(attr => attr.Name.ToString().StartsWith("RelayFactoryOf"))))
        {
            FactoryClasses.Add(classDeclaration);
        }

        if (syntaxNode is ConstructorDeclarationSyntax constructorDeclaration &&
            constructorDeclaration.Parent is ClassDeclarationSyntax parentClass &&
            parentClass.BaseList != null)
        {
            CommandTypeConstructors.Add(constructorDeclaration);
        }
    }
}
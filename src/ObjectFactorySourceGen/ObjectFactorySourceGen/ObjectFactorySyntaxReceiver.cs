using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System.Collections.Generic;
using System.Linq;

namespace ObjectFactorySourceGen;

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
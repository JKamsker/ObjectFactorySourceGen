using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectFactorySourceGen;

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


public class Helper
{
    public static bool InheritsFromBaseType(INamedTypeSymbol containingTypeSymbol, IEnumerable<TypeSyntax> baseTypes, SemanticModel semanticModel)
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

    public static bool HasFromServicesAttribute(IParameterSymbol parameterSymbol)
    {
        return parameterSymbol.GetAttributes().Any(attr => attr.AttributeClass.Name.StartsWith("FromServices"));
    }
}
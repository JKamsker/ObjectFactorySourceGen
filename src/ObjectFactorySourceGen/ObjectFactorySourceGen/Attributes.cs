using System;
using System.Collections.Generic;
using System.Text;

namespace ObjectFactorySourceGen;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RelayFactoryOfAttribute : Attribute
{
    public Type Type { get; }

    public RelayFactoryOfAttribute(Type type)
    {
        Type = type;
    }
}

public class FromServicesAttribute : Attribute
{
}
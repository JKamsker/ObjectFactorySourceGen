using Microsoft.Extensions.DependencyInjection;

namespace ObjectFactorySourceGen.ConsoleTest;

internal class Program
{
    private static void Main(string[] args)
    {
        var sc = new ServiceCollection()
           .AddSingleton<MyService>()
           .AddSingleton<MyFactory>()
           .AddTransient(sp => sp.GetService<MyFactory>().WithName("Hallo").CreateCommandType("", "", ""))
           //.AddTransient(x =>
           //{
           //    //ActivatorUtilities.CreateInstance<CommandType>(x, "", "", "");
           //})
           ;

        var sp = sc.BuildServiceProvider();

        var ct = sp.GetService<CommandType>();

        var factory = new MyFactory(sp);
        //factory.

        //var commandType = factory.CreateCommandType("", 1);
    }
}

[RelayFactoryOf(typeof(CommandTypeBase))]
public partial class MyFactory
{
    private readonly IServiceProvider _provider;
    private string _name;

    public MyFactory(IServiceProvider provider)
    {
        _provider = provider;
    }

    public MyFactory WithName(string name)
    {
        _name = name;
        return this;
    }

    private CommandType Intercept(CommandType commandType1)
    {
        commandType1.Name = _name;
        //commandType1.MyProperty = "Hello";
        return commandType1;
    }
}

public class CommandTypeBase
{
}

public class CommandType : CommandTypeBase
{
    public string Name { get; internal set; }

    /// <summary>
    /// This is CommandType2
    /// </summary>
    public CommandType(string myParameter, string myParameter1, string myParameter2, [FromServices] MyService context, [FromServices] MyService context1)
    {
        // do something
    }

    public CommandType(string myParameter, int myParameter1, [FromServices] MyService context)
    {
        // do something
    }
}

public class ShouldNotBeGenerated : NoBase
{
    public ShouldNotBeGenerated(string myParameter, string myParameter1, string myParameter2, [FromServices] MyService context, [FromServices] MyService context1)
    {
        // do something
    }
}


public class NoBase
{

}


public class MyService
{
}
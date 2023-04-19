using Microsoft.Extensions.DependencyInjection;

namespace ObjectFactorySourceGen.ConsoleTest;

internal class Program
{
    private static void Main(string[] args)
    {
        var sc = new ServiceCollection()
           .AddSingleton<MyService>()
           //.AddTransient(sp => sp.GetService<MyFactory>().CreateCommandType("", "", ""))
           //.AddTransient(x =>
           //{
           //    ActivatorUtilities.CreateInstance<CommandType1>(x, "", "", "");
           //})
           ;

        var sp = sc.BuildServiceProvider();

        var factory = new MyFactory(sp);
        //factory.CreateCommandType("", 1);


    }
}

[RelayFactoryOf(typeof(CommandTypeBase))]
public partial class MyFactory
{
    private readonly IServiceProvider _provider;

    public MyFactory(IServiceProvider provider)
    {
        _provider = provider;
    }
}

public class CommandTypeBase
{
}

public class CommandType : CommandTypeBase
{
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

public class MyService
{
}


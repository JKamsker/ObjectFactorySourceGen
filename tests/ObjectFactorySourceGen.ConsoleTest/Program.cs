using Microsoft.Extensions.DependencyInjection;

namespace ObjectFactorySourceGen.ConsoleTest;

internal class Program
{
    private static void Main(string[] args)
    {
        var sc = new ServiceCollection()
           .AddSingleton(new MyService(1))
           .AddSingleton(new MyService(2))
           .AddSingleton<MyService1>()
           .AddSingleton<MyFactory>()
           //.AddTransient(sp => sp.GetService<MyFactory>().WithName("Hallo").CreateCommandType("", "", ""))
           //.AddTransient(x =>
           //{
           //    //ActivatorUtilities.CreateInstance<CommandType>(x, "", "", "");
           //})
           ;

        var sp = sc.BuildServiceProvider();
        var ct4 = sp.GetService<IEnumerable<MyService>>();
        var ct5 = sp.GetService<MyService>();

        ct4.GetEnumerator();


        var ct = sp.GetService<CommandType>();

        var factory = new MyFactory(sp);
        var ctype = factory.CreateCommandType("", "", "");




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
        return commandType1;
    }

    //public CommandType CreateCommandType
    //(
    //    string myParameter,
    //    string myParameter1,
    //    string myParameter2
    //)
    //{
    //    var context0 = _provider.GetRequiredService<ObjectFactorySourceGen.ConsoleTest.MyService1>();
    //    var contexts = _provider.GetRequiredService<IEnumerable<ObjectFactorySourceGen.ConsoleTest.MyService>>();
    //    using var enumeration = contexts.GetEnumerator();
    //    if (!enumeration.MoveNext())
    //    {
    //        throw new InvalidOperationException("No service for type 'ObjectFactorySourceGen.ConsoleTest.MyService' has been registered.");
    //    }

    //    var context = enumeration.Current;
    //    if (!enumeration.MoveNext())
    //    {
    //        enumeration.Reset();
    //    }

    //    var context1 = enumeration.Current;


    //    var result = new CommandType(
    //        myParameter,
    //        myParameter1,
    //        myParameter2,
    //        context0,
    //        context,
    //        context1
    //    );
    //    result = Intercept(result);
    //    return result;
    //}


}

public class ShouldNotBeGenerated : NoBase
{
    public ShouldNotBeGenerated(string myParameter, string myParameter1, string myParameter2, [FromServices] MyService context, [FromServices] MyService context1)
    {
        Context = context;
        Context1 = context1;
        // do something
    }

    public MyService Context { get; }
    public MyService Context1 { get; }
}

public class NoBase
{
}

public class MyService1
{

}
public class MyService
{
    public int Number { get; set; }
    public MyService(int number)
    {
        Number = number;
    }
}


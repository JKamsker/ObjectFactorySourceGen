namespace ObjectFactorySourceGen.ConsoleTest;

public class CommandType : CommandTypeBase
{
    public string Name { get; internal set; }
    public MyService1 Context0 { get; }
    public MyService Context { get; }
    public MyService Context1 { get; }

    /// <summary>
    /// This is CommandType2
    /// </summary>
    public CommandType(string myParameter, string myParameter1, string myParameter2, [FromServices] MyService1 context0, [FromServices] MyService context, [FromServices] MyService context1)
    {
        Context0 = context0;
        Context = context;
        Context1 = context1;
        // do something
    }

    public CommandType(string myParameter, int myParameter1, [FromServices] MyService context)
    {
        // do something
    }
}

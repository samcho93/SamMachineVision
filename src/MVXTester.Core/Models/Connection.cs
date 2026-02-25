namespace MVXTester.Core.Models;

public class Connection : IConnection
{
    public IOutputPort Source { get; }
    public IInputPort Target { get; }

    public Connection(IOutputPort source, IInputPort target)
    {
        Source = source;
        Target = target;
    }

    public static bool CanConnect(IOutputPort source, IInputPort target)
    {
        if (source.Owner == target.Owner)
            return false;

        if (target.IsConnected)
            return false;

        return target.DataType.IsAssignableFrom(source.DataType)
            || source.DataType.IsAssignableFrom(target.DataType);
    }

    public static Connection? TryConnect(IOutputPort source, IInputPort target)
    {
        if (!CanConnect(source, target))
            return null;

        var connection = new Connection(source, target);
        source.Connections.Add(connection);
        target.Connection = connection;
        return connection;
    }

    public static void Disconnect(IConnection connection)
    {
        connection.Source.Connections.Remove(connection);
        connection.Target.Connection = null;
    }
}

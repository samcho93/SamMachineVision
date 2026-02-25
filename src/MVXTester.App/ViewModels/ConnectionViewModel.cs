using CommunityToolkit.Mvvm.ComponentModel;
using MVXTester.Core.Models;

namespace MVXTester.App.ViewModels;

public partial class ConnectionViewModel : ObservableObject
{
    public ConnectorViewModel Source { get; }
    public ConnectorViewModel Target { get; }
    public IConnection Model { get; }

    public ConnectionViewModel(ConnectorViewModel source, ConnectorViewModel target, IConnection model)
    {
        Source = source;
        Target = target;
        Model = model;
    }
}

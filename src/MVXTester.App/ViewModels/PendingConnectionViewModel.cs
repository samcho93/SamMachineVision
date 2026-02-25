using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MVXTester.App.ViewModels;

public partial class PendingConnectionViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;

    [ObservableProperty] private ConnectorViewModel? _source;
    [ObservableProperty] private bool _isVisible;

    public PendingConnectionViewModel(EditorViewModel editor)
    {
        _editor = editor;
    }

    [RelayCommand]
    private void Start(ConnectorViewModel connector)
    {
        Source = connector;
        IsVisible = true;
    }

    [RelayCommand]
    private void Finish(ConnectorViewModel? connector)
    {
        if (Source != null && connector != null && connector != Source)
        {
            _editor.TryConnect(Source, connector);
        }
        Source = null;
        IsVisible = false;
    }

    [RelayCommand]
    private void Cancel()
    {
        Source = null;
        IsVisible = false;
    }
}

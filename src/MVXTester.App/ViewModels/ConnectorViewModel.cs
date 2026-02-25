using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MVXTester.Core.Models;

namespace MVXTester.App.ViewModels;

public partial class ConnectorViewModel : ObservableObject
{
    [ObservableProperty] private Point _anchor;
    [ObservableProperty] private bool _isConnected;

    public string Name { get; }
    public Type DataType { get; }
    public bool IsInput { get; }
    public NodeViewModel Node { get; }

    public IInputPort? InputPort { get; }
    public IOutputPort? OutputPort { get; }

    public ConnectorViewModel(string name, Type dataType, bool isInput, NodeViewModel node,
        IInputPort? inputPort = null, IOutputPort? outputPort = null)
    {
        Name = name;
        DataType = dataType;
        IsInput = isInput;
        Node = node;
        InputPort = inputPort;
        OutputPort = outputPort;
    }

    public string DisplayName => $"{Name} ({GetTypeShortName()})";

    public SolidColorBrush TypeColor => GetTypeBrush();

    private SolidColorBrush GetTypeBrush()
    {
        if (DataType == typeof(OpenCvSharp.Mat))
            return new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
        if (DataType == typeof(int) || DataType == typeof(double)
            || DataType == typeof(float) || DataType == typeof(bool))
            return new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
        if (DataType == typeof(string))
            return new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
        if (DataType == typeof(OpenCvSharp.Point) || DataType == typeof(OpenCvSharp.Size)
            || DataType == typeof(OpenCvSharp.Rect) || DataType == typeof(OpenCvSharp.Rect[]))
            return new SolidColorBrush(Color.FromRgb(0xF5, 0xC2, 0xE7));
        if (DataType == typeof(OpenCvSharp.Point[][]) || DataType == typeof(double[]))
            return new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87));
        if (DataType == typeof(OpenCvSharp.Scalar))
            return new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
        if (DataType == typeof(string[]) || DataType == typeof(string[][]))
            return new SolidColorBrush(Color.FromRgb(0x94, 0xE2, 0xD5));
        return new SolidColorBrush(Color.FromRgb(0x93, 0x99, 0xB2));
    }

    private string GetTypeShortName()
    {
        if (DataType == typeof(OpenCvSharp.Mat)) return "Mat";
        if (DataType == typeof(int)) return "int";
        if (DataType == typeof(double)) return "double";
        if (DataType == typeof(string)) return "string";
        if (DataType == typeof(OpenCvSharp.Point[][])) return "Contours";
        if (DataType == typeof(bool)) return "bool";
        if (DataType == typeof(float)) return "float";
        if (DataType == typeof(OpenCvSharp.Point)) return "Point";
        if (DataType == typeof(OpenCvSharp.Size)) return "Size";
        if (DataType == typeof(OpenCvSharp.Scalar)) return "Scalar";
        if (DataType == typeof(OpenCvSharp.Rect)) return "Rect";
        if (DataType == typeof(OpenCvSharp.Rect[])) return "Rect[]";
        if (DataType == typeof(double[])) return "double[]";
        if (DataType == typeof(string[])) return "string[]";
        if (DataType == typeof(string[][])) return "string[][]";
        return DataType.Name;
    }
}

using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using MVXTester.Core.Models;
using MVXTester.Core.UndoRedo;
using MVXTester.App.UndoRedo;
using MVXTester.App.Views;

namespace MVXTester.App.ViewModels;

public partial class DeviceOptionItem : ObservableObject
{
    private readonly PropertyItem _parent;
    public string Name { get; }
    public int Index { get; }
    [ObservableProperty] private bool _isSelected;

    public DeviceOptionItem(string name, int index, PropertyItem parent, bool isSelected = false)
    {
        Name = name;
        Index = index;
        _parent = parent;
        _isSelected = isSelected;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            _parent.SelectDevice(Index);
    }
}

public partial class PropertyItem : ObservableObject
{
    private readonly NodeProperty _property;
    private readonly Action _onChanged;
    private readonly UndoRedoManager? _undoManager;
    private readonly EditorViewModel? _editor;
    private readonly string? _nodeId;
    private object? _previousValue;
    private bool _suppressUndo;
    private readonly INode? _ownerNode;

    public string Name => _property.DisplayName;
    public string PropertyName => _property.Name;
    public string? Description => _property.Description;
    public PropertyType PropertyType => _property.PropertyType;
    public object? MinValue => _property.MinValue;
    public object? MaxValue => _property.MaxValue;
    public Type? EnumType => _property.EnumType;

    [ObservableProperty] private object? _value;

    /// <summary>
    /// Slider-friendly double property that converts to/from the actual Value (boxed int or double).
    /// WPF Slider.Value is double, but PropertyItem.Value can be boxed int — direct binding breaks positioning.
    /// </summary>
    public double SliderValue
    {
        get
        {
            try { return Convert.ToDouble(Value ?? 0); }
            catch { return 0.0; }
        }
        set
        {
            if (PropertyType == PropertyType.Integer)
                Value = (int)Math.Round(value);
            else
                Value = value;
        }
    }

    [ObservableProperty] private ObservableCollection<DeviceOptionItem> _deviceOptions = new();

    public PropertyItem(NodeProperty property, Action onChanged,
        UndoRedoManager? undoManager = null, EditorViewModel? editor = null, string? nodeId = null,
        INode? ownerNode = null)
    {
        _property = property;
        _onChanged = onChanged;
        _undoManager = undoManager;
        _editor = editor;
        _nodeId = nodeId;
        _ownerNode = ownerNode;
        _value = property.Value;
        _previousValue = property.Value;

        if (property.PropertyType == PropertyType.DeviceList)
        {
            RefreshDeviceOptions();
            property.OptionsChanged += RefreshDeviceOptions;
        }
    }

    private void RefreshDeviceOptions()
    {
        var currentValue = _property.GetValue<int>();
        var items = new ObservableCollection<DeviceOptionItem>();
        foreach (var opt in _property.DeviceOptions)
            items.Add(new DeviceOptionItem(opt.Name, opt.Index, this, opt.Index == currentValue));
        DeviceOptions = items;
    }

    public void SelectDevice(int deviceIndex)
    {
        Value = deviceIndex;
        foreach (var opt in DeviceOptions)
            opt.IsSelected = opt.Index == deviceIndex;
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        if (_ownerNode is MVXTester.Nodes.Input.UsbCameraNode usbNode)
            usbNode.EnumerateDevices();
        else if (_ownerNode is MVXTester.Nodes.Input.HikCameraNode hikNode)
            hikNode.EnumerateDevices();
    }

    partial void OnValueChanged(object? value)
    {
        // Keep slider in sync whenever Value changes (undo/redo, text box, etc.)
        OnPropertyChanged(nameof(SliderValue));

        if (_suppressUndo)
        {
            _property.SetValue(value);
            _onChanged();
            return;
        }

        var oldValue = _previousValue;
        _property.SetValue(value);
        _previousValue = value;

        if (_undoManager != null && _editor != null && _nodeId != null && !_undoManager.IsExecutingUndoRedo)
        {
            var lastAction = _undoManager.PeekUndo() as ChangePropertyAction;
            if (lastAction != null && lastAction.TryMerge(_nodeId, _property.Name, value))
            {
                // Merged
            }
            else
            {
                var action = new ChangePropertyAction(_editor, _nodeId, _property.Name, oldValue, value);
                _undoManager.PushAction(action);
            }
        }

        _onChanged();
    }

    public void SetValueSilently(object? value)
    {
        _suppressUndo = true;
        Value = value;
        _previousValue = value;
        _suppressUndo = false;
    }

    public Array? EnumValues => EnumType != null ? Enum.GetValues(EnumType) : null;

    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif|Video Files|*.mp4;*.avi;*.mov;*.mkv|Cascade Files|*.xml|CSV Files|*.csv|All Files|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            Value = dialog.FileName;
        }
    }
}

public partial class PropertyEditorViewModel : ObservableObject
{
    [ObservableProperty] private NodeViewModel? _selectedNode;
    [ObservableProperty] private ObservableCollection<PropertyItem> _properties = new();
    [ObservableProperty] private string _nodeName = "";
    [ObservableProperty] private string _nodeDescription = "";
    [ObservableProperty] private string _nodeCategory = "";
    [ObservableProperty] private WriteableBitmap? _resultImage;
    [ObservableProperty] private string _resultImageInfo = "";
    [ObservableProperty] private bool _isFunctionNode;

    private Action? _onPropertyChanged;
    private UndoRedoManager? _undoManager;
    private EditorViewModel? _editor;

    public void Initialize(UndoRedoManager undoManager, EditorViewModel editor)
    {
        _undoManager = undoManager;
        _editor = editor;
    }

    public void SetSelectedNode(NodeViewModel? node, Action? onPropertyChanged)
    {
        _onPropertyChanged = onPropertyChanged;
        SelectedNode = node;
        Properties.Clear();
        ResultImage = null;
        ResultImageInfo = "";
        IsFunctionNode = node?.Model is FunctionNode;

        if (node == null)
        {
            NodeName = "";
            NodeDescription = "";
            NodeCategory = "";
            return;
        }

        NodeName = node.Model.Name;
        NodeDescription = node.Model.Description;
        NodeCategory = node.Model.Category;

        foreach (var prop in node.Model.Properties)
        {
            Properties.Add(new PropertyItem(prop, () => _onPropertyChanged?.Invoke(),
                _undoManager, _editor, node.Model.Id, node.Model));
        }

        UpdateResultImage();
    }

    public void UpdateResultImage()
    {
        if (SelectedNode == null)
        {
            ResultImage = null;
            ResultImageInfo = "";
            return;
        }

        try
        {
            using var snapshot = SelectedNode.Model.ClonePreview();
            if (snapshot != null)
            {
                Mat display;
                var maxW = 240.0;
                var maxH = 240.0;
                var scale = Math.Min(maxW / snapshot.Width, maxH / snapshot.Height);
                if (scale < 1.0)
                {
                    display = new Mat();
                    Cv2.Resize(snapshot, display, new OpenCvSharp.Size(0, 0), scale, scale);
                }
                else
                {
                    display = snapshot.Clone();
                }

                if (display.Channels() == 1)
                {
                    var bgr = new Mat();
                    Cv2.CvtColor(display, bgr, ColorConversionCodes.GRAY2BGR);
                    display.Dispose();
                    display = bgr;
                }

                ResultImage = display.ToWriteableBitmap();
                ResultImageInfo = $"{snapshot.Width} x {snapshot.Height}  |  {snapshot.Channels()}ch  |  {snapshot.Type()}";
                display.Dispose();
            }
            else
            {
                ResultImage = null;
                ResultImageInfo = "";
            }
        }
        catch
        {
            ResultImage = null;
            ResultImageInfo = "";
        }
    }

    public void RefreshValues()
    {
        foreach (var prop in Properties)
        {
            var modelProp = SelectedNode?.Model.Properties
                .FirstOrDefault(p => p.DisplayName == prop.Name);
            if (modelProp != null)
                prop.SetValueSilently(modelProp.Value);
        }
    }

    [RelayCommand]
    private void ViewFunctionDetail()
    {
        if (SelectedNode?.Model is not FunctionNode fn) return;
        if (fn.SubGraph == null) return;

        var vm = new FunctionDetailViewModel(fn);
        var dialog = new FunctionDetailDialog(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }
}

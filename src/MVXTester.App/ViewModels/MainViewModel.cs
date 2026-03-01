using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MVXTester.Core.Engine;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using MVXTester.Core.Serialization;
using MVXTester.App.Services;
using MVXTester.App.Views;

namespace MVXTester.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public EditorViewModel Editor { get; }
    public NodePaletteViewModel Palette { get; }
    public PropertyEditorViewModel PropertyEditor { get; }
    public NodeRegistry Registry { get; }

    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int _nodeCount;
    [ObservableProperty] private string _executionTime = "";
    [ObservableProperty] private bool _autoExecute;
    [ObservableProperty] private string _title = "SamMachineVision";
    [ObservableProperty] private bool _isPaletteVisible = true;
    [ObservableProperty] private bool _isPropertiesVisible = true;
    [ObservableProperty] private int _selectedRightTab;
    [ObservableProperty] private string _themeIcon = ThemeManager.IsDarkTheme ? "\u2600" : "\u263D";

    private string? _currentFilePath;
    private string? _currentExtractDir; // ZIP 아카이브 임시 추출 디렉토리
    private DispatcherTimer? _debounceTimer;
    private const int DebounceDelayMs = 150;
    private bool _isDirty;

    public bool IsExecuting => Editor.IsExecuting;
    public bool IsStreaming => Editor.IsStreaming;

    /// <summary>
    /// 그래프가 마지막 저장 이후 변경되었는지 여부
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            UpdateTitle();
        }
    }

    private void MarkDirty()
    {
        IsDirty = true;
    }

    private void UpdateTitle()
    {
        var name = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : "Untitled";
        Title = _isDirty ? $"SamMachineVision - {name} *" : $"SamMachineVision - {name}";
    }

    public MainViewModel()
    {
        Registry = new NodeRegistry();
        var nodesAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "MVXTester.Nodes");
        if (nodesAssembly != null)
            Registry.RegisterAssembly(nodesAssembly);
        else
        {
            try
            {
                var asm = System.Reflection.Assembly.Load("MVXTester.Nodes");
                Registry.RegisterAssembly(asm);
            }
            catch { }
        }

        Editor = new EditorViewModel(Registry);
        Palette = new NodePaletteViewModel(Registry, OnNodeSelectedFromPalette);
        PropertyEditor = new PropertyEditorViewModel();
        PropertyEditor.Initialize(Editor.UndoManager, Editor);

        Editor.NodeSelected += node =>
        {
            PropertyEditor.SetSelectedNode(node, OnPropertyChanged);
        };

        Editor.PropertyEditorRefreshRequested += () =>
        {
            PropertyEditor.RefreshValues();
        };

        Editor.GraphExecuted += () =>
        {
            NodeCount = Editor.Nodes.Count;
            ExecutionTime = $"{Editor.LastExecutionTime.TotalMilliseconds:F1}ms";
            if (Editor.IsStreaming)
            {
                var fps = Editor.LastExecutionTime.TotalMilliseconds > 0
                    ? 1000.0 / Editor.LastExecutionTime.TotalMilliseconds : 0;
                StatusText = $"Streaming ({ExecutionTime} / {fps:F1} FPS)";
            }
            else if (Editor.IsExecuting)
            {
                StatusText = $"Runtime ({ExecutionTime})";
            }
            else
            {
                StatusText = $"Executed ({ExecutionTime})";
            }
            PropertyEditor.UpdateResultImage();
        };

        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Editor.IsExecuting))
                OnPropertyChanged(nameof(IsExecuting));
            if (e.PropertyName == nameof(Editor.IsStreaming))
                OnPropertyChanged(nameof(IsStreaming));
        };

        Editor.ConnectionWarning += (message) =>
        {
            StatusText = $"Warning: {message}";
        };

        Editor.GraphStructureChanged += () =>
        {
            MarkDirty();
            NodeCount = Editor.Nodes.Count;
        };

        Editor.ConnectionChanged += async () =>
        {
            MarkDirty();
            if (Editor.UndoManager.IsExecutingUndoRedo) return;
            await Editor.Execute();
        };

        Editor.NodeDropped += async (nodeVm) =>
        {
            MarkDirty();
            NodeCount = Editor.Nodes.Count;
            if (AutoExecute) await Editor.Execute();
        };

        Editor.NodeDoubleClicked += async (nodeVm) =>
        {
            // FunctionNode 더블클릭 → Detail 팝업
            if (nodeVm.Model is FunctionNode fn && fn.SubGraph != null)
            {
                var detailVm = new FunctionDetailViewModel(fn);
                var detailDialog = new FunctionDetailDialog(detailVm)
                {
                    Owner = Application.Current.MainWindow
                };
                detailDialog.ShowDialog();
                return;
            }

            var filePathProp = nodeVm.Model.Properties
                .FirstOrDefault(p => p.PropertyType == PropertyType.FilePath);

            if (filePathProp != null)
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif|"
                           + "Video Files|*.mp4;*.avi;*.mov;*.mkv|"
                           + "Cascade Files|*.xml|CSV Files|*.csv|All Files|*.*"
                };
                if (dialog.ShowDialog() == true)
                {
                    filePathProp.SetValue(dialog.FileName);
                    PropertyEditor.RefreshValues();
                    await Editor.ExecuteForce();
                }
            }
            else
            {
                await Editor.ExecuteForce();
            }
        };

    }

    private async void OnNodeSelectedFromPalette(NodeRegistryEntry entry)
    {
        var pos = new Point(300 + Random.Shared.Next(-50, 50), 200 + Random.Shared.Next(-50, 50));
        var nodeVm = Editor.AddNode(entry, pos);
        Editor.SelectNode(nodeVm);
        NodeCount = Editor.Nodes.Count;

        if (AutoExecute) await Editor.Execute();
    }

    private void OnPropertyChanged()
    {
        MarkDirty();
        // FunctionNode CustomName 변경 시 노드 헤더 즉시 갱신
        PropertyEditor.SelectedNode?.RefreshTitle();

        _debounceTimer?.Stop();
        _debounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceDelayMs) };
        _debounceTimer.Tick -= OnDebounceTimerTick;
        _debounceTimer.Tick += OnDebounceTimerTick;
        _debounceTimer.Start();
    }

    private async void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        await Editor.Execute();
    }

    [RelayCommand]
    private async Task NewGraph()
    {
        if (!await ConfirmAndStopExecution()) return;
        if (!ConfirmDiscardChanges()) return;

        Editor.Clear();
        ProjectArchive.CleanupExtractDir(_currentExtractDir);
        _currentExtractDir = null;
        _currentFilePath = null;
        _isDirty = false;
        UpdateTitle();
        StatusText = "New graph created";
        NodeCount = 0;
        ExecutionTime = "";
    }

    [RelayCommand]
    private async Task Open()
    {
        if (!await ConfirmAndStopExecution()) return;
        if (!ConfirmDiscardChanges()) return;

        var dialog = new OpenFileDialog
        {
            Filter = "MVXTester Project (*.mvxp)|*.mvxp|MVXTester Graph (*.mvx)|*.mvx|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".mvxp"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                LoadGraph(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (!await ConfirmAndStopExecution()) return;

        if (_currentFilePath != null)
            SaveGraph(_currentFilePath);
        else
            SaveAs_Internal();
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        if (!await ConfirmAndStopExecution()) return;
        SaveAs_Internal();
    }

    private void SaveAs_Internal()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "MVXTester Project (*.mvxp)|*.mvxp|MVXTester Graph (*.mvx)|*.mvx|All Files (*.*)|*.*",
            DefaultExt = ".mvxp"
        };

        if (dialog.ShowDialog() == true)
        {
            SaveGraph(dialog.FileName);
        }
    }

    /// <summary>
    /// 실행 중이면 즉시 정지하고 완료를 대기합니다.
    /// 실행 중이 아니면 즉시 true를 반환합니다.
    /// </summary>
    private async Task<bool> ConfirmAndStopExecution()
    {
        if (!Editor.IsExecuting) return true;

        // MessageBox의 nested message loop이 Dispatcher.BeginInvoke와 교착을 일으키므로
        // 팝업 없이 즉시 정지 후 완료 대기
        await Editor.StopExecutionAsync();
        StatusText = "Execution stopped";
        return true;
    }

    /// <summary>
    /// 미저장 변경사항이 있으면 저장 여부를 묻습니다.
    /// Yes → 저장 후 true, No → 저장하지 않고 true, Cancel → false (작업 취소)
    /// 실행이 이미 정지된 상태에서 호출해야 합니다 (MessageBox 교착 방지).
    /// </summary>
    private bool ConfirmDiscardChanges()
    {
        if (!IsDirty) return true;

        var result = MessageBox.Show(
            "변경사항이 저장되지 않았습니다.\n저장하시겠습니까?",
            "저장 확인",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel) return false;

        if (result == MessageBoxResult.Yes)
        {
            if (_currentFilePath != null)
                SaveGraph(_currentFilePath);
            else
                SaveAs_Internal();
        }

        return true;
    }

    private void SaveGraph(string path)
    {
        try
        {
            if (ProjectArchive.IsProjectArchive(path))
            {
                // ZIP 형식 저장 (.mvxp)
                var graphJson = GraphSerializer.Serialize(Editor.Graph,
                    node => Editor.GetNodePosition(node));
                var fileMap = GraphSerializer.CollectReferencedFiles(Editor.Graph);
                ProjectArchive.Save(path, graphJson, fileMap);
            }
            else
            {
                // 레거시 .mvx 형식 (이전 호환)
                GraphSerializer.SaveToFile(Editor.Graph, path,
                    node => Editor.GetNodePosition(node));
            }

            _currentFilePath = path;
            _isDirty = false;
            UpdateTitle();
            StatusText = "Saved";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadGraph(string path)
    {
        // 이전 추출 디렉토리 정리
        ProjectArchive.CleanupExtractDir(_currentExtractDir);
        _currentExtractDir = null;

        GraphData? data;
        if (ProjectArchive.IsProjectArchive(path))
        {
            // ZIP 형식 로드 (.mvxp)
            var (graphJson, extractDir) = ProjectArchive.Load(path);
            _currentExtractDir = extractDir;
            data = GraphSerializer.Deserialize(graphJson);
        }
        else
        {
            // 레거시 .mvx / .json 형식
            data = GraphSerializer.LoadFromFile(path);
        }

        if (data == null) return;

        Editor.Clear();

        var nodeMap = new Dictionary<string, NodeViewModel>();

        foreach (var nodeData in data.Nodes)
        {
            var nodeType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(t => t.FullName == nodeData.TypeName);

            if (nodeType == null) continue;

            NodeViewModel vm;

            // FunctionNode 특수 처리: Initialize()로 서브그래프 로드 및 포트 생성 필요
            if (nodeType == typeof(MVXTester.Core.Models.FunctionNode)
                && nodeData.Properties.TryGetValue("SourceFilePath", out var srcPathElem))
            {
                var srcPath = srcPathElem.Deserialize<string>();
                if (!string.IsNullOrEmpty(srcPath))
                {
                    // 함수 노드를 레지스트리에도 등록 (팔레트 표시용)
                    var funcName = Path.GetFileNameWithoutExtension(srcPath);
                    Registry.RegisterFunction(funcName, srcPath);

                    var entry = Registry.Entries.FirstOrDefault(e =>
                        e.FunctionFilePath == srcPath && e.NodeType == typeof(MVXTester.Core.Models.FunctionNode));
                    if (entry != null)
                    {
                        vm = Editor.AddNodeInternal(entry, new Point(nodeData.X, nodeData.Y), nodeData.Id);

                        // FunctionNode도 저장된 프로퍼티 복원 (CustomName 등)
                        foreach (var kvp in nodeData.Properties)
                        {
                            var prop = vm.Model.Properties.FirstOrDefault(p => p.Name == kvp.Key);
                            if (prop != null)
                            {
                                try
                                {
                                    var value = kvp.Value.Deserialize(prop.ValueType);
                                    prop.SetValue(value);
                                }
                                catch { }
                            }
                        }

                        nodeMap[nodeData.Id] = vm;
                        continue;
                    }
                }
            }

            vm = Editor.AddNodeInternal(nodeType, new Point(nodeData.X, nodeData.Y), nodeData.Id);

            foreach (var kvp in nodeData.Properties)
            {
                var prop = vm.Model.Properties.FirstOrDefault(p => p.Name == kvp.Key);
                if (prop != null)
                {
                    try
                    {
                        var value = kvp.Value.Deserialize(prop.ValueType);
                        prop.SetValue(value);
                    }
                    catch { }
                }
            }

            nodeMap[nodeData.Id] = vm;
        }

        foreach (var connData in data.Connections)
        {
            if (!nodeMap.TryGetValue(connData.SourceNodeId, out var srcVm)) continue;
            if (!nodeMap.TryGetValue(connData.TargetNodeId, out var tgtVm)) continue;

            var srcConnector = srcVm.OutputConnectors.FirstOrDefault(c => c.Name == connData.SourcePortName);
            var tgtConnector = tgtVm.InputConnectors.FirstOrDefault(c => c.Name == connData.TargetPortName);

            if (srcConnector != null && tgtConnector != null)
                Editor.TryConnect(srcConnector, tgtConnector);
        }

        _currentFilePath = path;
        _isDirty = false;
        UpdateTitle();
        NodeCount = Editor.Nodes.Count;
        StatusText = "Loaded";

        await Editor.ExecuteForce();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ExecuteGraph()
    {
        if (Editor.IsExecuting)
        {
            Editor.CancelExecution();
            StatusText = "Stopped";
        }
        else
        {
            StatusText = "Runtime...";
            await Editor.StartRuntime();
            StatusText = "Stopped";
        }
    }

    [RelayCommand]
    private async Task ForceExecuteGraph()
    {
        StatusText = "Executing (force)...";
        await Editor.ExecuteForce();
    }

    [RelayCommand]
    private async Task StreamGraph()
    {
        if (IsStreaming)
        {
            Editor.CancelExecution();
            StatusText = "Stream stopped";
        }
        else
        {
            StatusText = "Streaming...";
            await Editor.StartStreaming();
            StatusText = "Stream ended";
        }
    }

    [RelayCommand]
    private void CancelExecution()
    {
        Editor.CancelExecution();
        StatusText = "Cancelled";
    }

    [RelayCommand]
    private void TogglePalette() => IsPaletteVisible = !IsPaletteVisible;

    [RelayCommand]
    private void ToggleProperties() => IsPropertiesVisible = !IsPropertiesVisible;

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeManager.ToggleTheme();
        ThemeIcon = ThemeManager.IsDarkTheme ? "\u2600" : "\u263D";
    }

    [RelayCommand]
    private void ShowHelp()
    {
        var helpWindow = new HelpWindow
        {
            Owner = Application.Current.MainWindow
        };
        helpWindow.ShowDialog();
    }

    [RelayCommand]
    private void ImportFunction()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MVXTester Project (*.mvxp)|*.mvxp|MVXTester Graph (*.mvx)|*.mvx|All Files (*.*)|*.*",
            Title = "Import Function"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(dialog.FileName);
                Registry.RegisterFunction(name, dialog.FileName);
                Palette.Refresh();
                StatusText = $"Function '{name}' imported";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import function: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void GeneratePythonCode()
    {
        if (Editor.Graph.Nodes.Count == 0)
        {
            MessageBox.Show("No nodes to generate code from.", "Code Generation",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var code = PythonCodeGenerator.Generate(Editor.Graph);
            var preview = new Views.CodePreviewDialog(code, "Python")
            {
                Owner = Application.Current?.MainWindow
            };
            preview.ShowDialog();
            StatusText = "Python code generated";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Code generation failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void GenerateCSharpCode()
    {
        if (Editor.Graph.Nodes.Count == 0)
        {
            MessageBox.Show("No nodes to generate code from.", "Code Generation",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var code = CSharpCodeGenerator.Generate(Editor.Graph);
            var preview = new Views.CodePreviewDialog(code, "C#")
            {
                Owner = Application.Current?.MainWindow
            };
            preview.ShowDialog();
            StatusText = "C# code generated";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Code generation failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

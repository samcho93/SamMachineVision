using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MVXTester.Core.Registry;

namespace MVXTester.App.ViewModels;

public class NodeCategoryItem : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public ObservableCollection<NodeRegistryEntry> Nodes { get; init; } = new();
    public SolidColorBrush CategoryColor => CategoryColorHelper.GetBrush(Name);

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            if (value)
                Expanded?.Invoke(this);
        }
    }

    /// <summary>Raised when this category is expanded (for accordion behavior).</summary>
    public event Action<NodeCategoryItem>? Expanded;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class NodePaletteViewModel : ObservableObject
{
    private readonly NodeRegistry _registry;
    private readonly Action<NodeRegistryEntry> _onNodeSelected;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObservableCollection<NodeCategoryItem> _categories = new();

    public NodePaletteViewModel(NodeRegistry registry, Action<NodeRegistryEntry> onNodeSelected)
    {
        _registry = registry;
        _onNodeSelected = onNodeSelected;
        RefreshCategories();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshCategories();
    }

    private void RefreshCategories()
    {
        // Unsubscribe old items
        foreach (var cat in Categories)
            cat.Expanded -= OnCategoryExpanded;

        Categories.Clear();
        var entries = string.IsNullOrWhiteSpace(SearchText)
            ? _registry.GetByCategory()
            : _registry.Search(SearchText)
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in entries)
        {
            var item = new NodeCategoryItem
            {
                Name = kvp.Key,
                Nodes = new ObservableCollection<NodeRegistryEntry>(kvp.Value),
                IsExpanded = !string.IsNullOrWhiteSpace(SearchText)
            };
            item.Expanded += OnCategoryExpanded;
            Categories.Add(item);
        }
    }

    /// <summary>Accordion: when one category opens, collapse all others.</summary>
    private void OnCategoryExpanded(NodeCategoryItem expanded)
    {
        foreach (var cat in Categories)
        {
            if (cat != expanded && cat.IsExpanded)
                cat.IsExpanded = false;
        }
    }

    /// <summary>
    /// 외부에서 레지스트리 변경 후 팔레트를 새로고침
    /// </summary>
    public void Refresh() => RefreshCategories();

    [RelayCommand]
    private void AddNode(NodeRegistryEntry entry)
    {
        _onNodeSelected(entry);
    }
}

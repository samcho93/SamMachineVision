using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MVXTester.Core.Registry;

namespace MVXTester.App.ViewModels;

public class NodeCategoryItem
{
    public string Name { get; init; } = "";
    public ObservableCollection<NodeRegistryEntry> Nodes { get; init; } = new();
    public bool IsExpanded { get; set; }
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
        Categories.Clear();
        var entries = string.IsNullOrWhiteSpace(SearchText)
            ? _registry.GetByCategory()
            : _registry.Search(SearchText)
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in entries)
        {
            Categories.Add(new NodeCategoryItem
            {
                Name = kvp.Key,
                Nodes = new ObservableCollection<NodeRegistryEntry>(kvp.Value),
                IsExpanded = !string.IsNullOrWhiteSpace(SearchText)
            });
        }
    }

    [RelayCommand]
    private void AddNode(NodeRegistryEntry entry)
    {
        _onNodeSelected(entry);
    }
}

using System.Reflection;
using MVXTester.Core.Models;

namespace MVXTester.Core.Registry;

public class NodeRegistryEntry
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public Type NodeType { get; init; } = null!;
}

public class NodeRegistry
{
    private readonly List<NodeRegistryEntry> _entries = new();

    public IReadOnlyList<NodeRegistryEntry> Entries => _entries;

    public void RegisterAssembly(Assembly assembly)
    {
        var nodeTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseNode).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<NodeInfoAttribute>() != null);

        foreach (var type in nodeTypes)
        {
            var attr = type.GetCustomAttribute<NodeInfoAttribute>()!;
            _entries.Add(new NodeRegistryEntry
            {
                Name = attr.Name,
                Category = attr.Category,
                Description = attr.Description,
                NodeType = type
            });
        }
    }

    public INode CreateNode(Type nodeType)
    {
        return (INode)Activator.CreateInstance(nodeType)!;
    }

    public INode CreateNode(string name)
    {
        var entry = _entries.FirstOrDefault(e => e.Name == name)
            ?? throw new InvalidOperationException($"Node type '{name}' not found in registry.");
        return CreateNode(entry.NodeType);
    }

    public Dictionary<string, List<NodeRegistryEntry>> GetByCategory()
    {
        return _entries
            .GroupBy(e => e.Category)
            .OrderBy(g => GetCategoryOrder(g.Key))
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Name).ToList());
    }

    public List<NodeRegistryEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _entries.ToList();

        var q = query.ToLowerInvariant();
        return _entries
            .Where(e => e.Name.ToLowerInvariant().Contains(q)
                     || e.Category.ToLowerInvariant().Contains(q)
                     || e.Description.ToLowerInvariant().Contains(q))
            .ToList();
    }

    private static int GetCategoryOrder(string category) => category switch
    {
        NodeCategories.Input => 0,
        NodeCategories.Color => 1,
        NodeCategories.Filter => 2,
        NodeCategories.Edge => 3,
        NodeCategories.Morphology => 4,
        NodeCategories.Threshold => 5,
        NodeCategories.Contour => 6,
        NodeCategories.Feature => 7,
        NodeCategories.Drawing => 8,
        NodeCategories.Transform => 9,
        NodeCategories.Histogram => 10,
        NodeCategories.Arithmetic => 11,
        NodeCategories.Detection => 12,
        NodeCategories.Segmentation => 13,
        NodeCategories.Value => 14,
        NodeCategories.Control => 15,
        NodeCategories.Communication => 16,
        NodeCategories.Data => 17,
        NodeCategories.Event => 18,
        NodeCategories.Script => 19,
        _ => 99
    };
}

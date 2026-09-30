namespace MiniPdm.Core;

public static class Graph
{
    /// <summary>Возвращает именно участников циклов; отклонение родителей выполняет импортёр.</summary>
    public static HashSet<T> CycleNodes<T>(IReadOnlyDictionary<T, IReadOnlyList<T>> edges) where T : notnull
    {
        var colors = new Dictionary<T, int>();
        var path = new List<T>();
        var cycles = new HashSet<T>();
        void Visit(T node)
        {
            colors.TryGetValue(node, out int color);
            if (color == 2) return;
            if (color == 1)
            {
                cycles.UnionWith(path.Skip(path.IndexOf(node)));
                return;
            }
            colors[node] = 1;
            path.Add(node);
            if (edges.TryGetValue(node, out var children))
                foreach (var child in children) Visit(child);
            path.RemoveAt(path.Count - 1);
            colors[node] = 2;
        }
        foreach (var node in edges.Keys) Visit(node);
        return cycles;
    }
}

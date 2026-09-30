namespace MiniPdm.Core;

public sealed class BomService(IPdmStore store)
{
    public IReadOnlyList<SpecificationRow> GetSpecification(long assemblyId) => Calculate(store.GetTree(assemblyId));

    public decimal GetMass(long assemblyId)
    {
        var rows = GetSpecification(assemblyId);
        var missing = rows.Where(r => r.UnitMassKg is null).Select(r => r.Name).ToArray();
        if (missing.Length != 0) throw new DomainException("Не указана масса: " + string.Join(", ", missing));
        return rows.Sum(r => r.TotalMassKg!.Value);
    }

    public static IReadOnlyList<SpecificationRow> Calculate(IReadOnlyList<TreeRow> tree)
    {
        if (tree.Count == 0) throw new DomainException("Объект не найден");
        if (tree[0].Type != ObjectType.Assembly) throw new DomainException("Выберите сборку");
        if (tree.Any(r => r.IsCycle)) throw new DomainException("Обнаружен цикл в составе");
        var unavailable = tree.Where(r => r.VersionId is null).Select(r => r.Name).Distinct().ToArray();
        if (unavailable.Length != 0)
            throw new DomainException("Нет действующей версии: " + string.Join(", ", unavailable));

        // CTE возвращает все вхождения одним запросом. Decimal-арифметика в C# исключает
        // незаметный переход SQLite от больших целых чисел к неточному REAL.
        var quantities = new Dictionary<string, decimal>();
        var result = new Dictionary<long, SpecificationRow>();
        foreach (var row in tree.OrderBy(r => r.Path.Count(c => c == '/')))
        {
            decimal quantity = row.ParentPath == "" ? 1 : checked(quantities[row.ParentPath] * row.Quantity);
            quantities[row.Path] = quantity;
            if (row.Type == ObjectType.Assembly) continue;
            if (result.TryGetValue(row.ObjectId, out var previous))
                result[row.ObjectId] = previous with { Quantity = checked(previous.Quantity + quantity) };
            else result[row.ObjectId] = new(row.ObjectId, row.Designation, row.Name, quantity, row.MassKg);
        }
        return result.Values.OrderBy(r => r.Designation ?? r.Name, StringComparer.Ordinal).ToArray();
    }
}

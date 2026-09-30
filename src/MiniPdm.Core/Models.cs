namespace MiniPdm.Core;

public enum ObjectType { Assembly, Part, StandardPart }
public enum VersionState { Draft, Approved, Cancelled }

public sealed record CadComponent(string File, int Count);
public sealed record CadDocument(string FileName, ObjectType Type, string? Designation,
    string Name, string? Material, decimal? MassKg, IReadOnlyList<CadComponent> Components)
{
    // Идентичность объекта не зависит от имени CAD-файла.
    public string Identity => Type == ObjectType.StandardPart ? "N:" + Name : "D:" + Designation;
}

public sealed record CadSource(string Id, string FileName);
public sealed record BomLink(long ChildObjectId, int Quantity);
public sealed record PdmVersion(long Id, int Number, VersionState State, string Name,
    string? Material, decimal? MassKg, IReadOnlyList<BomLink> Links);
public sealed record PdmObject(long Id, ObjectType Type, string? Designation, string Identity,
    PdmVersion? Current, int LastVersionNumber);

public sealed class ObjectCard
{
    public long Id { get; init; }
    public ObjectType Type { get; init; }
    public string? Designation { get; init; }
    public string Name { get; init; } = "";
    public long? VersionId { get; init; }
    public int? VersionNumber { get; init; }
    public VersionState? State { get; init; }
    public string? Material { get; init; }
    public decimal? MassKg { get; init; }
    public string TypeText => Type switch { ObjectType.Assembly => "Сборка", ObjectType.Part => "Деталь", _ => "Стандартное изделие" };
    public string StateText => State switch { VersionState.Draft => "В работе", VersionState.Approved => "Утверждено", VersionState.Cancelled => "Аннулировано", _ => "Нет действующей версии" };
    public string Caption => string.IsNullOrEmpty(Designation) ? Name : $"{Designation}  {Name}";
}

// Каждая строка — вхождение, а не уникальный объект: общий потомок может встретиться несколько раз.
public sealed class TreeRow
{
    public long ObjectId { get; init; }
    public ObjectType Type { get; init; }
    public string? Designation { get; init; }
    public string Name { get; init; } = "";
    public decimal? MassKg { get; init; }
    public long? VersionId { get; init; }
    public int Quantity { get; init; }
    public string Path { get; init; } = "";
    public string ParentPath { get; init; } = "";
    public bool IsCycle { get; init; }
}

public sealed record SpecificationRow(long ObjectId, string? Designation, string Name,
    decimal Quantity, decimal? UnitMassKg)
{
    public decimal? TotalMassKg => UnitMassKg * Quantity;
}

public enum ImportOutcome { Created, Updated, NewVersion, Unchanged, Rejected }
public sealed record ImportRow(string FileName, ImportOutcome Outcome, string Reason, bool HasWarning = false)
{
    public string Result => Outcome == ImportOutcome.Rejected ? "Ошибка" : HasWarning ? "Предупреждение" : "Принято";
    public string Action => Outcome switch
    {
        ImportOutcome.Created => "Создан", ImportOutcome.Updated => "Обновлён",
        ImportOutcome.NewVersion => "Новая версия", ImportOutcome.Unchanged => "Без изменений", _ => "Отклонён"
    };
}
public sealed record ImportReport(IReadOnlyList<ImportRow> Rows)
{
    public int Accepted => Rows.Count(r => r.Outcome != ImportOutcome.Rejected);
    public int Rejected => Rows.Count(r => r.Outcome == ImportOutcome.Rejected);
    public int Warnings => Rows.Count(r => r.HasWarning);
    public string Summary => $"Принято: {Accepted}; отклонено: {Rejected}; с предупреждением: {Warnings}";
}

public class DomainException(string message) : Exception(message);
public sealed class CadReadException(string message) : Exception(message);

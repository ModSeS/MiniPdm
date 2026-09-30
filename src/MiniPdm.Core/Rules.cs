using System.Text.RegularExpressions;

namespace MiniPdm.Core;

public static partial class Rules
{
    [GeneratedRegex(@"\A[А-ЯЁ]{4}\.[0-9]{6}\.[0-9]{3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DesignationPattern();
    public static bool IsDesignationValid(string? value) => value is not null && DesignationPattern().IsMatch(value);

    public static bool CanTransition(VersionState from, VersionState to) =>
        (from == VersionState.Draft && to is VersionState.Approved or VersionState.Cancelled) ||
        (from == VersionState.Approved && to == VersionState.Cancelled);

    public static IReadOnlyList<string> Validate(CadDocument d)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(d.Type)) errors.Add("Неизвестный тип объекта");
        if (string.IsNullOrWhiteSpace(d.Name)) errors.Add("Не указано наименование");
        if (d.Type != ObjectType.StandardPart && !IsDesignationValid(d.Designation))
            errors.Add("Неверное обозначение: нужны четыре заглавные кириллические буквы, шесть и три цифры");
        if (d.Type == ObjectType.StandardPart && d.Designation is not null)
            errors.Add("У стандартного изделия не должно быть обозначения");
        if (d.Type == ObjectType.Part && string.IsNullOrWhiteSpace(d.Material)) errors.Add("Не указан материал детали");
        if (d.Type != ObjectType.Part && d.Material is not null) errors.Add("Материал допустим только для детали");
        if (d.Type == ObjectType.Assembly && d.MassKg is not null) errors.Add("Масса сборки должна вычисляться");
        if (d.Type == ObjectType.StandardPart && d.MassKg is null) errors.Add("Не указана масса стандартного изделия");
        if (d.MassKg < 0) errors.Add("Масса не может быть отрицательной");
        if (d.Type != ObjectType.Assembly && d.Components.Count != 0) errors.Add("Состав допустим только для сборки");
        if (d.Components.Any(c => c.Count <= 0)) errors.Add("Количество компонента должно быть больше нуля");
        if (d.Components.GroupBy(c => c.File, StringComparer.OrdinalIgnoreCase).Any(g => g.Sum(c => (long)c.Count) > int.MaxValue))
            errors.Add("Суммарное количество одного компонента превышает допустимое целое число");
        if (d.Components.Any(c => string.IsNullOrWhiteSpace(c.File))) errors.Add("Пустая ссылка на компонент");
        return errors;
    }
}

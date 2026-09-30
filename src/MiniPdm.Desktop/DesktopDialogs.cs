using System.IO;
using System.Text;
using Microsoft.Win32;
using MiniPdm.Core;

namespace MiniPdm.Desktop;

public interface IDesktopDialogs
{
    string? SelectFolder();
    void ExportSpecification(IReadOnlyList<SpecificationRow> rows);
}

public sealed class DesktopDialogs : IDesktopDialogs
{
    public string? SelectFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку с CAD-документами" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void ExportSpecification(IReadOnlyList<SpecificationRow> rows)
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Спецификация.csv" };
        if (dialog.ShowDialog() != true) return;
        static string Cell(object? value)
        {
            string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            // Защищаем текстовые ячейки от интерпретации как формул при открытии в Excel.
            if (text.Length > 0 && "=+-@".Contains(text[0])) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var lines = new List<string> { "Обозначение;Наименование;Количество;Масса единицы, кг;Общая масса, кг" };
        lines.AddRange(rows.Select(r => string.Join(";", new object?[] { r.Designation, r.Name, r.Quantity, r.UnitMassKg, r.TotalMassKg }.Select(Cell))));
        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
    }
}

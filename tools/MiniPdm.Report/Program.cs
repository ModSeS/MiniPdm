using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using MiniPdm.Core;
using MiniPdm.Infrastructure;

// Воспроизводимый отчёт по приложенным данным. Работает на отдельной БД в памяти.
Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string samples = args.Length > 0 ? args[0] : "samples";
string output = args.Length > 1 ? args[1] : "docs/import-report.md";
var text = new StringBuilder("# Результаты импорта\n\nСформировано командой `dotnet run --project tools/MiniPdm.Report -- samples docs/import-report.md`.\n\n");
foreach (bool approve in new[] { false, true })
{
    string cs = $"Data Source=report-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    using var keeper = new SqliteConnection(cs);
    keeper.Open();
    var store = new SqlitePdmStore(cs);
    store.Initialize();
    var adapter = new JsonCadAdapter();
    var importer = new ImportService(adapter, adapter, store);
    var bom = new BomService(store);
    text.AppendLine(approve ? "## Сценарий 2: утверждение перед импортом v2\n" : "## Сценарий 1: версии остаются в работе\n");
    var first = await importer.ImportAsync(Path.Combine(samples, "cad-export"));
    WriteReport("Первый импорт", first, details: !approve);
    WriteMass();
    var repeat = await importer.ImportAsync(Path.Combine(samples, "cad-export"));
    text.AppendLine($"Повтор того же набора: {repeat.Rows.Count(r => r.Outcome == ImportOutcome.Unchanged)} без изменений, {repeat.Rejected} отклонено.\n");
    if (approve)
        foreach (var card in store.GetObjects()) store.ChangeState(card.VersionId!.Value, VersionState.Approved);
    var second = await importer.ImportAsync(Path.Combine(samples, "cad-export-v2"));
    WriteReport("Импорт cad-export-v2", second, details: false);
    text.AppendLine("| Изменённый объект | Действие | Версия | Состояние |\n|---|---|---:|---|");
    foreach (var row in second.Rows.Where(r => r.Outcome is ImportOutcome.Updated or ImportOutcome.NewVersion))
    {
        var card = store.GetObjects().Single(o => o.Name == Path.GetFileNameWithoutExtension(row.FileName));
        text.AppendLine($"| {card.Name} | {row.Action} | {card.VersionNumber} | {card.StateText} |");
    }
    text.AppendLine();
    WriteMass();
    long reducerId = store.GetObjects().Single(o => o.Designation == "РДЦЛ.304112.000").Id;
    if (!approve)
    {
        text.AppendLine("### Спецификация редуктора после v2\n\n| Обозначение | Наименование | Количество | Масса 1 шт., кг | Всего, кг |\n|---|---|---:|---:|---:|");
        foreach (var row in bom.GetSpecification(reducerId))
            text.AppendLine($"| {row.Designation} | {row.Name} | {row.Quantity} | {row.UnitMassKg} | {row.TotalMassKg} |");
        text.AppendLine();
        var oil = store.GetObjects().Single(o => o.Designation == "РДЦЛ.304112.900");
        try { bom.GetMass(oil.Id); }
        catch (DomainException ex) { text.AppendLine("Маслоуказатель: **" + ex.Message + "**. В спецификации строка прокладки имеет пустую массу.\n"); }
    }

    void WriteMass()
    {
        var root = store.GetObjects().Single(o => o.Designation == "РДЦЛ.304112.000");
        text.AppendLine($"Масса редуктора: **{bom.GetMass(root.Id)} кг**.\n");
    }
    void WriteReport(string heading, ImportReport report, bool details)
    {
        text.AppendLine($"### {heading}\n\n{report.Summary}. Предупреждения входят в число принятых.\n");
        if (!details) return;
        text.AppendLine("| Файл | Результат | Действие | Причина |\n|---|---|---|---|");
        foreach (var row in report.Rows)
            text.AppendLine($"| {row.FileName} | {row.Result} | {row.Action} | {row.Reason.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ")} |");
        text.AppendLine();
    }
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
await File.WriteAllTextAsync(output, text.ToString(), new UTF8Encoding(false));
Console.WriteLine(text);

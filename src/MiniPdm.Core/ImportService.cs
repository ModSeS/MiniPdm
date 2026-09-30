namespace MiniPdm.Core;

public sealed class ImportService(ICadCatalog catalog, ICadDocumentReader reader, IPdmStore store)
{
    public async Task<ImportReport> ImportAsync(string location, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var sources = await catalog.ListAsync(location, ct);
        var documents = new Dictionary<string, CadDocument>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Reject(string file, string reason)
        {
            if (!errors.TryGetValue(file, out var reasons)) errors[file] = reasons = [];
            if (!reasons.Contains(reason)) reasons.Add(reason);
        }

        // Читаем всё до записи: так можно отклонить ВСЕ дубликаты, независимо от порядка файлов.
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("Чтение: " + source.FileName);
            try
            {
                var document = await reader.ReadAsync(source.Id, ct);
                documents[source.FileName] = document;
                foreach (var error in Rules.Validate(document)) Reject(source.FileName, error);
            }
            catch (CadReadException ex) { Reject(source.FileName, ex.Message); }
        }

        foreach (var group in documents.GroupBy(p => p.Value.Identity).Where(g => g.Count() > 1))
            foreach (var item in group) Reject(item.Key, "Повторяющийся идентификатор объекта: " + group.Key[2..]);

        foreach (var (file, document) in documents)
            foreach (var component in document.Components)
                if (!documents.ContainsKey(component.File) && !errors.ContainsKey(component.File))
                    Reject(file, $"Ссылка на отсутствующий файл «{component.File}»");

        var edges = documents.ToDictionary(p => p.Key.ToUpperInvariant(),
            p => (IReadOnlyList<string>)p.Value.Components.Select(c => c.File.ToUpperInvariant()).ToArray());
        var cycleNodes = Graph.CycleNodes(edges);
        foreach (var file in documents.Keys.Where(f => cycleNodes.Contains(f.ToUpperInvariant())))
            Reject(file, "Циклическая ссылка в составе");

        // Одна транзакция для всех принятых объектов. Любой сбой БД или отмена откатывают её целиком.
        using var session = store.BeginImport();
        var existing = session.LoadObjects().ToDictionary(o => o.Identity);
        foreach (var (file, document) in documents)
            if (existing.TryGetValue(document.Identity, out var old) && old.Type != document.Type)
                Reject(file, "Нельзя менять тип существующего объекта");

        PropagateRejections();

        // Проверяем также итоговый граф БД: новый файл может замкнуть цепочку старых сборок.
        // Отклоняем импортируемые участники такого цикла и повторяем после возврата старых связей.
        while (true)
        {
            var graph = existing.Values.ToDictionary(o => o.Identity,
                o => (IReadOnlyList<string>)(o.Current?.Links.Select(l => existing.Values.Single(x => x.Id == l.ChildObjectId).Identity).ToArray() ?? []));
            foreach (var (file, d) in documents.Where(p => !errors.ContainsKey(p.Key)))
                graph[d.Identity] = d.Components.Select(c => documents[c.File].Identity).ToArray();
            var cycles = Graph.CycleNodes(graph);
            var affected = documents.Where(p => !errors.ContainsKey(p.Key) && cycles.Contains(p.Value.Identity)).ToArray();
            if (affected.Length == 0) break;
            foreach (var item in affected) Reject(item.Key, "Изменение создаёт цикл с объектами базы данных");
            PropagateRejections();
        }

        var accepted = documents.Where(p => !errors.ContainsKey(p.Key)).ToArray();
        var ids = existing.ToDictionary(p => p.Key, p => p.Value.Id);
        foreach (var (_, document) in accepted)
        {
            ct.ThrowIfCancellationRequested();
            if (!ids.ContainsKey(document.Identity)) ids[document.Identity] = session.CreateObject(document);
        }

        var rows = new List<ImportRow>();
        foreach (var (file, document) in accepted)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("Сохранение: " + file);
            // Повторные вхождения одного компонента в документе складываются в одну связь.
            var links = document.Components.GroupBy(c => ids[documents[c.File].Identity])
                .Select(g => new BomLink(g.Key, checked(g.Sum(c => c.Count)))).ToArray();
            var outcome = session.Save(ids[document.Identity], document, links);
            bool warning = document.Type == ObjectType.Part && document.MassKg is null;
            rows.Add(new(file, outcome, warning ? "Не указана масса детали" : "", warning));
        }
        session.EnsureAcyclic();
        ct.ThrowIfCancellationRequested();
        session.Commit();
        rows.AddRange(errors.Select(p => new ImportRow(p.Key, ImportOutcome.Rejected, string.Join("; ", p.Value))));
        return new(rows.OrderBy(r => r.FileName, StringComparer.Ordinal).ToArray());

        void PropagateRejections()
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var (file, document) in documents.Where(p => !errors.ContainsKey(p.Key)))
                {
                    var rejected = document.Components.FirstOrDefault(c => errors.ContainsKey(c.File));
                    if (rejected is null) continue;
                    Reject(file, $"Компонент «{rejected.File}» отклонён");
                    changed = true;
                }
            } while (changed);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using MiniPdm.Core;

namespace MiniPdm.Infrastructure;

public sealed class JsonCadAdapter : ICadCatalog, ICadDocumentReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter<ObjectType>(allowIntegerValues: false) }
    };

    public Task<IReadOnlyList<CadSource>> ListAsync(string location, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<CadSource> sources = Directory.EnumerateFiles(location)
            .Where(p => Path.GetExtension(p).Equals(".a3d", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(p).Equals(".m3d", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new CadSource(p, Path.GetFileName(p))).ToArray();
        return Task.FromResult(sources);
    }

    public async Task<CadDocument> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            // StreamReader поддерживает UTF-8 как с BOM, так и без него.
            using var stream = new StreamReader(path);
            var json = await stream.ReadToEndAsync(ct);
            var dto = JsonSerializer.Deserialize<DocumentDto>(json, Options)
                ?? throw new CadReadException("Пустой документ");
            if (dto.FormatVersion != 1) throw new CadReadException("Неподдерживаемая версия формата");
            if (dto.Type is null || dto.Properties is null || dto.Components is null ||
                dto.Name is null || dto.Components.Any(c => c is null || c.File is null))
                throw new CadReadException("Отсутствуют обязательные поля документа");
            string fileName = Path.GetFileName(path);
            if (!string.Equals(dto.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                throw new CadReadException("Поле fileName не совпадает с именем файла");
            string expected = dto.Type == ObjectType.Assembly ? ".a3d" : ".m3d";
            if (!Path.GetExtension(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new CadReadException("Расширение файла не соответствует типу объекта");
            if (dto.Components.Any(c => Path.GetFileName(c!.File) != c.File || c.File!.Contains('/') || c.File.Contains('\\')))
                throw new CadReadException("Компоненты должны ссылаться на имена файлов в той же папке");
            return new(fileName, dto.Type.Value, dto.Designation, dto.Name,
                dto.Properties.Material, dto.Properties.Mass,
                dto.Components.Select(c => new CadComponent(c!.File!, c.Count)).ToArray());
        }
        catch (JsonException ex) { throw new CadReadException($"Повреждённый JSON, строка {ex.LineNumber + 1}: {ex.Message}"); }
        catch (IOException ex) { throw new CadReadException("Ошибка чтения: " + ex.Message); }
        catch (UnauthorizedAccessException ex) { throw new CadReadException("Нет доступа: " + ex.Message); }
    }

    private sealed class DocumentDto
    {
        public int FormatVersion { get; set; }
        public string? FileName { get; set; }
        public ObjectType? Type { get; set; }
        public string? Designation { get; set; }
        public string? Name { get; set; }
        public PropertiesDto? Properties { get; set; }
        public List<ComponentDto?>? Components { get; set; }
    }
    private sealed class PropertiesDto
    {
        public string? Material { get; set; }
        public decimal? Mass { get; set; }
    }
    private sealed class ComponentDto
    {
        public string? File { get; set; }
        public int Count { get; set; }
    }
}

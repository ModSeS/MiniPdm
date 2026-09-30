using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using MiniPdm.Core;

namespace MiniPdm.Infrastructure;

public sealed class SqlitePdmStore(string connectionString) : IPdmStore
{
    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return connection;
    }

    public void Initialize()
    {
        using var connection = Open();
        connection.Execute(ReadSql("Schema.sql"));
    }

    internal static string ReadSql(string file)
    {
        using var stream = typeof(SqlitePdmStore).Assembly.GetManifestResourceStream("MiniPdm.Infrastructure.Sql." + file)
            ?? throw new InvalidOperationException("Не найден SQL-ресурс: " + file);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public IImportSession BeginImport() => new SqliteImportSession(Open());

    public IReadOnlyList<ObjectCard> GetObjects(string search = "")
    {
        using var connection = Open();
        var objects = connection.Query<ObjectCard>("""
            SELECT o.id Id, o.object_type Type, o.designation Designation,
                   COALESCE(v.name, (SELECT name FROM object_version WHERE object_id=o.id ORDER BY version_no DESC LIMIT 1), o.identity) Name,
                   v.id VersionId, v.version_no VersionNumber, v.state State, v.material Material, v.mass_kg MassKg
            FROM pdm_object o LEFT JOIN current_version v ON v.object_id = o.id
            """);
        // SQLite NOCASE не умеет кириллицу; для небольшого локального каталога фильтруем в .NET.
        return objects.Where(o => o.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            (o.Designation?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(o => o.Type).ThenBy(o => o.Caption, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<TreeRow> GetTree(long rootId)
    {
        using var connection = Open();
        return connection.Query<TreeRow>(ReadSql("Tree.sql"), new { rootId }).ToArray();
    }

    public void ChangeState(long versionId, VersionState target)
    {
        using var session = new SqliteImportSession(Open());
        session.ChangeState(versionId, target);
        // Аннулирование может открыть более старую версию с другим составом.
        session.EnsureAcyclic();
        session.Commit();
    }

    internal static string? MassText(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
}

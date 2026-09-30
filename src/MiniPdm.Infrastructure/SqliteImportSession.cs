using Dapper;
using Microsoft.Data.Sqlite;
using MiniPdm.Core;

namespace MiniPdm.Infrastructure;

internal sealed class SqliteImportSession : IImportSession
{
    private readonly SqliteConnection connection;
    private readonly SqliteTransaction transaction;

    public SqliteImportSession(SqliteConnection connection)
    {
        this.connection = connection;
        // Немедленная write-транзакция сериализует импорт и смену состояний.
        transaction = connection.BeginTransaction(deferred: false);
    }

    public IReadOnlyList<PdmObject> LoadObjects()
    {
        var objects = connection.Query<ObjectRecord>("""
            SELECT o.id Id, o.object_type Type, o.designation Designation, o.identity Identity,
                   COALESCE((SELECT MAX(version_no) FROM object_version WHERE object_id=o.id),0) LastVersionNumber
            FROM pdm_object o
            """, transaction: transaction).ToArray();
        var versions = connection.Query<VersionRecord>("""
            SELECT id Id, object_id ObjectId, version_no Number, state State, name Name, material Material, mass_kg MassKg
            FROM current_version
            """, transaction: transaction).ToDictionary(v => v.ObjectId);
        var links = connection.Query<LinkRecord>("""
            SELECT b.parent_version_id ParentVersionId, b.child_object_id ChildObjectId, b.quantity Quantity
            FROM bom_link b JOIN current_version v ON v.id=b.parent_version_id
            """, transaction: transaction).ToLookup(l => l.ParentVersionId);
        return objects.Select(o => new PdmObject(o.Id, o.Type, o.Designation, o.Identity,
            versions.TryGetValue(o.Id, out var v)
                ? new(v.Id, v.Number, v.State, v.Name, v.Material, v.MassKg,
                    links[v.Id].Select(l => new BomLink(l.ChildObjectId, l.Quantity)).ToArray())
                : null, o.LastVersionNumber)).ToArray();
    }

    public long CreateObject(CadDocument d) => connection.ExecuteScalar<long>("""
        INSERT INTO pdm_object(object_type, identity, designation) VALUES (@Type, @Identity, @Designation);
        SELECT last_insert_rowid();
        """, new { d.Type, d.Identity, d.Designation }, transaction);

    public ImportOutcome Save(long objectId, CadDocument document, IReadOnlyList<BomLink> links)
    {
        var old = connection.QuerySingleOrDefault<VersionRecord>("""
            SELECT id Id, object_id ObjectId, version_no Number, state State, name Name, material Material, mass_kg MassKg
            FROM current_version WHERE object_id=@objectId
            """, new { objectId }, transaction);
        var oldLinks = old is null ? [] : connection.Query<LinkRecord>("""
            SELECT child_object_id ChildObjectId, quantity Quantity FROM bom_link WHERE parent_version_id=@Id
            """, new { old.Id }, transaction).Select(l => new BomLink(l.ChildObjectId, l.Quantity)).ToArray();
        if (old is not null && old.Name == document.Name && old.Material == document.Material &&
            old.MassKg == document.MassKg && oldLinks.OrderBy(l => l.ChildObjectId).SequenceEqual(links.OrderBy(l => l.ChildObjectId)))
            return ImportOutcome.Unchanged;

        long versionId;
        ImportOutcome outcome;
        var values = new { objectId, document.Name, document.Material, Mass = SqlitePdmStore.MassText(document.MassKg) };
        if (old?.State == VersionState.Draft)
        {
            versionId = old.Id;
            connection.Execute("""
                UPDATE object_version SET name=@Name, material=@Material, mass_kg=@Mass
                WHERE object_id=@objectId AND id=(SELECT id FROM current_version WHERE object_id=@objectId)
                """, values, transaction);
            connection.Execute("DELETE FROM bom_link WHERE parent_version_id=@versionId", new { versionId }, transaction);
            outcome = ImportOutcome.Updated;
        }
        else
        {
            int previousNumber = connection.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(version_no),0) FROM object_version WHERE object_id=@objectId", new { objectId }, transaction);
            versionId = connection.ExecuteScalar<long>("""
                INSERT INTO object_version(object_id, version_no, state, name, material, mass_kg)
                VALUES (@objectId, @Number, 0, @Name, @Material, @Mass);
                SELECT last_insert_rowid();
                """, new { objectId, Number = previousNumber + 1, document.Name, document.Material, values.Mass }, transaction);
            outcome = previousNumber == 0 ? ImportOutcome.Created : ImportOutcome.NewVersion;
        }

        foreach (var link in links)
            connection.Execute("""
                INSERT INTO bom_link(parent_version_id, child_object_id, quantity) VALUES (@versionId,@ChildObjectId,@Quantity)
                """, new { versionId, link.ChildObjectId, link.Quantity }, transaction);
        return outcome;
    }

    public void ChangeState(long versionId, VersionState target)
    {
        var state = connection.QuerySingleOrDefault<int?>("SELECT state FROM object_version WHERE id=@versionId", new { versionId }, transaction);
        if (state is null) throw new DomainException("Версия не найдена");
        if (!Rules.CanTransition((VersionState)state, target)) throw new DomainException("Недопустимый переход состояния");
        connection.Execute("UPDATE object_version SET state=@target WHERE id=@versionId", new { target, versionId }, transaction);
    }

    public void EnsureAcyclic()
    {
        var objects = LoadObjects();
        var edges = objects.ToDictionary(o => o.Id, o => (IReadOnlyList<long>)(o.Current?.Links.Select(l => l.ChildObjectId).ToArray() ?? []));
        if (Graph.CycleNodes(edges).Count != 0) throw new DomainException("Операция создаёт цикл в действующем составе; изменения отменены");
    }

    public void Commit() => transaction.Commit();
    public void Dispose()
    {
        // Dispose без Commit откатывает транзакцию, включая уже созданные объекты.
        transaction.Dispose();
        connection.Dispose();
    }

    private sealed class ObjectRecord
    {
        public long Id { get; set; }
        public ObjectType Type { get; set; }
        public string? Designation { get; set; }
        public string Identity { get; set; } = "";
        public int LastVersionNumber { get; set; }
    }
    private sealed class VersionRecord
    {
        public long Id { get; set; }
        public long ObjectId { get; set; }
        public int Number { get; set; }
        public VersionState State { get; set; }
        public string Name { get; set; } = "";
        public string? Material { get; set; }
        public decimal? MassKg { get; set; }
    }
    private sealed class LinkRecord
    {
        public long ParentVersionId { get; set; }
        public long ChildObjectId { get; set; }
        public int Quantity { get; set; }
    }
}

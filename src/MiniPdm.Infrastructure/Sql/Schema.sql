CREATE TABLE IF NOT EXISTS pdm_object (
    id INTEGER PRIMARY KEY,
    object_type INTEGER NOT NULL CHECK (object_type IN (0,1,2)),
    identity TEXT NOT NULL UNIQUE,
    designation TEXT UNIQUE,
    CHECK ((object_type = 2 AND designation IS NULL) OR (object_type <> 2 AND designation IS NOT NULL))
);
CREATE TABLE IF NOT EXISTS object_version (
    id INTEGER PRIMARY KEY,
    object_id INTEGER NOT NULL REFERENCES pdm_object(id),
    version_no INTEGER NOT NULL CHECK (version_no > 0),
    state INTEGER NOT NULL CHECK (state IN (0,1,2)),
    name TEXT NOT NULL CHECK (length(trim(name)) > 0),
    material TEXT,
    -- Текст хранит decimal точно, без округления до двоичного REAL.
    mass_kg TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UNIQUE (object_id, version_no)
);
CREATE TABLE IF NOT EXISTS bom_link (
    parent_version_id INTEGER NOT NULL REFERENCES object_version(id),
    child_object_id INTEGER NOT NULL REFERENCES pdm_object(id),
    quantity INTEGER NOT NULL CHECK (quantity > 0 AND quantity <= 2147483647),
    PRIMARY KEY (parent_version_id, child_object_id)
);
CREATE INDEX IF NOT EXISTS ix_bom_child ON bom_link(child_object_id);
CREATE INDEX IF NOT EXISTS ix_version_current ON object_version(object_id, state, version_no);

-- Текущая версия вычисляется в представлении: указатель не может устареть при аннулировании.
CREATE VIEW IF NOT EXISTS current_version AS
SELECT v.* FROM object_version v
WHERE v.state <> 2 AND v.version_no =
    (SELECT MAX(v2.version_no) FROM object_version v2 WHERE v2.object_id = v.object_id AND v2.state <> 2);

CREATE TRIGGER IF NOT EXISTS valid_state_transition BEFORE UPDATE OF state ON object_version
WHEN NOT ((OLD.state = 0 AND NEW.state IN (1,2)) OR (OLD.state = 1 AND NEW.state = 2))
BEGIN SELECT RAISE(ABORT, 'Invalid state transition'); END;
CREATE TRIGGER IF NOT EXISTS immutable_version BEFORE UPDATE OF name, material, mass_kg, object_id, version_no ON object_version
WHEN OLD.state <> 0
BEGIN SELECT RAISE(ABORT, 'Version is immutable'); END;
CREATE TRIGGER IF NOT EXISTS immutable_link_insert BEFORE INSERT ON bom_link
WHEN (SELECT state FROM object_version WHERE id = NEW.parent_version_id) <> 0
BEGIN SELECT RAISE(ABORT, 'Version is immutable'); END;
CREATE TRIGGER IF NOT EXISTS immutable_link_delete BEFORE DELETE ON bom_link
WHEN (SELECT state FROM object_version WHERE id = OLD.parent_version_id) <> 0
BEGIN SELECT RAISE(ABORT, 'Version is immutable'); END;
CREATE TRIGGER IF NOT EXISTS immutable_link_update BEFORE UPDATE ON bom_link
WHEN (SELECT state FROM object_version WHERE id = OLD.parent_version_id) <> 0
  OR (SELECT state FROM object_version WHERE id = NEW.parent_version_id) <> 0
BEGIN SELECT RAISE(ABORT, 'Version is immutable'); END;

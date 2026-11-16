namespace Qsearch.Index.Sqlite;

internal static class IndexSchema
{
    public const int Version = 2;

    public const string CreateVersionOne = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS metadata (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS scopes (
            id TEXT PRIMARY KEY,
            root_path TEXT NOT NULL,
            freshness INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS items (
            row_id INTEGER PRIMARY KEY AUTOINCREMENT,
            id TEXT NOT NULL UNIQUE,
            scope_id TEXT NOT NULL,
            parent_id TEXT NULL,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            full_path TEXT NOT NULL,
            extension TEXT NOT NULL,
            item_type INTEGER NOT NULL,
            size INTEGER NOT NULL,
            last_write_utc TEXT NOT NULL,
            attributes INTEGER NOT NULL,
            path_depth INTEGER NOT NULL,
            FOREIGN KEY(scope_id) REFERENCES scopes(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_items_scope ON items(scope_id);
        CREATE INDEX IF NOT EXISTS ix_items_extension ON items(extension);
        CREATE INDEX IF NOT EXISTS ix_items_type ON items(item_type);
        CREATE INDEX IF NOT EXISTS ix_items_normalized_name ON items(normalized_name);

        CREATE VIRTUAL TABLE IF NOT EXISTS items_fts USING fts5(
            normalized_name,
            content = 'items',
            content_rowid = 'row_id',
            tokenize = 'trigram case_sensitive 1'
        );

        CREATE TRIGGER IF NOT EXISTS items_after_insert AFTER INSERT ON items BEGIN
            INSERT INTO items_fts(rowid, normalized_name)
            VALUES (new.row_id, new.normalized_name);
        END;

        CREATE TRIGGER IF NOT EXISTS items_after_delete AFTER DELETE ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, normalized_name)
            VALUES ('delete', old.row_id, old.normalized_name);
        END;

        CREATE TRIGGER IF NOT EXISTS items_after_update AFTER UPDATE ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, normalized_name)
            VALUES ('delete', old.row_id, old.normalized_name);
            INSERT INTO items_fts(rowid, normalized_name)
            VALUES (new.row_id, new.normalized_name);
        END;

        CREATE TABLE IF NOT EXISTS checkpoints (
            scope_id TEXT PRIMARY KEY,
            journal_id INTEGER NOT NULL,
            next_usn INTEGER NOT NULL,
            recorded_at_utc TEXT NOT NULL,
            FOREIGN KEY(scope_id) REFERENCES scopes(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS review_cache (
            item_id TEXT PRIMARY KEY,
            state TEXT NOT NULL,
            is_complete INTEGER NOT NULL,
            size INTEGER NOT NULL,
            last_write_utc TEXT NOT NULL,
            policy_version INTEGER NOT NULL,
            FOREIGN KEY(item_id) REFERENCES items(id) ON DELETE CASCADE
        );

        INSERT OR IGNORE INTO metadata(key, value) VALUES('schema_version', '1');
        INSERT OR IGNORE INTO metadata(key, value) VALUES('normalization_version', '1');
        PRAGMA user_version = 1;
        """;

    public const string CreateVersionTwo = """
        CREATE INDEX IF NOT EXISTS ix_items_name_sort ON items(normalized_name, id);
        CREATE INDEX IF NOT EXISTS ix_items_path_sort ON items(full_path COLLATE NOCASE, id);
        CREATE INDEX IF NOT EXISTS ix_items_type_sort ON items(item_type, id);
        CREATE INDEX IF NOT EXISTS ix_items_size_sort ON items(size, id);
        CREATE INDEX IF NOT EXISTS ix_items_modified_sort ON items(last_write_utc, id);

        UPDATE metadata SET value = '2' WHERE key = 'schema_version';
        PRAGMA user_version = 2;
        """;
}

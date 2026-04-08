//database.cs

using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace jpn_lang_dbm_desktop_app_standalone;

public sealed class Database : IDisposable
{
    private readonly SqliteConnection _conn;

    public Database()
    {
        var dbPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "jpn_lang_dbm.sqlite3"      //Here's the filename, btw -- in case we want to change it.
        );

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();

        using var pragma = _conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        EnsureSchema();
        EnsureDefaultSettings();
    }

    public SqliteConnection Connection => _conn;

    private void EnsureSchema()
    {
        using var tx = _conn.BeginTransaction();

        Exec(tx, @"CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT NULL
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS text_entries (
            id INTEGER PRIMARY KEY,
            input_hash TEXT NOT NULL UNIQUE,
            input_text TEXT NOT NULL
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS lindera_results_cache (
            id INTEGER PRIMARY KEY,
            created_utc TEXT NOT NULL,
            last_used_utc TEXT NOT NULL,
            text_entry_id INTEGER NOT NULL
                REFERENCES text_entries(id)
                ON DELETE CASCADE,
            dict TEXT NOT NULL,
            result_json TEXT NOT NULL,
            result_json_hash TEXT NOT NULL,
            parse_context_json TEXT NOT NULL,
            parse_context_hash TEXT NOT NULL
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS tags (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL UNIQUE
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS text_entry_tags (
            text_entry_id INTEGER NOT NULL
                REFERENCES text_entries(id)
                ON DELETE CASCADE,
            tag_id INTEGER NOT NULL
                REFERENCES tags(id)
                ON DELETE CASCADE,
            PRIMARY KEY (text_entry_id, tag_id)
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS templates (
            id INTEGER PRIMARY KEY,
            template_name TEXT NOT NULL UNIQUE
        );");

        Exec(tx, @"
            INSERT OR IGNORE INTO templates (id, template_name)
            VALUES (0, 'Blank');
        ");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS template_keys (
            id INTEGER PRIMARY KEY,
            template_id INTEGER NOT NULL
                REFERENCES templates(id)
                ON DELETE CASCADE,
            key TEXT NOT NULL,
            display_order INTEGER NOT NULL,
            UNIQUE (template_id, key),
            UNIQUE (template_id, display_order)
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS source_data_block_stash (
            id INTEGER PRIMARY KEY,
            starting_template_id INTEGER NULL
                REFERENCES templates(id)
                ON DELETE SET NULL,
            created_utc TEXT NOT NULL,
            last_edited_utc TEXT NOT NULL
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS source_data_block_stash_rows (
            id INTEGER PRIMARY KEY,
            source_data_block_id INTEGER NOT NULL
                REFERENCES source_data_block_stash(id)
                ON DELETE CASCADE,
            key TEXT NOT NULL,
            value TEXT NULL,
            display_order INTEGER NOT NULL
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS source_data_block (
            id INTEGER PRIMARY KEY,
            starting_template_id INTEGER NULL
                REFERENCES templates(id)
                ON DELETE SET NULL,
            created_utc TEXT NOT NULL,
            last_edited_utc TEXT NOT NULL
        );");

        Exec(tx, @"
            INSERT OR IGNORE INTO source_data_block
                (id, starting_template_id, created_utc, last_edited_utc)
            VALUES
                (0, NULL, '1970-01-01T00:00:00Z', '1970-01-01T00:00:00Z');
        ");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS source_data_block_rows (
            id INTEGER PRIMARY KEY,
            source_data_block_id INTEGER NOT NULL
                REFERENCES source_data_block(id)
                ON DELETE CASCADE,
            key TEXT NOT NULL,
            value TEXT NULL,
            display_order INTEGER NOT NULL
        );");
        
        Exec(tx, @"CREATE TABLE IF NOT EXISTS timestamped_input (
            id INTEGER PRIMARY KEY,
            created_utc TEXT NOT NULL,
            text_entry_id INTEGER NOT NULL
                REFERENCES text_entries(id)
                ON DELETE CASCADE,
            word_count_json TEXT NOT NULL,
            source_metadata_applied INTEGER NOT NULL DEFAULT 0,
            source_metadata_applied_utc TEXT NULL
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS timestamped_input_with_source_data (
            id INTEGER PRIMARY KEY,
            timestamped_input_id INTEGER NOT NULL
                REFERENCES timestamped_input(id)
                ON DELETE CASCADE,
            source_data_id INTEGER NOT NULL
                REFERENCES source_data_block(id),
            created_utc TEXT NOT NULL,
            UNIQUE (timestamped_input_id, source_data_id, created_utc)
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS timestamped_input_with_source_data_tags (
            id INTEGER PRIMARY KEY,
            ti_sd_id INTEGER NOT NULL,
            tag_id INTEGER NOT NULL,

            FOREIGN KEY (ti_sd_id)
                REFERENCES timestamped_input_with_source_data(id)
                ON DELETE CASCADE,

            FOREIGN KEY (tag_id)
                REFERENCES tags(id)
                ON DELETE CASCADE,

            UNIQUE (ti_sd_id, tag_id)
        );");

        Exec(tx, @"CREATE INDEX IF NOT EXISTS idx_ti_sd_tags_tag_id
        ON timestamped_input_with_source_data_tags(tag_id);");

        Exec(tx, @"CREATE INDEX IF NOT EXISTS idx_ti_sd_tags_ti_sd_id
        ON timestamped_input_with_source_data_tags(ti_sd_id);");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS timestamped_input_sidebar_custom_sorting (
            timestamped_input_id INTEGER NOT NULL
                REFERENCES timestamped_input(id),
            display_order INTEGER NOT NULL
        );");

        Exec(tx, @"CREATE TABLE IF NOT EXISTS user_submitted_dictionary_entries (
            id INTEGER PRIMARY KEY,
            surface TEXT NOT NULL,
            reading TEXT NULL,
            count_as TEXT NULL,
            notes TEXT NULL,
            created_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))
        );");

        tx.Commit();
    }

    private void EnsureDefaultSettings()
    {
        using var tx = _conn.BeginTransaction();

        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO settings (key, value)
            VALUES ($key, $value);
            ";

        var pKey = cmd.CreateParameter();
        pKey.ParameterName = "$key";
        cmd.Parameters.Add(pKey);

        var pValue = cmd.CreateParameter();
        pValue.ParameterName = "$value";
        cmd.Parameters.Add(pValue);

        void InsertDefault(string key, string value)
        {
            pKey.Value = key;
            pValue.Value = value;
            cmd.ExecuteNonQuery();
        }

        // Invariants
        //InsertDefault("count.ignore_punctuation", "1");
        InsertDefault("count.ignore_decorative_symbols_and_emoji", "1");
        InsertDefault("count.ignore_auxiliaries", "0");
        InsertDefault("count.normalize_iteration_marks", "0");

        // Part-of-speech inclusion / exclusion
        InsertDefault("count.ignore_particles_and_copula", "1");
        InsertDefault("count.ignore_interjections", "0");
        InsertDefault("count.ignore_fillers", "0");

        // Word form choice
        InsertDefault("count.form_preference", "SurfaceForm");
        InsertDefault("count.merge_te_miru_constructions", "0");

        // Orthographic collapsing
        InsertDefault("count.merge_latin_characters_if_different_case_and_character_set", "1");
        InsertDefault("count.do_unicode_nfkc_normalization", "1");
        InsertDefault("count.merge_kana", "Neither");
        InsertDefault("count.deduplicate_repeated_characters", "1");

        // Numeric handling
        InsertDefault("count.merge_numeric_variants", "1");

        // Unknown tokens
        InsertDefault("count.unknown_token_policy", "keep_surface");

        // Word identity / grouping
        InsertDefault("count.token_key_fields", "surface_plus_pos");
        InsertDefault("count.include_reading", "1");

        // Math symbols
        InsertDefault("count.handle_math_symbols", "Ignore");

        // Sidebar: timestamped input
        InsertDefault("sidebar.timestamped_input.sort_type", "ChronologicalDescending");

        // Sidebar: timestamped input width (NULL means: leave XAML Auto width alone)
        using (var cmd2 = _conn.CreateCommand())
        {
            cmd2.Transaction = tx;
            cmd2.CommandText = @"
                INSERT OR IGNORE INTO settings (key, value)
                VALUES ('sidebar.timestamped_input.expanded_width', NULL);

                INSERT OR IGNORE INTO settings (key, value)
                VALUES ('sidebar.UI2.expanded_width', NULL);
            ";

            cmd2.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static void Exec(SqliteTransaction tx, string sql)
    {
        using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn.Dispose();
    }
}

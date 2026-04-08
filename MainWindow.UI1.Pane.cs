// MainWindow.UI1.Pane.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;


namespace jpn_lang_dbm_desktop_app;

public partial class MainWindow : Window
{
    private void SubmitButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;

        try
        {
            var input = (InputTextBox.Text ?? string.Empty).Trim();

            if (input.Length == 0)
            {
                ResultsTextBox.Text = "No input text.";
                return;
            }

            var dict = "ipadic";
            var parseContextJson = _linderaWasm.GetParseContextJson();

            var payload = new
            {
                text = input,
                dict = dict,
            };

            var json = JsonSerializer.Serialize(payload);
            var body = _linderaWasm.LinderaParseJson(json);

            var wordCounts = WordCountBuilder.Build(App.Db.Connection, body);
            SaveToDatabase(input, dict, body, parseContextJson, wordCounts);
            LinderaTextBox.Text = PrettyJson(body);
            ResultsTextBox.Text = FormatWordCounts(wordCounts);

            var sortType = ReadTimestampedInputSortTypeOrThrow();
            TimestampedInputItems.ItemsSource = ReadTimestampedInputRows(sortType);
        }
        catch (Exception ex)
        {
            ResultsTextBox.Text = ex.ToString();
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }

    private static void SaveToDatabase(
        string input,
        string dict,
        string linderaJson,
        string parseContextJson,
        Dictionary<string, int> wordCounts
    )
    {
        var inputHash = ComputeSha256Hex(input);

        using var tx = App.Db.Connection.BeginTransaction();

        long textId;

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                SELECT id
                FROM text_entries
                WHERE input_hash = $hash;
            ";
            cmd.Parameters.AddWithValue("$hash", inputHash);

            var existing = cmd.ExecuteScalar();
            if (existing != null)
            {
                textId = Convert.ToInt64(existing);
            }
            else
            {
                cmd.CommandText = @"
                    INSERT INTO text_entries (input_hash, input_text)
                    VALUES ($hash, $text);
                    SELECT last_insert_rowid();
                ";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$hash", inputHash);
                cmd.Parameters.AddWithValue("$text", input);

                textId = Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;

            var nowUtc = DateTime.UtcNow.ToString("O");
            var wordCountJson = JsonSerializer.Serialize(wordCounts);

            cmd.CommandText = @"
                INSERT INTO timestamped_input
                (created_utc, text_entry_id, word_count_json)
                VALUES ($created_utc, $tid, $word_count_json);
            ";

            cmd.Parameters.AddWithValue("$created_utc", nowUtc);
            cmd.Parameters.AddWithValue("$tid", textId);
            cmd.Parameters.AddWithValue("$word_count_json", wordCountJson);

            cmd.ExecuteNonQuery();
        }

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;

            var parseContextHash = ComputeSha256Hex(parseContextJson + "\nDICT=" + dict);
            var linderaHash = ComputeSha256Hex(linderaJson);
            var nowUtc = DateTime.UtcNow.ToString("O");

            cmd.CommandText = @"
                SELECT id
                FROM lindera_results_cache
                WHERE text_entry_id = $tid
                  AND dict = $dict
                  AND result_json_hash = $json_hash
                  AND parse_context_hash = $parse_context_hash;
            ";
            cmd.Parameters.AddWithValue("$tid", textId);
            cmd.Parameters.AddWithValue("$dict", dict);
            cmd.Parameters.AddWithValue("$json_hash", linderaHash);
            cmd.Parameters.AddWithValue("$parse_context_hash", parseContextHash);

            var existingId = cmd.ExecuteScalar();

            cmd.Parameters.Clear();

            if (existingId != null)
            {
                cmd.CommandText = @"
                    UPDATE lindera_results_cache
                    SET last_used_utc = $last_used_utc
                    WHERE id = $id;
                ";
                cmd.Parameters.AddWithValue("$last_used_utc", nowUtc);
                cmd.Parameters.AddWithValue("$id", Convert.ToInt64(existingId));
            }
            else
            {
                cmd.CommandText = @"
                    INSERT INTO lindera_results_cache
                    (created_utc, last_used_utc, text_entry_id, dict,
                     result_json, result_json_hash,
                     parse_context_json, parse_context_hash)
                    VALUES
                    ($created_utc, $last_used_utc, $tid, $dict,
                     $json, $json_hash,
                     $parse_context_json, $parse_context_hash);
                ";
                cmd.Parameters.AddWithValue("$created_utc", nowUtc);
                cmd.Parameters.AddWithValue("$last_used_utc", nowUtc);
                cmd.Parameters.AddWithValue("$tid", textId);
                cmd.Parameters.AddWithValue("$dict", dict);
                cmd.Parameters.AddWithValue("$json", linderaJson);
                cmd.Parameters.AddWithValue("$json_hash", linderaHash);
                cmd.Parameters.AddWithValue("$parse_context_json", parseContextJson);
                cmd.Parameters.AddWithValue("$parse_context_hash", parseContextHash);
            }

            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static string ComputeSha256Hex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);

        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));

        return sb.ToString();
    }

    private static string PrettyJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(
            doc.RootElement,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }
        );
    }

    private static string FormatWordCounts(Dictionary<string, int> counts)
    {
        var sb = new StringBuilder();

        foreach (var kvp in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append(kvp.Value);
            sb.Append('\t');
            sb.AppendLine(kvp.Key);
        }

        return sb.ToString();
    }
}

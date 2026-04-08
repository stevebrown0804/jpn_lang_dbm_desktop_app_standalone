// MainWindow.UI1.Sidebar.cs

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace jpn_lang_dbm_desktop_app;

internal sealed class TimestampedInputRow
{
    public long Id { get; init; }
    public string TimestampUtc { get; init; } = "";
    public string InputText { get; init; } = "";
    public string WordCountJson { get; init; } = "";
    public string WordCountDisplay { get; init; } = "";
}

internal enum Sidebar_TimestampedInput_SortTypes
{
    Custom,
    ChronologicalAscending,
    ChronologicalDescending,
}

public sealed class SourceMetadataDropdownItem
{
    public long TimestampedInputId { get; init; }
    public string DisplayText { get; init; } = "";

    public override string ToString() => DisplayText;
}

public partial class MainWindow : Window
{
    private void TimestampedInputItems_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TimestampedInputItems == null)
            throw new InvalidOperationException("TimestampedInputItems not found. Check the XAML x:Name.");

        if (AssignSourceDataButton == null)
            throw new InvalidOperationException("AssignSourceDataButton not found. Check the XAML x:Name.");

        if (TimestampedInputItems.SelectedItem is not TimestampedInputRow row)
        {
            _selectedTimestampedInputId = null;
            AssignSourceDataButton.IsEnabled = false;
            return;
        }

        _selectedTimestampedInputId = row.Id;
        AssignSourceDataButton.IsEnabled = true;
    }

    private void LoadTimestampedInputSidebar()
    {
        var sortType = ReadTimestampedInputSortTypeOrThrow();
        ApplySortRadioSelectionOrThrow(sortType);

        TimestampedInputItems.ItemsSource = ReadTimestampedInputRows(sortType);
    }

    private Sidebar_TimestampedInput_SortTypes ReadTimestampedInputSortTypeOrThrow()
    {
        const string key = "sidebar.timestamped_input.sort_type";

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT value
            FROM settings
            WHERE key = $key;
        ";
        cmd.Parameters.AddWithValue("$key", key);

        var vObj = cmd.ExecuteScalar();
        if (vObj == null)
            throw new InvalidOperationException("Missing settings value for key: " + key);

        var v = Convert.ToString(vObj) ?? "";

        if (!Enum.TryParse<Sidebar_TimestampedInput_SortTypes>(v, ignoreCase: false, out var parsed))
            throw new InvalidOperationException("Invalid value for " + key + ": '" + v + "'");

        return parsed;
    }

    private void ApplySortRadioSelectionOrThrow(Sidebar_TimestampedInput_SortTypes sortType)
    {
        if (sortType == Sidebar_TimestampedInput_SortTypes.Custom)
        {
            TimestampedInputSort_Custom.IsChecked = true;
            return;
        }

        if (sortType == Sidebar_TimestampedInput_SortTypes.ChronologicalAscending)
        {
            TimestampedInputSort_Asc.IsChecked = true;
            return;
        }

        if (sortType == Sidebar_TimestampedInput_SortTypes.ChronologicalDescending)
        {
            TimestampedInputSort_Desc.IsChecked = true;
            return;
        }

        throw new InvalidOperationException("Unhandled Sidebar_TimestampedInput_SortTypes value: " + sortType);
    }

    private List<TimestampedInputRow> ReadTimestampedInputRows(Sidebar_TimestampedInput_SortTypes sortType)
    {
        var rows = new List<TimestampedInputRow>();

        using var cmd = App.Db.Connection.CreateCommand();

        if (sortType == Sidebar_TimestampedInput_SortTypes.Custom)
        {
            cmd.CommandText = @"
                SELECT
                    ti.id,
                    ti.created_utc,
                    te.input_text,
                    ti.word_count_json
                FROM timestamped_input ti
                JOIN text_entries te ON te.id = ti.text_entry_id
                LEFT JOIN timestamped_input_sidebar_custom_sorting tis ON tis.timestamped_input_id = ti.id
                ORDER BY
                    (tis.display_order IS NULL) ASC,
                    tis.display_order ASC,
                    ti.created_utc DESC
                LIMIT 50;
            ";
        }
        else if (sortType == Sidebar_TimestampedInput_SortTypes.ChronologicalAscending)
        {
            cmd.CommandText = @"
                SELECT
                    ti.id,
                    ti.created_utc,
                    te.input_text,
                    ti.word_count_json
                FROM timestamped_input ti
                JOIN text_entries te ON te.id = ti.text_entry_id
                ORDER BY ti.created_utc ASC
                LIMIT 50;
            ";
        }
        else if (sortType == Sidebar_TimestampedInput_SortTypes.ChronologicalDescending)
        {
            cmd.CommandText = @"
                SELECT
                    ti.id,
                    ti.created_utc,
                    te.input_text,
                    ti.word_count_json
                FROM timestamped_input ti
                JOIN text_entries te ON te.id = ti.text_entry_id
                ORDER BY ti.created_utc DESC
                LIMIT 50;
            ";
        }
        else
        {
            throw new InvalidOperationException("Unhandled Sidebar_TimestampedInput_SortTypes value: " + sortType);
        }

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var id = rdr.GetInt64(0);
            var createdUtc = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
            var inputText = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
            var wordCountJson = rdr.IsDBNull(3) ? "" : rdr.GetString(3);
            string wordCountDisplay;

            if (string.IsNullOrWhiteSpace(wordCountJson))
            {
                wordCountDisplay = "";
            }
            else
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(wordCountJson);
                    wordCountDisplay = parsed == null ? "" : FormatWordCounts(parsed);
                }
                catch
                {
                    wordCountDisplay = wordCountJson;
                }
            }

            rows.Add(new TimestampedInputRow
            {
                Id = id,
                TimestampUtc = FormatLocalTimestamp(createdUtc),
                InputText = Trunc(inputText, 48),
                WordCountJson = wordCountJson,
                WordCountDisplay = wordCountDisplay,
            });
        }

        return rows;
    }

    private static string FormatLocalTimestamp(string utcIso)
    {
        if (!DateTime.TryParse(
                utcIso,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var utc))
            return "";

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
        var now = DateTime.Now;

        bool isToday = local.Date == now.Date;
        bool isThisYear = local.Year == now.Year;

        string timePart = local.ToString("h:mm tt").ToLowerInvariant();

        if (isToday)
            return timePart;

        string month =
            local.Month switch
            {
                9 => "Sept.",
                _ => local.ToString("MMM.")
            };

        if (isThisYear)
            return $"{timePart}, {month} {local.Day}";

        return $"{timePart}, {month} {local.Day}, {local.Year}";
    }

    private static string Trunc(string s, int maxLen)
    {
        if (maxLen <= 0)
            return "";

        if (s.Length <= maxLen)
            return s;

        if (maxLen <= 3)
            return s.Substring(0, maxLen);

        return s.Substring(0, maxLen - 3) + "...";
    }

    private static string PreviewStopAtNewlineOrTab(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s) || maxLen <= 0)
            return "";

        int stop = -1;

        for (int i = 0; i < s.Length && i < maxLen; i++)
        {
            if (s[i] == '\n' || s[i] == '\r' || s[i] == '\t')
            {
                stop = i;
                break;
            }
        }

        if (stop >= 0)
            return s.Substring(0, stop);

        if (s.Length <= maxLen)
            return s;

        if (maxLen <= 3)
            return s.Substring(0, maxLen);

        return s.Substring(0, maxLen - 3) + "...";
    }

    private static string PreviewStopAtNewlineTabsToSpaces(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s) || maxLen <= 0)
            return "";

        int stop = -1;

        for (int i = 0; i < s.Length && i < maxLen; i++)
        {
            if (s[i] == '\n' || s[i] == '\r')
            {
                stop = i;
                break;
            }
        }

        string head;

        if (stop >= 0)
            head = s.Substring(0, stop);
        else if (s.Length <= maxLen)
            head = s;
        else if (maxLen <= 3)
            head = s.Substring(0, maxLen);
        else
            head = s.Substring(0, maxLen - 3) + "...";

        return head.Replace('\t', ' ');
    }

    private void TimestampedInputSortRadio_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb)
            return;

        var tag = rb.Tag as string ?? "";
        if (!Enum.TryParse<Sidebar_TimestampedInput_SortTypes>(tag, ignoreCase: false, out var sortType))
            throw new InvalidOperationException("Sort button Tag is not a valid Sidebar_TimestampedInput_SortTypes value: '" + tag + "'");

        const string key = "sidebar.timestamped_input.sort_type";

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE settings
                SET value = $value
                WHERE key = $key;
            ";
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", sortType.ToString());

            var changed = cmd.ExecuteNonQuery();
            if (changed != 1)
                throw new InvalidOperationException("Expected settings row to update exactly once for key: " + key);
        }

        TimestampedInputItems.ItemsSource = ReadTimestampedInputRows(sortType);
    }

    private void TimestampedInputDelete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        if (btn.Tag is not long id)
        {
            if (btn.Tag is int i)
                id = i;
            else if (btn.Tag is string s && long.TryParse(s, out var parsed))
                id = parsed;
            else
                throw new InvalidOperationException("Delete button Tag did not contain a valid timestamped_input id.");
        }

        var current = TimestampedInputItems.ItemsSource as List<TimestampedInputRow>;
        if (current != null)
        {
            var next = current.Where(r => r.Id != id).ToList();
            TimestampedInputItems.ItemsSource = next;
        }
        else
        {
            var sortType = ReadTimestampedInputSortTypeOrThrow();
            TimestampedInputItems.ItemsSource = ReadTimestampedInputRows(sortType);
        }

        try
        {
            DeleteTimestampedInputAndCleanupTextEntry(id);
        }
        catch (Exception ex)
        {
            ResultsTextBox.Text = ex.ToString();

            var sortType = ReadTimestampedInputSortTypeOrThrow();
            TimestampedInputItems.ItemsSource = ReadTimestampedInputRows(sortType);
        }
    }

    private static void DeleteTimestampedInputAndCleanupTextEntry(long timestampedInputId)
    {
        using var tx = App.Db.Connection.BeginTransaction();

        long textEntryId;

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                SELECT text_entry_id
                FROM timestamped_input
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", timestampedInputId);

            var v = cmd.ExecuteScalar();
            if (v == null || v is DBNull)
                throw new InvalidOperationException("timestamped_input row not found for id: " + timestampedInputId);

            textEntryId = Convert.ToInt64(v);
        }

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                DELETE FROM timestamped_input_sidebar_custom_sorting
                WHERE timestamped_input_id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", timestampedInputId);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                DELETE FROM timestamped_input
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", timestampedInputId);

            var changed = cmd.ExecuteNonQuery();
            if (changed != 1)
                throw new InvalidOperationException("Expected timestamped_input delete to affect exactly one row. id=" + timestampedInputId);
        }

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                SELECT COUNT(1)
                FROM timestamped_input
                WHERE text_entry_id = $tid;
            ";
            cmd.Parameters.AddWithValue("$tid", textEntryId);

            var remaining = Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
            if (remaining == 0)
            {
                cmd.CommandText = @"
                    DELETE FROM text_entries
                    WHERE id = $tid;
                ";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$tid", textEntryId);

                cmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    // ---------------------------
    // Import sidebar sizing
    // ---------------------------

    private void InitializeImportRightPaneSizing()
    {
        if (ImportRightPaneSplitter.Parent is not Grid g)
            throw new InvalidOperationException("Expected ImportRightPaneSplitter to be inside a Grid.");

        var col = g.ColumnDefinitions[2];

        _importRightPaneExpandedWidthPx = ReadImportExpandedWidthPxOrNull();

        if (_importRightPaneExpandedWidthPx != null)
        {
            col.Width = new GridLength(_importRightPaneExpandedWidthPx.Value);
            ImportRightPaneSplitter.IsVisible = true;
        }
        else
        {
            ImportRightPaneSplitter.IsVisible = col.Width.Value > 0;
        }
    }

    private void ImportRightPaneRoot_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (ImportRightPaneSplitter.Parent is not Grid g)
            throw new InvalidOperationException("Expected ImportRightPaneSplitter to be inside a Grid.");

        var col = g.ColumnDefinitions[2];

        if (col.Width.GridUnitType == GridUnitType.Pixel && col.Width.Value <= 0)
            return;

        var w = ImportRightPaneRoot.Bounds.Width;
        if (double.IsNaN(w) || double.IsInfinity(w))
            return;

        var px = (int)Math.Round(w);
        if (px <= 0)
            return;

        if (_importRightPaneExpandedWidthPx != px)
        {
            _importRightPaneExpandedWidthPx = px;
            WriteImportExpandedWidthPx(px);
        }
    }

    private void ToggleImportRightPane_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ImportRightPaneSplitter.Parent is not Grid g)
            throw new InvalidOperationException("Expected ImportRightPaneSplitter to be inside a Grid.");

        var col = g.ColumnDefinitions[2];

        // Collapsed
        if (col.Width.GridUnitType == GridUnitType.Pixel && col.Width.Value <= 0)
        {
            if (_importRightPaneExpandedWidthPx != null)
                col.Width = new GridLength(_importRightPaneExpandedWidthPx.Value);
            else
                col.Width = GridLength.Auto;

            ImportRightPaneSplitter.IsVisible = true;
            return;
        }

        // Expanded
        if (col.Width.GridUnitType == GridUnitType.Pixel && col.Width.Value > 0)
        {
            var px = (int)Math.Round(col.Width.Value);
            if (px > 0)
            {
                _importRightPaneExpandedWidthPx = px;
                WriteImportExpandedWidthPx(px);
            }
        }

        col.Width = new GridLength(0);
        ImportRightPaneSplitter.IsVisible = false;
    }

    private int? ReadImportExpandedWidthPxOrNull()
    {
        const string key = "sidebar.timestamped_input.expanded_width";

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT value
            FROM settings
            WHERE key = $key;
        ";
        cmd.Parameters.AddWithValue("$key", key);

        var vObj = cmd.ExecuteScalar();

        if (vObj == null || vObj is DBNull)
            return null;

        var s = Convert.ToString(vObj) ?? "";
        if (s.Length == 0)
            return null;

        if (!int.TryParse(s, out var px))
            throw new InvalidOperationException("Invalid value for " + key + ": '" + s + "'");

        if (px <= 0)
            throw new InvalidOperationException("Invalid value for " + key + ": '" + s + "'");

        return px;
    }

    private void WriteImportExpandedWidthPx(int px)
    {
        if (px <= 0)
            throw new InvalidOperationException("WriteImportExpandedWidthPx received non-positive width: " + px);

        const string key = "sidebar.timestamped_input.expanded_width";

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO settings (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        ";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", px.ToString());

        cmd.ExecuteNonQuery();
    }
}

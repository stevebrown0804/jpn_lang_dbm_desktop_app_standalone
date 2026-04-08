//MainWindow.UI2.Pane.cs

using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;

namespace jpn_lang_dbm_desktop_app_standalone;

public partial class MainWindow : Window
{
    private void AssignSourceDataButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedTimestampedInputId == null)
            return;

        if (NavView == null)
            throw new InvalidOperationException("NavView not found. Check the XAML x:Name.");

        var id = _selectedTimestampedInputId.Value;

        var items = ReadSourceMetadataDropdownItems();
        var match = items.Find(i => i.TimestampedInputId == id);

        if (match == null)
            throw new InvalidOperationException("Could not find source metadata dropdown item for timestamped_input id: " + id);

        _selectedTimestampedInputId = match.TimestampedInputId;
        SelectedSourceTextBox.Text = match.DisplayText;

        ApplySelectedSourcePreviewTextOrThrow(match.TimestampedInputId);

        Avalonia.Threading.Dispatcher.UIThread.Post(
            ApplySelectedSourceColumnSizing,
            Avalonia.Threading.DispatcherPriority.Loaded);

       SelectNavTag("source");
    }

    private void ChooseSourceButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var items = ReadSourceMetadataDropdownItems();

        // Drop the placeholder row
        items.RemoveAll(i => i.TimestampedInputId == 0);

        var win = new SourceInputSelectorWindow(items);

        win.SourceSelected += item =>
        {
            _selectedTimestampedInputId = item.TimestampedInputId;
            if (AttachSourceDataToInputButton == null)
                throw new InvalidOperationException("AttachSourceDataToInputButton not found.");

            AttachSourceDataToInputButton.IsEnabled = true;
            SelectedSourceTextBox.Text = item.DisplayText;

            ApplySelectedSourcePreviewTextOrThrow(item.TimestampedInputId);

            Avalonia.Threading.Dispatcher.UIThread.Post(
                ApplySelectedSourceColumnSizing,
                Avalonia.Threading.DispatcherPriority.Loaded);
        };

        win.Show();
    }

    private void ClearSourceButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _selectedTimestampedInputId = null;

        if (SelectedSourceTextBox == null)
            throw new InvalidOperationException("SelectedSourceTextBox not found.");

        SelectedSourceTextBox.Text = ""; //render the watermark text again

        if (AttachSourceDataToInputButton == null)
            throw new InvalidOperationException("AttachSourceDataToInputButton not found.");

        AttachSourceDataToInputButton.IsEnabled = false;

        if (SelectedSource_Time == null || SelectedSource_InputPreview == null || SelectedSource_WordCountPreview == null)
            throw new InvalidOperationException("Selected source preview controls not found.");

        SelectedSource_Time.Text = "";
        SelectedSource_InputPreview.Text = "";
        SelectedSource_WordCountPreview.Text = "";
    }

    private List<SourceMetadataDropdownItem> ReadSourceMetadataDropdownItems()
    {
        var items = new List<SourceMetadataDropdownItem>();

        using var cmd = App.Db.Connection.CreateCommand();
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

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var id = rdr.GetInt64(0);
            var createdUtc = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
            var inputText = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
            var wordCountJson = rdr.IsDBNull(3) ? "" : rdr.GetString(3);

            var ts = FormatLocalTimestamp(createdUtc);

            var inputPart = PreviewStopAtNewlineOrTab(inputText, 10);

            string wcPart;
            if (string.IsNullOrWhiteSpace(wordCountJson))
            {
                wcPart = "";
            }
            else
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(wordCountJson);
                    wcPart = parsed == null ? "" : PreviewStopAtNewlineTabsToSpaces(FormatWordCounts(parsed), 10);
                }
                catch
                {
                    wcPart = PreviewStopAtNewlineTabsToSpaces(wordCountJson, 10);
                }
            }

            items.Add(new SourceMetadataDropdownItem
            {
                TimestampedInputId = id,
                DisplayText = $"{ts} -- {inputPart} -- {wcPart}"
            });
        }

        // Placeholder row for "no selection yet".
        // TimestampedInputId=0 is reserved as "no selection".
        items.Insert(0, new SourceMetadataDropdownItem
        {
            TimestampedInputId = 0,
            DisplayText = ""  //I think this is moot because this line is never called atm
                                // ...but it'll have the control display its watermark, if it's not
        });

        return items;
    }

    private void ApplySelectedSourcePreviewTextOrThrow(long timestampedInputId)
    {
        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                ti.created_utc,
                te.input_text,
                ti.word_count_json
            FROM timestamped_input ti
            JOIN text_entries te ON te.id = ti.text_entry_id
            WHERE ti.id = $id
            LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$id", timestampedInputId);

        using var rdr = cmd.ExecuteReader();

        if (!rdr.Read())
            throw new InvalidOperationException("timestamped_input row not found for id: " + timestampedInputId);

        var createdUtc = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
        var inputText = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
        var wordCountJson = rdr.IsDBNull(2) ? "" : rdr.GetString(2);

        var ts = FormatLocalTimestamp(createdUtc);
        var inputPart = PreviewStopAtNewlineOrTab(inputText, 48);

        string wcPart;
        if (string.IsNullOrWhiteSpace(wordCountJson))
        {
            wcPart = "";
        }
        else
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, int>>(wordCountJson);
                wcPart = parsed == null ? "" : PreviewStopAtNewlineTabsToSpaces(FormatWordCounts(parsed), 48);
            }
            catch
            {
                wcPart = PreviewStopAtNewlineTabsToSpaces(wordCountJson, 48);
            }
        }

        SelectedSource_Time.Text = ts;
        SelectedSource_InputPreview.Text = inputPart;
        SelectedSource_WordCountPreview.Text = wcPart;
    }

    private void ApplySelectedSourceColumnSizing()
    {
        if (!SourceMetadataPage.IsVisible)
            return;

        var grid = SelectedSourcePreviewGrid;
        if (grid == null)
            return;

        if (grid.ColumnDefinitions.Count != 3)
            return;

        var timeCol = grid.ColumnDefinitions[0];
        var inputCol = grid.ColumnDefinitions[1];
        var wcCol = grid.ColumnDefinitions[2];

        var inputText = SelectedSource_InputPreview;
        var wcText = SelectedSource_WordCountPreview;

        if (inputText == null || wcText == null)
            return;

        double inputDesired = inputText.DesiredSize.Width;
        double wcDesired = wcText.DesiredSize.Width;

        double inputActual = inputText.Bounds.Width;
        double wcActual = wcText.Bounds.Width;

        bool inputOverflows = inputDesired > inputActual + 0.5;
        bool wcOverflows = wcDesired > wcActual + 0.5;

        timeCol.Width = GridLength.Auto;

        if (inputOverflows == wcOverflows)
        {
            inputCol.Width = new GridLength(1, GridUnitType.Star);
            wcCol.Width = new GridLength(1, GridUnitType.Star);
            return;
        }

        if (!inputOverflows && wcOverflows)
        {
            inputCol.Width = GridLength.Auto;
            wcCol.Width = new GridLength(1, GridUnitType.Star);
            return;
        }

        if (inputOverflows && !wcOverflows)
        {
            wcCol.Width = GridLength.Auto;
            inputCol.Width = new GridLength(1, GridUnitType.Star);
        }
    }

    private void PopulateTemplateComboBoxOrThrow()
    {
        if (TemplateComboBox == null)
            throw new InvalidOperationException("TemplateComboBox not found. Check the XAML x:Name.");

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT template_name
            FROM templates
            ORDER BY template_name COLLATE NOCASE;
        ";

        using var rdr = cmd.ExecuteReader();

        var names = new System.Collections.Generic.List<string>();

        while (rdr.Read())
        {
            if (!rdr.IsDBNull(0))
                names.Add(rdr.GetString(0));
        }

        TemplateComboBox.ItemsSource = names;

        if (names.Count == 0)
        {
            TemplateComboBox.SelectedIndex = -1;
            return;
        }

        var blankIndex = names.FindIndex(n => string.Equals(n, "Blank", StringComparison.OrdinalIgnoreCase));
        TemplateComboBox.SelectedIndex = blankIndex >= 0 ? blankIndex : 0;
        UpdateTemplateBlankFieldsVisibilityOrThrow();
    }

    private void TemplateComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateTemplateBlankFieldsVisibilityOrThrow();
    }

    private void UpdateTemplateBlankFieldsVisibilityOrThrow()
    {
        if (TemplateBlankFieldsRow == null)
            throw new InvalidOperationException("TemplateBlankFieldsRow not found. Check the XAML x:Name.");

        if (TemplateComboBox == null)
            throw new InvalidOperationException("TemplateComboBox not found. Check the XAML x:Name.");

        if (TemplateComboBox.SelectedItem is not string templateName || string.IsNullOrWhiteSpace(templateName))
        {
            TemplateBlankFieldsRow.IsVisible = false;
            _templateKeyValueRows.Clear();
            return;
        }

        long templateId;

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT id
                FROM templates
                WHERE template_name = $name
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("$name", templateName);

            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value)
                throw new InvalidOperationException("Template not found in DB: " + templateName);

            templateId = Convert.ToInt64(obj);
        }

        TemplateBlankFieldsRow.IsVisible = true;

        _templateKeyValueRows.Clear();

        if (templateId == 0)
            return;

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT key
                FROM template_keys
                WHERE template_id = $id
                ORDER BY display_order ASC;
            ";
            cmd.Parameters.AddWithValue("$id", templateId);

            using var rdr = cmd.ExecuteReader();

            while (rdr.Read())
            {
                var key = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                _templateKeyValueRows.Add(new TemplateKeyValueRow
                {
                    LeftText = key,
                    RightText = ""
                });
            }
        }
    }

    private void TemplateAddRowButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _templateKeyValueRows.Add(new TemplateKeyValueRow());
    }

    private void TemplateDeleteRowButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button btn)
            throw new InvalidOperationException("TemplateDeleteRowButton_OnClick sender was not a Button.");

        if (btn.Tag is not TemplateKeyValueRow row)
            throw new InvalidOperationException("TemplateDeleteRowButton_OnClick Button.Tag was not a TemplateKeyValueRow.");

        if (!_templateKeyValueRows.Remove(row))
            throw new InvalidOperationException("TemplateDeleteRowButton_OnClick could not remove the row from the collection.");
    }

    private async void SaveAsTemplateButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var reservedName = ReadReservedTemplateNameOrThrow();

        var initialName = "";
        while (true)
        {
            var (ok, enteredName) = await ShowTemplateNameDialogAsync(initialName);
            if (!ok)
                return;

            var name = (enteredName ?? "").Trim();
            initialName = name;

            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (string.Equals(name, reservedName, StringComparison.OrdinalIgnoreCase))
            {
                await ShowErrorDialogAsync("The template name '" + reservedName + "' is reserved. Please enter a different name.");
                continue;
            }

            var existingId = FindTemplateIdByNameOrNull(name);

            if (existingId.HasValue)
            {
                var replace = await ShowReplaceTemplateDialogAsync(name);
                if (!replace)
                    continue;

                DeleteTemplateByIdOrThrow(existingId.Value);
            }

            WriteTemplateOrThrow(name);
            PopulateTemplateComboBoxOrThrow();

            if (TemplateComboBox?.ItemsSource is System.Collections.Generic.List<string> names)
            {
                var exactName = names.Find(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(exactName))
                    TemplateComboBox.SelectedItem = exactName;
            }

            UpdateTemplateBlankFieldsVisibilityOrThrow();
            return;
        }
    }

    private string ReadReservedTemplateNameOrThrow()
    {
        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT template_name
            FROM templates
            WHERE id = 0
            LIMIT 1;
        ";

        var obj = cmd.ExecuteScalar();
        if (obj == null || obj == DBNull.Value)
            throw new InvalidOperationException("Reserved template name not found. Expected templates.id == 0.");

        return Convert.ToString(obj) ?? "";
    }

    private long? FindTemplateIdByNameOrNull(string templateName)
    {
        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id
            FROM templates
            WHERE template_name = $name COLLATE NOCASE
            LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$name", templateName);

        var obj = cmd.ExecuteScalar();
        if (obj == null || obj == DBNull.Value)
            return null;

        return Convert.ToInt64(obj);
    }

    private void DeleteTemplateByIdOrThrow(long templateId)
    {
        if (templateId == 0)
            throw new InvalidOperationException("Refusing to delete templates.id == 0.");

        using var tx = App.Db.Connection.BeginTransaction();

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                DELETE FROM templates
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", templateId);

            var rows = cmd.ExecuteNonQuery();
            if (rows != 1)
                throw new InvalidOperationException("DeleteTemplateByIdOrThrow expected to delete 1 row, deleted: " + rows);
        }

        tx.Commit();
    }

    private void WriteTemplateOrThrow(string templateName)
    {
        using var tx = App.Db.Connection.BeginTransaction();

        long newTemplateId;

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO templates (template_name)
                VALUES ($name);
            ";
            cmd.Parameters.AddWithValue("$name", templateName);

            var rows = cmd.ExecuteNonQuery();
            if (rows != 1)
                throw new InvalidOperationException("WriteTemplateOrThrow expected to insert 1 template row, inserted: " + rows);
        }

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT last_insert_rowid();";

            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value)
                throw new InvalidOperationException("WriteTemplateOrThrow could not read last_insert_rowid().");

            newTemplateId = Convert.ToInt64(obj);
        }

        int displayOrder = 0;

        foreach (var row in _templateKeyValueRows)
        {
            var key = (row.LeftText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            using var cmd = App.Db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO template_keys (template_id, key, display_order)
                VALUES ($templateId, $key, $order);
            ";
            cmd.Parameters.AddWithValue("$templateId", newTemplateId);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$order", displayOrder);

            var rows = cmd.ExecuteNonQuery();
            if (rows != 1)
                throw new InvalidOperationException("WriteTemplateOrThrow expected to insert 1 template_keys row, inserted: " + rows);

            displayOrder++;
        }

        tx.Commit();
    }

    private async System.Threading.Tasks.Task<(bool ok, string? name)> ShowTemplateNameDialogAsync(string initialName)
    {
        var tb = new TextBox
        {
            Text = initialName ?? "",
            Width = 320
        };

        var dlg = new ContentDialog
        {
            Title = "Save as template",
            Content = tb,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dlg.ShowAsync(this);
        if (result != ContentDialogResult.Primary)
            return (false, null);

        return (true, tb.Text);
    }

    private async System.Threading.Tasks.Task ShowErrorDialogAsync(string message)
    {
        var dlg = new ContentDialog
        {
            Title = "Error",
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            CloseButtonText = "OK"
        };

        await dlg.ShowAsync(this);
    }

    private async System.Threading.Tasks.Task<bool> ShowReplaceTemplateDialogAsync(string templateName)
    {
        var dlg = new ContentDialog
        {
            Title = "Replace template",
            Content = new TextBlock
            {
                Text = "Replace template '" + templateName + "'?",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            PrimaryButtonText = "Confirm",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dlg.ShowAsync(this);
        return result == ContentDialogResult.Primary;
    }

    private static string BuildSourceDataSummary(System.Collections.Generic.IEnumerable<TemplateKeyValueRow> rows)
    {
        static string Clean(string s)
        {
            return (s ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Trim();
        }

        static string Quote(string s)
        {
            return "'" + Clean(s).Replace("'", "''") + "'";
        }

        var parts = new System.Collections.Generic.List<string>();

        foreach (var r in rows)
        {
            var key = Clean(r.LeftText ?? "");
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var value = Clean(r.RightText ?? "");
            parts.Add(Quote(key) + ", " + Quote(value));
        }

        return string.Join("; ", parts);
    }

    private void LoadReusableSourceDataSummarySidebarOrThrow()
    {
        if (SourceDataBlockRowsGrid == null)
            throw new InvalidOperationException("SourceDataBlockRowsGrid not found. Check the XAML x:Name.");

        _sourceDataBlockSummaryRows.Clear();

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id
            FROM source_data_block_stash
            ORDER BY created_utc DESC;
        ";

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var blockId = rdr.GetInt64(0);

            var kvRows = ReadSourceDataBlockRowsAsTemplateKeyValueRows(blockId);
            var summary = BuildSourceDataSummary(kvRows);

            _sourceDataBlockSummaryRows.Add(new SourceDataBlockRowGridRow
            {
                Id = blockId,
                SummaryText = summary
            });
        }
    }

    private static System.Collections.Generic.List<TemplateKeyValueRow> ReadSourceDataBlockRowsAsTemplateKeyValueRows(long sourceDataBlockId)
    {
        var rows = new System.Collections.Generic.List<TemplateKeyValueRow>();

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT key, value
            FROM source_data_block_stash_rows
            WHERE source_data_block_id = $id
            ORDER BY display_order ASC;
        ";
        cmd.Parameters.AddWithValue("$id", sourceDataBlockId);

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var key = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
            var value = rdr.IsDBNull(1) ? "" : rdr.GetString(1);

            rows.Add(new TemplateKeyValueRow
            {
                LeftText = key,
                RightText = value
            });
        }

        return rows;
    }

    private void SaveCopyOfSourceDataButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        using var tx = App.Db.Connection.BeginTransaction();

        long newBlockId;

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO source_data_block_stash (starting_template_id, created_utc, last_edited_utc)
                VALUES (NULL, $createdUtc, $lastEditedUtc);
            ";
            cmd.Parameters.AddWithValue("$createdUtc", nowUtc);
            cmd.Parameters.AddWithValue("$lastEditedUtc", nowUtc);

            var rows = cmd.ExecuteNonQuery();
            if (rows != 1)
                throw new InvalidOperationException("SaveCopyOfSourceDataButton_OnClick expected to insert 1 source_data_block_stash row, inserted: " + rows);
        }

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT last_insert_rowid();";

            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value)
                throw new InvalidOperationException("SaveCopyOfSourceDataButton_OnClick could not read last_insert_rowid().");

            newBlockId = Convert.ToInt64(obj);
        }

        int displayOrder = 0;

        foreach (var row in _templateKeyValueRows)
        {
            var key = (row.LeftText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var valueRaw = (row.RightText ?? "").Trim();
            object valueParam = string.IsNullOrWhiteSpace(valueRaw) ? DBNull.Value : valueRaw;

            using var cmd = App.Db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO source_data_block_stash_rows (source_data_block_id, key, value, display_order)
                VALUES ($blockId, $key, $value, $order);
            ";
            cmd.Parameters.AddWithValue("$blockId", newBlockId);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", valueParam);
            cmd.Parameters.AddWithValue("$order", displayOrder);

            var rows = cmd.ExecuteNonQuery();
            if (rows != 1)
                throw new InvalidOperationException("SaveCopyOfSourceDataButton_OnClick expected to insert 1 source_data_block_stash_rows row, inserted: " + rows);

            displayOrder++;
        }

        tx.Commit();

        var summary = BuildSourceDataSummary(_templateKeyValueRows);
        _sourceDataBlockSummaryRows.Add(new SourceDataBlockRowGridRow { Id = newBlockId, SummaryText = summary });
    }

    private void AttachSourceDataToInputButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedTimestampedInputId == null)
            throw new InvalidOperationException("AttachSourceDataToInputButton_OnClick called with no selected timestamped input.");

        var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        long sourceDataBlockId = 0;

        bool hasRows = false;
        foreach (var row in _templateKeyValueRows)
        {
            var key = (row.LeftText ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                hasRows = true;
                break;
            }
        }

        using var tx = App.Db.Connection.BeginTransaction();

        if (hasRows)
        {
            using (var cmd = App.Db.Connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO source_data_block (starting_template_id, created_utc, last_edited_utc)
                    VALUES (NULL, $createdUtc, $lastEditedUtc);
                ";
                cmd.Parameters.AddWithValue("$createdUtc", nowUtc);
                cmd.Parameters.AddWithValue("$lastEditedUtc", nowUtc);

                var rows = cmd.ExecuteNonQuery();
                if (rows != 1)
                    throw new InvalidOperationException("AttachSourceDataToInputButton_OnClick expected to insert 1 source_data_block row, inserted: " + rows);
            }

            using (var cmd = App.Db.Connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT last_insert_rowid();";

                var obj = cmd.ExecuteScalar();
                if (obj == null || obj == DBNull.Value)
                    throw new InvalidOperationException("AttachSourceDataToInputButton_OnClick could not read last_insert_rowid().");

                sourceDataBlockId = Convert.ToInt64(obj);
            }

            int displayOrder = 0;

            foreach (var row in _templateKeyValueRows)
            {
                var key = (row.LeftText ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var valueRaw = (row.RightText ?? "").Trim();
                object valueParam = string.IsNullOrWhiteSpace(valueRaw) ? DBNull.Value : valueRaw;

                using var cmd = App.Db.Connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO source_data_block_rows (source_data_block_id, key, value, display_order)
                    VALUES ($blockId, $key, $value, $order);
                ";
                cmd.Parameters.AddWithValue("$blockId", sourceDataBlockId);
                cmd.Parameters.AddWithValue("$key", key);
                cmd.Parameters.AddWithValue("$value", valueParam);
                cmd.Parameters.AddWithValue("$order", displayOrder);

                var rows = cmd.ExecuteNonQuery();
                if (rows != 1)
                    throw new InvalidOperationException("AttachSourceDataToInputButton_OnClick expected to insert 1 source_data_block_rows row, inserted: " + rows);

                displayOrder++;
            }
        }

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO timestamped_input_with_source_data
                (timestamped_input_id, source_data_id, created_utc)
                VALUES ($inputId, $sourceId, $createdUtc);
            ";
            cmd.Parameters.AddWithValue("$inputId", _selectedTimestampedInputId.Value);
            cmd.Parameters.AddWithValue("$sourceId", sourceDataBlockId);
            cmd.Parameters.AddWithValue("$createdUtc", nowUtc);

            var rows = cmd.ExecuteNonQuery();
            if (rows != 1)
                throw new InvalidOperationException("AttachSourceDataToInputButton_OnClick expected to insert 1 timestamped_input_with_source_data row, inserted: " + rows);
        }

        tx.Commit();
        
        PopulateTimestampedInputSourceDataSidebar();

        if (AllowSourceDataReuseCheckBox?.IsChecked == true)
        {
            SaveCopyOfSourceDataButton_OnClick(sender, e);
        }
    }

    private void SelectedSourcePreviewGrid_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplySelectedSourceColumnSizing();
    }
}

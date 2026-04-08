// MainWindow.UI3.Pane.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;

namespace jpn_lang_dbm_desktop_app;


public class TagDisplayPart
{
    public string Text { get; }
    public bool IsMatch { get; }
    public bool IsAttached { get; }

    public TagDisplayPart(string text, bool isMatch, bool isAttached)
    {
        Text = text;
        IsMatch = isMatch;
        IsAttached = isAttached;
    }
}

public class TagDisplay
{
    public List<TagDisplayPart> Parts { get; }

    public TagDisplay(string tag, string filter, bool isAttached)
    {
        Parts = MainWindow.SplitForHighlight(tag, filter, isAttached);
    }
}


public partial class MainWindow
{
    private List<string> _allTagsTab1 = new();
    private List<string> _allTagsTab2 = new();
    private TimestampedInputSourceDataRow? _selectedBundle;
    private Avalonia.Controls.Notifications.WindowNotificationManager? _notificationManager;
 
    private void UpdateAttachTagButtonState()
    {
        if (AttachTagButton == null)
            throw new Exception("AttachTagButton was null");

        if (DetachTagButton == null)
            throw new Exception("DetachTagButton was null");

        if (DeleteTagButton == null)
            throw new Exception("DeleteTagButton was null");

        var hasBundle = _selectedBundle != null;

        var hasSelection =
            Tab1ListBox?.SelectedItems != null &&
            Tab1ListBox.SelectedItems.Count > 0;

        var hasAnyTagSelection = hasSelection;
        var hasAttachDetachSelection = hasBundle && hasSelection;

        AttachTagButton.IsEnabled = hasAttachDetachSelection;
        DetachTagButton.IsEnabled = hasAttachDetachSelection;
        DeleteTagButton.IsEnabled = hasAnyTagSelection;
    }

    private void Tab1ListBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateAttachTagButtonState();
    }

    private void AttachTagButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedBundle == null)
            throw new Exception("No bundle selected");

        var tisId = _selectedBundle.Id;

        using var tx = App.Db.Connection.BeginTransaction();

        if (Tab1ListBox == null)
            throw new Exception("Tab1ListBox was null");

        foreach (var item in Tab1ListBox.SelectedItems ?? throw new Exception("Tab1ListBox.SelectedItems was null"))
        {
            string tagName = item switch
            {
                string s => s,

                TagDisplay td => CombineTagParts(td),

                _ => throw new Exception("Unexpected tag item type")
            };

            using var getTagCmd = App.Db.Connection.CreateCommand();
            getTagCmd.Transaction = tx;
            getTagCmd.CommandText = @"SELECT id FROM tags WHERE name = $name;";
            getTagCmd.Parameters.AddWithValue("$name", tagName);

            var tagIdObj = getTagCmd.ExecuteScalar();
            if (tagIdObj == null)
                throw new Exception($"Tag not found: {tagName}");

            var tagId = (long)tagIdObj;

            using var insertCmd = App.Db.Connection.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                @"INSERT OR IGNORE INTO timestamped_input_with_source_data_tags (ti_sd_id, tag_id)
                VALUES ($tis, $tag);";
            insertCmd.Parameters.AddWithValue("$tis", tisId);
            insertCmd.Parameters.AddWithValue("$tag", tagId);

            insertCmd.ExecuteNonQuery();
        }

        tx.Commit();
        ApplyFilterTab1(Tab1TextBox?.Text ?? "");
        AttachTagsSidebarControl.RefreshAttachedTags();
        UpdateAttachTagButtonState();
    }

    private static string CombineTagParts(TagDisplay td)
    {
        if (td.Parts == null)
            throw new Exception("TagDisplay.Parts was null");

        var combined = "";
        foreach (var part in td.Parts)
            combined += part.Text;

        return combined;
    }

    private List<string> ReadSelectedTagNamesFromTab1ListBox()
    {
        if (Tab1ListBox == null)
            throw new Exception("Tab1ListBox was null");

        var selectedItems = Tab1ListBox.SelectedItems ?? throw new Exception("Tab1ListBox.SelectedItems was null");
        var tagNames = new List<string>();

        foreach (var item in selectedItems)
        {
            string tagName = item switch
            {
                string s => s,
                TagDisplay td => CombineTagParts(td),
                _ => throw new Exception("Unexpected tag item type")
            };

            tagNames.Add(tagName);
        }

        return tagNames;
    }

    private List<long> ReadTagIdsByName(List<string> tagNames, Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        var tagIds = new List<long>();

        foreach (var tagName in tagNames)
        {
            using var cmd = App.Db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT id FROM tags WHERE name = $name;";
            cmd.Parameters.AddWithValue("$name", tagName);

            var result = cmd.ExecuteScalar();
            if (result == null)
                throw new Exception($"Tag not found: {tagName}");

            tagIds.Add((long)result);
        }

        return tagIds;
    }

    private int ReadAffectedInputCountForTagIds(List<long> tagIds, Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        if (tagIds.Count == 0)
            return 0;

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.Transaction = tx;

        var inParts = new List<string>();
        for (var i = 0; i < tagIds.Count; i++)
        {
            var paramName = $"$tag{i}";
            inParts.Add(paramName);
            cmd.Parameters.AddWithValue(paramName, tagIds[i]);
        }

        cmd.CommandText =
            @"SELECT COUNT(DISTINCT ti_sd_id)
            FROM timestamped_input_with_source_data_tags
            WHERE tag_id IN (" + string.Join(", ", inParts) + ");";

        var result = cmd.ExecuteScalar();
        if (result == null || result is DBNull)
            return 0;

        return Convert.ToInt32(result);
    }

    private async System.Threading.Tasks.Task<bool> ShowDeleteTagsConfirmationAsync(int affectedInputCount)
    {
        var dialog = new Window
        {
            Title = "Confirm delete",
            Width = 420,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var message = new TextBlock
        {
            Text = $"This will remove the tag(s) from {affectedInputCount} inputs. Proceed?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 12)
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(12, 6),
            MinWidth = 90
        };

        var proceedButton = new Button
        {
            Content = "Proceed",
            Padding = new Thickness(12, 6),
            MinWidth = 90
        };

        cancelButton.Click += (_, __) => dialog.Close(false);
        proceedButton.Click += (_, __) => dialog.Close(true);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(16, 0, 16, 16)
        };
        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(proceedButton);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        root.Children.Add(message);
        Grid.SetRow(message, 0);
        root.Children.Add(buttonRow);
        Grid.SetRow(buttonRow, 1);

        dialog.Content = root;

        var result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }

    private async void DeleteTagButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_notificationManager == null)
            throw new Exception("_notificationManager was null");

        if (AttachTagsSidebarControl == null)
            throw new Exception("AttachTagsSidebarControl was null");

        var tagNames = ReadSelectedTagNamesFromTab1ListBox();

        int affectedInputCount;
        List<long> tagIds;

        using (var tx = App.Db.Connection.BeginTransaction())
        {
            tagIds = ReadTagIdsByName(tagNames, tx);
            affectedInputCount = ReadAffectedInputCountForTagIds(tagIds, tx);
            tx.Commit();
        }

        if (affectedInputCount > 0)
        {
            var shouldProceed = await ShowDeleteTagsConfirmationAsync(affectedInputCount);
            if (!shouldProceed)
                return;
        }

        var deletedAttachmentRows = 0;

        using (var tx = App.Db.Connection.BeginTransaction())
        {
            foreach (var tagId in tagIds)
            {
                using (var deleteAttachmentsCmd = App.Db.Connection.CreateCommand())
                {
                    deleteAttachmentsCmd.Transaction = tx;
                    deleteAttachmentsCmd.CommandText =
                        @"DELETE FROM timestamped_input_with_source_data_tags
                        WHERE tag_id = $tag_id;";
                    deleteAttachmentsCmd.Parameters.AddWithValue("$tag_id", tagId);

                    deletedAttachmentRows += deleteAttachmentsCmd.ExecuteNonQuery();
                }

                using (var deleteTagCmd = App.Db.Connection.CreateCommand())
                {
                    deleteTagCmd.Transaction = tx;
                    deleteTagCmd.CommandText =
                        @"DELETE FROM tags
                        WHERE id = $tag_id;";
                    deleteTagCmd.Parameters.AddWithValue("$tag_id", tagId);

                    deleteTagCmd.ExecuteNonQuery();
                }
            }

            tx.Commit();
        }

        PopulateTagListBoxes();
        AttachTagsSidebarControl.RefreshAttachedTags();
        UpdateAttachTagButtonState();

        _notificationManager.Show(
            new Avalonia.Controls.Notifications.Notification(
                "Tags deleted",
                $"Deleted {deletedAttachmentRows} tags from {affectedInputCount} inputs.",
                Avalonia.Controls.Notifications.NotificationType.Success,
                TimeSpan.FromSeconds(3)));
    }

    private void DetachTagButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedBundle == null)
            throw new Exception("No bundle selected");

        if (Tab1ListBox == null)
            throw new Exception("Tab1ListBox was null");

        if (AttachTagsSidebarControl == null)
            throw new Exception("AttachTagsSidebarControl was null");

        if (_notificationManager == null)
            throw new Exception("_notificationManager was null");

        var tisId = _selectedBundle.Id;
        var deletedCount = 0;

        using var tx = App.Db.Connection.BeginTransaction();

        foreach (var item in Tab1ListBox.SelectedItems ?? throw new Exception("Tab1ListBox.SelectedItems was null"))
        {
            string tagName = item switch
            {
                string s => s,
                TagDisplay td => CombineTagParts(td),
                _ => throw new Exception("Unexpected tag item type")
            };

            using var getTagCmd = App.Db.Connection.CreateCommand();
            getTagCmd.Transaction = tx;
            getTagCmd.CommandText = @"SELECT id FROM tags WHERE name = $name;";
            getTagCmd.Parameters.AddWithValue("$name", tagName);

            var tagIdObj = getTagCmd.ExecuteScalar();
            if (tagIdObj == null)
                throw new Exception($"Tag not found: {tagName}");

            var tagId = (long)tagIdObj;

            using var deleteCmd = App.Db.Connection.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText =
                @"DELETE FROM timestamped_input_with_source_data_tags
                WHERE ti_sd_id = $tis
                    AND tag_id = $tag;";
            deleteCmd.Parameters.AddWithValue("$tis", tisId);
            deleteCmd.Parameters.AddWithValue("$tag", tagId);

            deletedCount += deleteCmd.ExecuteNonQuery();
        }

        tx.Commit();

        PopulateTagListBoxes();
        AttachTagsSidebarControl.RefreshAttachedTags();
        UpdateAttachTagButtonState();

        _notificationManager.Show(
            new Avalonia.Controls.Notifications.Notification(
                "Tags detached",
                $"Removed {deletedCount} tag(s)",
                Avalonia.Controls.Notifications.NotificationType.Success,
                TimeSpan.FromSeconds(3)));
    }

    private List<string> LoadTagNames()
    {
        var tags = new List<string>();

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"SELECT name FROM tags ORDER BY name ASC;";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    private void PopulateTagListBoxes()
    {
        var tags = LoadTagNames();

        if (tags.Count == 0)
        {
            tags.Add("[no tags present]");
        }

        _allTagsTab1 = new List<string>(tags);
        _allTagsTab2 = new List<string>(tags);

        ApplyFilterTab1(Tab1TextBox?.Text ?? "");
        ApplyFilterTab2(Tab2TextBox?.Text ?? "");

        if (SearchExportPage == null)
            throw new Exception("SearchExportPage was null");

        SearchExportPage.SetTags(tags);
    }

    private void InsertTag(string rawText)
    {
        var name = rawText?.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            using var cmd = App.Db.Connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO tags (name) VALUES ($name);";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.ExecuteNonQuery();
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            // SQLITE_CONSTRAINT = 19
            if (ex.SqliteErrorCode == 19)
            {
                System.Console.WriteLine($"Duplicate tag ignored: \"{name}\"");
            }
            else
            {
                throw;
            }
        }
    }

    private void Tab1AddTagButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var text = Tab1TextBox.Text;
        if (text is null)
            throw new Exception("Tab1TextBox.Text was null");
        InsertTag(text);
        Tab1TextBox.Text = "";
        PopulateTagListBoxes();
    }

    private void Tab2AddTagButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var text = Tab2TextBox.Text;
        if (text is null)
            throw new Exception("Tab2TextBox.Text was null");
        InsertTag(text);
        Tab2TextBox.Text = "";
        PopulateTagListBoxes();
    }

    private HashSet<string> ReadAttachedTagNamesForSelectedBundle()
    {
        var attachedTagNames = new HashSet<string>(StringComparer.Ordinal);

        if (_selectedBundle == null)
            return attachedTagNames;

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText =
            @"SELECT t.name
            FROM timestamped_input_with_source_data_tags tisdt
            JOIN tags t ON t.id = tisdt.tag_id
            WHERE tisdt.ti_sd_id = $ti_sd_id;";
        cmd.Parameters.AddWithValue("$ti_sd_id", _selectedBundle.Id);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            if (reader.IsDBNull(0))
                throw new Exception("tags.name was null");

            attachedTagNames.Add(reader.GetString(0));
        }

        return attachedTagNames;
    }

    private List<TagDisplay> FilterTags(List<string> source, string filter)
    {
        var trimmed = filter.Trim();
        var attachedTagNames = ReadAttachedTagNamesForSelectedBundle();

        var result = new List<TagDisplay>();

        foreach (var tag in source)
        {
            if (trimmed.Length == 0 ||
                tag.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new TagDisplay(tag, trimmed, attachedTagNames.Contains(tag)));
            }
        }

        return result;
    }

    private void ApplyFilterTab1(string text)
    {
        Tab1ListBox.ItemsSource = FilterTags(_allTagsTab1, text);
    }

    private void ApplyFilterTab2(string text)
    {
        Tab2ListBox.ItemsSource = FilterTags(_allTagsTab2, text);
    }

    private void Tab1TextBox_OnTextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        var text = Tab1TextBox.Text ?? "";
        ApplyFilterTab1(text);

        if (Tab1AddTagButton == null)
            throw new Exception("Tab1AddTagButton was null");

        Tab1AddTagButton.IsEnabled = !string.IsNullOrWhiteSpace(text);
    }

    private void Tab2TextBox_OnTextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        var text = Tab2TextBox.Text ?? "";
        ApplyFilterTab2(text);
    }

    public static List<TagDisplayPart> SplitForHighlight(string tag, string filter, bool isAttached)
    {
        var result = new List<TagDisplayPart>();

        if (string.IsNullOrEmpty(filter))
        {
            result.Add(new TagDisplayPart(tag, false, isAttached));
            return result;
        }

        var index = 0;

        while (true)
        {
            var matchIndex = tag.IndexOf(filter, index, StringComparison.OrdinalIgnoreCase);

            if (matchIndex < 0)
            {
                result.Add(new TagDisplayPart(tag.Substring(index), false, isAttached));
                break;
            }

            if (matchIndex > index)
            {
                result.Add(new TagDisplayPart(tag.Substring(index, matchIndex - index), false, isAttached));
            }

            result.Add(new TagDisplayPart(tag.Substring(matchIndex, filter.Length), true, isAttached));

            index = matchIndex + filter.Length;
        }

        return result;
    }

    private void ChooseBundleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var items = ReadBundleSelectorItems();

        var win = new BundleSelectorWindow(items);

        win.BundleSelected += item =>
        {
            _selectedBundle = item;
            UpdateAttachTagButtonState();
            ApplyFilterTab1(Tab1TextBox?.Text ?? "");

            SelectedBundleTextBox.Text = item.SummaryText;

            SelectedBundle_Time.Text = item.CreatedUtc;
            SelectedBundle_InputPreview.Text = item.InputPreview;
            SelectedBundle_SourcePreview.Text = item.SourcePreview;
        };

        win.Show();
    }

    private void ClearBundleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _selectedBundle = null;
        UpdateAttachTagButtonState();
        ApplyFilterTab1(Tab1TextBox?.Text ?? "");

        SelectedBundleTextBox.Text = "";

        SelectedBundle_Time.Text = "";
        SelectedBundle_InputPreview.Text = "";
        SelectedBundle_SourcePreview.Text = "";
    }

    private List<TimestampedInputSourceDataRow> ReadBundleSelectorItems()
    {
        var rows = new List<TimestampedInputSourceDataRow>();

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText =
            @"SELECT
                tis.id,
                tis.timestamped_input_id,
                tis.source_data_id,
                tis.created_utc,
                te.input_text,
                COALESCE(sdb.source_data_summary, '')
            FROM timestamped_input_with_source_data tis
            JOIN timestamped_input ti ON ti.id = tis.timestamped_input_id
            JOIN text_entries te ON te.id = ti.text_entry_id
            LEFT JOIN (
                SELECT
                    source_data_block_id,
                    group_concat(key || ': ' || COALESCE(value, ''), '; ') AS source_data_summary
                FROM source_data_block_rows
                GROUP BY source_data_block_id
            ) sdb ON sdb.source_data_block_id = tis.source_data_id
            ORDER BY tis.created_utc DESC
            LIMIT 50;";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var tisId = reader.GetInt64(0);
            var ti = reader.GetInt64(1);
            var sd = reader.GetInt64(2);
            var tsRaw = reader.GetString(3);
            var ts = DateTime.Parse(tsRaw).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var text = reader.GetString(4);
            var sourceText = reader.IsDBNull(5) ? "" : reader.GetString(5);

            var firstLineEnd = text.IndexOfAny(new[] { '\r', '\n' });
            var inputPreview = firstLineEnd >= 0 ? text.Substring(0, firstLineEnd) + " …" : text;

            var sourceFirstLineEnd = sourceText.IndexOfAny(new[] { '\r', '\n' });
            var sourcePreview = sourceFirstLineEnd >= 0 ? sourceText.Substring(0, sourceFirstLineEnd) + " …" : sourceText;

            rows.Add(new TimestampedInputSourceDataRow
            {
                Id = tisId,
                TimestampedInputId = ti,
                SourceDataId = sd,
                CreatedUtc = ts,
                InputPreview = inputPreview,
                SourcePreview = sourcePreview,
                SummaryText = $"{ts} | {inputPreview}"
            });
        }

        return rows;
    }

}

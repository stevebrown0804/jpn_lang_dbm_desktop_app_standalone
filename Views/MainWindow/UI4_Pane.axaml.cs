// UI4_Pane.axaml.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace jpn_lang_dbm_desktop_app;

public partial class UI4_Pane : UserControl
{
    private List<string> _allTags = new();

    public UI4_Pane()
    {
        InitializeComponent();
        TagListBox.SelectionChanged += TagListBox_SelectionChanged;
        RefreshAggregateWordCount();
    }

    public void SetTags(List<string> tags)
    {
        _allTags = new List<string>(tags);
        ApplyTagFilter(TagFilterTextBox?.Text ?? "");
        RefreshAggregateWordCount();
    }

    private void TagFilterTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyTagFilter(TagFilterTextBox?.Text ?? "");
    }

    private void TagListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshAggregateWordCount();
    }

    private void ApplyTagFilter(string text)
    {
        var trimmed = text.Trim();
        var result = new List<TagDisplay>();

        foreach (var tag in _allTags)
        {
            if (trimmed.Length == 0 ||
                tag.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new TagDisplay(tag, trimmed, false));
            }
        }

        TagListBox.ItemsSource = result;
    }

    private void RefreshAggregateWordCount()
    {
        if (AggregateWordCountTextBox == null)
            throw new Exception("AggregateWordCountTextBox was null");

        var selectedTagNames = ReadSelectedTagNames();
        var selectedTagIds = ReadSelectedTagIds(selectedTagNames);
        var aggregate = new Dictionary<string, int>(StringComparer.Ordinal);

        using var cmd = App.Db.Connection.CreateCommand();

        if (selectedTagIds.Count == 0)
        {
            cmd.CommandText =
                @"SELECT ti.word_count_json
                FROM timestamped_input_with_source_data tis
                JOIN timestamped_input ti ON ti.id = tis.timestamped_input_id
                ORDER BY tis.id ASC;";
        }
        else
        {
            var tagIdParamNames = new List<string>();

            for (var i = 0; i < selectedTagIds.Count; i++)
            {
                var paramName = "$tag" + i;
                tagIdParamNames.Add(paramName);
                cmd.Parameters.AddWithValue(paramName, selectedTagIds[i]);
            }

            cmd.Parameters.AddWithValue("$selected_tag_count", selectedTagIds.Count);

            cmd.CommandText =
                @"SELECT ti.word_count_json
                FROM timestamped_input_with_source_data tis
                JOIN timestamped_input ti ON ti.id = tis.timestamped_input_id
                WHERE tis.id IN (
                    SELECT tisdt.ti_sd_id
                    FROM timestamped_input_with_source_data_tags tisdt
                    WHERE tisdt.tag_id IN (" + string.Join(", ", tagIdParamNames) + @")
                    GROUP BY tisdt.ti_sd_id
                    HAVING COUNT(DISTINCT tisdt.tag_id) = $selected_tag_count
                )
                ORDER BY tis.id ASC;";
        }

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            if (reader.IsDBNull(0))
                throw new Exception("timestamped_input.word_count_json was null");

            var json = reader.GetString(0);

            Dictionary<string, int>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            }
            catch (Exception ex)
            {
                throw new Exception("Could not parse timestamped_input.word_count_json", ex);
            }

            if (parsed == null)
                throw new Exception("Deserialized word_count_json was null");

            foreach (var kvp in parsed)
            {
                if (aggregate.TryGetValue(kvp.Key, out var existing))
                    aggregate[kvp.Key] = existing + kvp.Value;
                else
                    aggregate[kvp.Key] = kvp.Value;
            }
        }

        AggregateWordCountTextBox.Text = FormatWordCounts(aggregate);
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

    private static string CombineTagParts(TagDisplay td)
    {
        if (td.Parts == null)
            throw new Exception("TagDisplay.Parts was null");

        var sb = new StringBuilder();

        foreach (var part in td.Parts)
            sb.Append(part.Text);

        return sb.ToString();
    }

    private List<string> ReadSelectedTagNames()
    {
        if (TagListBox == null)
            throw new Exception("TagListBox was null");

        var selectedItems = TagListBox.SelectedItems ?? throw new Exception("TagListBox.SelectedItems was null");
        var result = new List<string>();

        foreach (var item in selectedItems)
        {
            if (item is not TagDisplay td)
                throw new Exception("TagListBox selected item was not a TagDisplay");

            result.Add(CombineTagParts(td));
        }

        return result;
    }

    private static List<long> ReadSelectedTagIds(List<string> selectedTagNames)
    {
        var result = new List<long>();

        foreach (var tagName in selectedTagNames)
        {
            using var cmd = App.Db.Connection.CreateCommand();
            cmd.CommandText = @"SELECT id FROM tags WHERE name = $name;";
            cmd.Parameters.AddWithValue("$name", tagName);

            var obj = cmd.ExecuteScalar();
            if (obj == null)
                throw new Exception("Tag not found: " + tagName);

            result.Add(Convert.ToInt64(obj));
        }

        return result;
    }

    private async void ExportButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            throw new Exception("TopLevel.GetTopLevel(this) returned null");

        if (AggregateWordCountTextBox == null)
            throw new Exception("AggregateWordCountTextBox was null");

        var suggestedFileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_aggregate_word_count";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Choose export file",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "csv",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV file")
                    {
                        Patterns = new[] { "*.csv" },
                        MimeTypes = new[] { "text/csv" }
                    }
                }
            });

        if (file == null)
        {
            return;
        }

        var path = file.TryGetLocalPath();
        if (path == null)
            throw new Exception("Save picker did not return a local path.");

        var exportText = (AggregateWordCountTextBox.Text ?? "")
            .Replace("|", "\t");

        await File.WriteAllTextAsync(path, exportText, Encoding.UTF8);
    }

}

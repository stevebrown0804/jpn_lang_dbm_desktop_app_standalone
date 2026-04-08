// MainWindow.UI2.Sidebar.cs

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace jpn_lang_dbm_desktop_app;

public partial class MainWindow : Window
{
    private void TimestampedInputSourceAttachTagsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TimestampedInputSourceDataGrid?.SelectedItem is not TimestampedInputSourceDataRow row)
            throw new InvalidOperationException("Attach tags pressed but no TI+SD row was selected.");

        _selectedBundle = row;

        SelectedBundleTextBox.Text = row.SummaryText;
        SelectedBundle_Time.Text = row.CreatedUtc;
        SelectedBundle_InputPreview.Text = row.InputPreview;
        SelectedBundle_SourcePreview.Text = row.SourcePreview;

        UpdateAttachTagButtonState();
        ApplyFilterTab1(Tab1TextBox?.Text ?? "");

        ImportTextPage.IsVisible = false;
        SourceMetadataPage.IsVisible = false;
        AttachTagsPage.IsVisible = true;

        ApplyUI2RightPaneStateToVisibleUI2Page();

        if (AttachTagsTabControl == null)
            throw new InvalidOperationException("AttachTagsTabControl not found.");

        AttachTagsTabControl.SelectedIndex = 0;
    }

    private void InitializeUI2RightPaneSizing()
    {
        _UI2RightPaneExpandedWidthPx = ReadUI2ExpandedWidthPxOrNull();

        if (_UI2RightPaneExpandedWidthPx == null)
        {
            var importWidth = ReadImportExpandedWidthPxOrNull();
            if (importWidth != null)
                _UI2RightPaneExpandedWidthPx = importWidth;
        }

        ApplyUI2RightPaneStateToVisibleUI2Page();
    }

    private List<TimestampedInputSourceDataRow> ReadTimestampedInputSourceDataSidebarRows()
    {
        return ReadBundleSelectorItems();
    }

    private void PopulateTimestampedInputSourceDataSidebar()
    {
        var rows = ReadTimestampedInputSourceDataSidebarRows();
        TimestampedInputSourceDataGrid.ItemsSource = rows;
        AttachTagsSidebarControl.SetBundleItems(ReadBundleSelectorItems());
    }

    private void UI2_RightPaneRoot_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var root = GetVisibleUI2RightPaneRootOrNull();
        var splitter = GetVisibleUI2RightPaneSplitterOrNull();
        var col = GetVisibleUI2RightPaneColumnOrNull();

        if (root == null || splitter == null || col == null)
            return;

        if (_UI2RightPaneIsCollapsed)
            return;

        if (col.Width.GridUnitType == GridUnitType.Pixel && col.Width.Value <= 0)
            return;

        var w = root.Bounds.Width;
        if (double.IsNaN(w) || double.IsInfinity(w))
            return;

        var px = (int)Math.Round(w);
        if (px <= 0)
            return;

        if (_UI2RightPaneExpandedWidthPx != px)
        {
            _UI2RightPaneExpandedWidthPx = px;
            WriteUI2ExpandedWidthPx(px);
        }
    }

    private void ToggleUI2RightPane_OnClick(object? sender, RoutedEventArgs e)
    {
        var splitter = GetVisibleUI2RightPaneSplitterOrNull();
        var col = GetVisibleUI2RightPaneColumnOrNull();

        if (splitter == null || col == null)
            return;

        if (_UI2RightPaneIsCollapsed)
        {
            if (_UI2RightPaneExpandedWidthPx != null)
                col.Width = new GridLength(_UI2RightPaneExpandedWidthPx.Value);
            else
                col.Width = GridLength.Auto;

            splitter.IsVisible = true;
            _UI2RightPaneIsCollapsed = false;
            return;
        }

        if (col.Width.GridUnitType == GridUnitType.Pixel && col.Width.Value > 0)
        {
            var px = (int)Math.Round(col.Width.Value);
            if (px > 0)
            {
                _UI2RightPaneExpandedWidthPx = px;
                WriteUI2ExpandedWidthPx(px);
            }
        }

        col.Width = new GridLength(0);
        splitter.IsVisible = false;
        _UI2RightPaneIsCollapsed = true;
    }

    private void ApplyUI2RightPaneStateToVisibleUI2Page()
    {
        var splitter = GetVisibleUI2RightPaneSplitterOrNull();
        var col = GetVisibleUI2RightPaneColumnOrNull();

        if (splitter == null || col == null)
            return;

        if (_UI2RightPaneIsCollapsed)
        {
            col.Width = new GridLength(0);
            splitter.IsVisible = false;
            return;
        }

        if (_UI2RightPaneExpandedWidthPx != null)
        {
            col.Width = new GridLength(_UI2RightPaneExpandedWidthPx.Value);
            splitter.IsVisible = true;
            return;
        }

        splitter.IsVisible = col.Width.Value > 0;
    }

    private Grid? GetVisibleUI2RightPaneRootOrNull()
    {
        if (SourceMetadataPage.IsVisible)
            return UI2_RightPaneRoot_Source;

        if (AttachTagsPage.IsVisible)
            return UI2RightPaneRoot_Tags;

        return null;
    }

    private GridSplitter? GetVisibleUI2RightPaneSplitterOrNull()
    {
        if (SourceMetadataPage.IsVisible)
            return UI2_RightPaneSplitter_Source;

        if (AttachTagsPage.IsVisible)
            return UI2RightPaneSplitter_Tags;

        return null;
    }

    private ColumnDefinition? GetVisibleUI2RightPaneColumnOrNull()
    {
        if (SourceMetadataPage.IsVisible)
        {
            if (UI2_RightPaneSplitter_Source.Parent is not Grid g)
                throw new InvalidOperationException("Expected UI2_RightPaneSplitter_Source to be inside a Grid.");

            return g.ColumnDefinitions[2];
        }

        if (AttachTagsPage.IsVisible)
        {
            if (UI2RightPaneSplitter_Tags.Parent is not Grid g)
                throw new InvalidOperationException("Expected UI2RightPaneSplitter_Tags to be inside a Grid.");

            return g.ColumnDefinitions[2];
        }

        return null;
    }

    private int? ReadUI2ExpandedWidthPxOrNull()
    {
        const string key = "sidebar.UI2.expanded_width";

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

    private void WriteUI2ExpandedWidthPx(int px)
    {
        if (px <= 0)
            throw new InvalidOperationException("WriteUI2ExpandedWidthPx received non-positive width: " + px);

        const string key = "sidebar.UI2.expanded_width";

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

    private void SourceDataStashDeleteButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SourceDataBlockRowsGrid?.SelectedItem is not SourceDataBlockRowGridRow row)
            return;

        var id = row.Id;

        using var tx = App.Db.Connection.BeginTransaction();

        using (var cmd = App.Db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                DELETE FROM source_data_block_stash
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();

        LoadReusableSourceDataSummarySidebarOrThrow();
    }

    private void SourceDataBlockRowsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SourceDataStashDeleteButton == null)
            throw new InvalidOperationException("SourceDataStashDeleteButton not found.");

        if (SourceDataStashLoadButton == null)
            throw new InvalidOperationException("SourceDataStashLoadButton not found.");

        var hasSelection = SourceDataBlockRowsGrid?.SelectedItem is SourceDataBlockRowGridRow;

        SourceDataStashDeleteButton.IsEnabled = hasSelection;
        SourceDataStashLoadButton.IsEnabled = hasSelection;
    }

    private void TimestampedInputSourceDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TimestampedInputSourceDeleteButton == null)
            throw new InvalidOperationException("TimestampedInputSourceDeleteButton not found.");

        if (TimestampedInputSourceAttachTagsButton == null)
            throw new InvalidOperationException("TimestampedInputSourceAttachTagsButton not found.");

        var hasSelection = TimestampedInputSourceDataGrid?.SelectedItem is TimestampedInputSourceDataRow;

        TimestampedInputSourceDeleteButton.IsEnabled = hasSelection;
        TimestampedInputSourceAttachTagsButton.IsEnabled = hasSelection;
    }
    
    private void TimestampedInputSourceDeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TimestampedInputSourceDataGrid?.SelectedItem is not TimestampedInputSourceDataRow row)
            return;

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText =
            @"DELETE FROM timestamped_input_with_source_data
            WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", row.Id);
        cmd.ExecuteNonQuery();

        PopulateTimestampedInputSourceDataSidebar();

        TimestampedInputSourceDeleteButton.IsEnabled = false;
        TimestampedInputSourceAttachTagsButton.IsEnabled = false;
    }

    private void SourceDataStashLoadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SourceDataBlockRowsGrid?.SelectedItem is not SourceDataBlockRowGridRow row)
            throw new InvalidOperationException("Load pressed but no stash row selected.");

        var blockId = row.Id;

        // Clear existing editable key/value rows
        _templateKeyValueRows.Clear();

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT key, value
            FROM source_data_block_stash_rows
            WHERE source_data_block_id = $id
            ORDER BY display_order ASC;
        ";
        cmd.Parameters.AddWithValue("$id", blockId);

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var key = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
            var value = rdr.IsDBNull(1) ? "" : rdr.GetString(1);

            _templateKeyValueRows.Add(new TemplateKeyValueRow
            {
                LeftText = key,
                RightText = value
            });
        }

        // De-select the stash row for tidiness
        SourceDataBlockRowsGrid.SelectedItem = null;

        if (SourceDataStashLoadButton == null || SourceDataStashDeleteButton == null)
            throw new InvalidOperationException("Load/Delete buttons not found.");

        SourceDataStashLoadButton.IsEnabled = false;
        SourceDataStashDeleteButton.IsEnabled = false;
    }
    
}

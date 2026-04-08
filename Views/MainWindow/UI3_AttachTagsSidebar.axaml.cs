// AttachTagsSidebar.axaml.cs

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Microsoft.Data.Sqlite;

namespace jpn_lang_dbm_desktop_app;

public partial class AttachTagsSidebar : UserControl
{
    public event Action<long, string>? DetachRequested;

    public AttachTagsSidebar()
    {
        InitializeComponent();
        BundleListBox.SelectionChanged += BundleListBox_SelectionChanged;
    }

    public void SetBundleItems(List<TimestampedInputSourceDataRow> items)
    {
        BundleListBox.ItemsSource = items;
    }

    private void BundleListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshAttachedTags();
    }

    private void DetachMenuItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (BundleListBox.SelectedItem is not TimestampedInputSourceDataRow row)
            throw new Exception("BundleListBox.SelectedItem was not a TimestampedInputSourceDataRow");

        if (AttachedTagsListBox.SelectedItem is not string tagName)
            throw new Exception("AttachedTagsListBox.SelectedItem was not a string");

        DetachRequested?.Invoke(row.Id, tagName);
    }

    public void RefreshAttachedTags()
    {
        if (BundleListBox.SelectedItem == null)
        {
            AttachedTagsListBox.ItemsSource = new List<string>();
            return;
        }

        if (BundleListBox.SelectedItem is not TimestampedInputSourceDataRow row)
            throw new Exception("BundleListBox.SelectedItem was not a TimestampedInputSourceDataRow");

        AttachedTagsListBox.ItemsSource = ReadAttachedTagsForBundle(row.Id);
    }

    private static List<string> ReadAttachedTagsForBundle(long tiSdId)
    {
        var tags = new List<string>();

        using var cmd = App.Db.Connection.CreateCommand();
        cmd.CommandText =
            @"SELECT t.name
            FROM timestamped_input_with_source_data_tags tisdt
            JOIN tags t ON t.id = tisdt.tag_id
            WHERE tisdt.ti_sd_id = $ti_sd_id
            ORDER BY t.name ASC;";
        cmd.Parameters.AddWithValue("$ti_sd_id", tiSdId);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            if (reader.IsDBNull(0))
                throw new Exception("tags.name was null");

            tags.Add(reader.GetString(0));
        }

        return tags;
    }
}
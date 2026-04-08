// BundleSelectorWindow.axaml.cs

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace jpn_lang_dbm_desktop_app;

public partial class BundleSelectorWindow : Window
{
    public event Action<TimestampedInputSourceDataRow>? BundleSelected;

    public BundleSelectorWindow()
    {
        InitializeComponent();
    }

    public BundleSelectorWindow(List<TimestampedInputSourceDataRow> items)
    {
        InitializeComponent();
        BundleList.ItemsSource = items;
        BundleList.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BundleList.SelectedItem is TimestampedInputSourceDataRow item)
            BundleSelected?.Invoke(item);
    }
}

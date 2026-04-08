//SourceInputSelectorWindow.axaml.cs

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace jpn_lang_dbm_desktop_app_standalone;

public partial class SourceInputSelectorWindow : Window
{
    public event Action<SourceMetadataDropdownItem>? SourceSelected;

    public SourceInputSelectorWindow()
    {
        InitializeComponent();
    }
    
    public SourceInputSelectorWindow(List<SourceMetadataDropdownItem> items)
    {
        InitializeComponent();

        SourceList.ItemsSource = items;
        SourceList.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SourceList.SelectedItem is SourceMetadataDropdownItem item)
            SourceSelected?.Invoke(item);
    }
}

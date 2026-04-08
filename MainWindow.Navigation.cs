// MainWindow.Navigation.cs

using System;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace jpn_lang_dbm_desktop_app_standalone;

public partial class MainWindow : Window
{
    private void NavView_SelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is not NavigationViewItem nvi)
            return;

        var tag = nvi.Tag as string ?? "";
        ApplyNavigationTagSelection(tag);
    }

    private void ApplyNavigationTagSelection(string tag)
    {
        ImportTextPage.IsVisible = tag == "import";
        SourceMetadataPage.IsVisible = tag == "source";
        AttachTagsPage.IsVisible = tag == "tags";
        SearchExportPage.IsVisible = tag == "search";

        if (SourceMetadataPage.IsVisible)
        {
            PopulateTemplateComboBoxOrThrow();
            LoadReusableSourceDataSummarySidebarOrThrow();

            if (AttachSourceDataToInputButton == null)
                throw new InvalidOperationException("AttachSourceDataToInputButton not found.");

            AttachSourceDataToInputButton.IsEnabled = _selectedTimestampedInputId != null;
        }

        ApplyUI2RightPaneStateToVisibleUI2Page();
    }

    private void SelectNavTag(string tagToSelect)
    {
        if (NavView == null)
            throw new InvalidOperationException("NavView not found. Check the XAML x:Name.");

        foreach (var obj in NavView.MenuItems)
        {
            if (obj is not NavigationViewItem nvi)
                continue;

            var tag = nvi.Tag as string ?? "";
            if (tag == tagToSelect)
            {
                NavView.SelectedItem = nvi;
                return;
            }
        }

        throw new InvalidOperationException("Could not find NavigationViewItem with Tag == '" + tagToSelect + "'.");
    }
}

// MainWindow.axaml.cs

using System;
using System.Globalization;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace jpn_lang_dbm_desktop_app;

public sealed class SourceDataBlockGridRow
{
    public long Id { get; set; }
    public string CreatedUtc { get; set; } = "";
}

public sealed class SourceDataBlockRowGridRow
{
    public long Id { get; set; }
    public string SummaryText { get; set; } = "";
}

public sealed class TemplateKeyValueRow
{
    public string LeftText { get; set; } = "";
    public string RightText { get; set; } = "";
}

public sealed class TimestampedInputSourceDataRow
{
    public long Id { get; set; }
    public long TimestampedInputId { get; set; }
    public long SourceDataId { get; set; }
    public string CreatedUtc { get; set; } = "";
    public string InputPreview { get; set; } = "";
    public string SourcePreview { get; set; } = "";
    public string SummaryText { get; set; } = "";
}

public class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b
            ? Brushes.Yellow
            : Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b
            ? FontWeight.Bold
            : FontWeight.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public partial class MainWindow : Window
{
    private const string BackendBaseUrl = "http://127.0.0.1:50000";
    private static readonly HttpClient _http = new HttpClient();
    private int? _importRightPaneExpandedWidthPx;
    private int? _UI2RightPaneExpandedWidthPx;
    private bool _UI2RightPaneIsCollapsed;
    private long? _selectedTimestampedInputId;

    private readonly System.Collections.ObjectModel.ObservableCollection<TemplateKeyValueRow> _templateKeyValueRows
        = new System.Collections.ObjectModel.ObservableCollection<TemplateKeyValueRow>();
    private readonly System.Collections.ObjectModel.ObservableCollection<SourceDataBlockRowGridRow> _sourceDataBlockSummaryRows
    = new System.Collections.ObjectModel.ObservableCollection<SourceDataBlockRowGridRow>();

    //Reminder for ChatGPT: The constructor should be the only function in this file.
    //  That may change in the future, but only at my specific request.
    public MainWindow()
    {
        InitializeComponent();

        _notificationManager = new Avalonia.Controls.Notifications.WindowNotificationManager(this)
    {
        Position = Avalonia.Controls.Notifications.NotificationPosition.BottomRight,
        MaxItems = 3  // <--?  this seems kinda arbitrary.  NOTE TO SELF: Evaluate this, at some point.
    };

        if (TemplateKeyValueRowsItemsControl == null)
            throw new InvalidOperationException("TemplateKeyValueRowsItemsControl not found. Check the XAML x:Name.");

        TemplateKeyValueRowsItemsControl.ItemsSource = _templateKeyValueRows;
        if (SourceDataBlockRowsGrid == null)
            throw new InvalidOperationException("SourceDataBlockRowsGrid not found. Check the XAML x:Name.");

        // UI #1
        SourceDataBlockRowsGrid.ItemsSource = _sourceDataBlockSummaryRows;
        LoadReusableSourceDataSummarySidebarOrThrow();
        SourceDataBlockRowsGrid.SelectionChanged += SourceDataBlockRowsGrid_SelectionChanged;        
        InitializeImportRightPaneSizing();
        InitializeUI2RightPaneSizing();
        
        // UI #2
        PopulateTimestampedInputSourceDataSidebar();
        TimestampedInputSourceDataGrid.SelectionChanged += TimestampedInputSourceDataGrid_SelectionChanged;
        LoadTimestampedInputSidebar();
        
        // UI #3        
        this.Opened += (_, __) =>
        {
            PopulateTagListBoxes();
        };
        AttachTagsSidebarControl.DetachRequested += (tiSdId, tagName) =>
        {
            using var getTagCmd = App.Db.Connection.CreateCommand();
            getTagCmd.CommandText = @"SELECT id FROM tags WHERE name = $name;";
            getTagCmd.Parameters.AddWithValue("$name", tagName);

            var tagIdObj = getTagCmd.ExecuteScalar();
            if (tagIdObj == null)
                throw new Exception($"Tag not found: {tagName}");

            using var deleteCmd = App.Db.Connection.CreateCommand();
            deleteCmd.CommandText =
                @"DELETE FROM timestamped_input_with_source_data_tags
                WHERE ti_sd_id = $ti_sd_id
                    AND tag_id = $tag_id;";
            deleteCmd.Parameters.AddWithValue("$ti_sd_id", tiSdId);
            deleteCmd.Parameters.AddWithValue("$tag_id", (long)tagIdObj);
            deleteCmd.ExecuteNonQuery();

            PopulateTagListBoxes();
            AttachTagsSidebarControl.RefreshAttachedTags();
            UpdateAttachTagButtonState();
        };
    }
}

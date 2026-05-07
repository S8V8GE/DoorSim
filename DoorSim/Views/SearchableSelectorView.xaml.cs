using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace DoorSim.Views;

// Reusable searchable selector control.
//
// This control deliberately avoids editable ComboBox behaviour.
//
// Important design rule: typing in the search box only filters the visible result list. It does NOT change SelectedItem.
//
// SelectedItem only changes when the user clicks a result.
public partial class SearchableSelectorView : UserControl
{
    private readonly CollectionViewSource _viewSource = new();

    public SearchableSelectorView()
    {
        InitializeComponent();

        _viewSource.Filter += ViewSource_Filter;

        UpdateSelectedText();
    }

    /*
      #############################################################################
                              Dependency Properties
      #############################################################################
    */

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(SearchableSelectorView),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(SearchableSelectorView),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedItemChanged));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(SearchableSelectorView),
            new PropertyMetadata(string.Empty));

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(
            nameof(PlaceholderText),
            typeof(string),
            typeof(SearchableSelectorView),
            new PropertyMetadata("Select an item"));

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /*
      #############################################################################
                              Property Change Handlers
      #############################################################################
    */

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableSelectorView selector)
            return;

        selector._viewSource.Source = e.NewValue;

        // Important:
        // When the source collection changes, CollectionViewSource can provide a new View instance.
        // Re-attach the ListBox to the current view so refreshed door lists actually appear in the popup.
        selector.ResultsListBox.ItemsSource = selector._viewSource.View;

        selector._viewSource.View?.Refresh();
        selector.UpdateNoResultsText();
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableSelectorView selector)
            return;

        selector.UpdateSelectedText();
    }

    /*
      #############################################################################
                              Popup Behaviour
      #############################################################################
    */

    private void SelectorButton_Click(object sender, RoutedEventArgs e)
    {
        SelectorPopup.IsOpen = true;
    }

    private void SelectorPopup_Opened(object sender, EventArgs e)
    {
        // Start each open with a clean search so the full list is visible.
        SearchTextBox.Text = string.Empty;

        _viewSource.View?.Refresh();
        UpdateNoResultsText();

        // Focus after the popup has rendered.
        Dispatcher.BeginInvoke(() =>
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void SelectorPopup_Closed(object sender, EventArgs e)
    {
        // Clear search when the popup closes.
        SearchTextBox.Text = string.Empty;

        _viewSource.View?.Refresh();
        UpdateNoResultsText();
    }

    /*
      #############################################################################
                              Search and Filtering
      #############################################################################
    */

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewSource.View?.Refresh();
        UpdateNoResultsText();
    }

    private void ViewSource_Filter(object sender, FilterEventArgs e)
    {
        var searchText = SearchTextBox?.Text?.Trim();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            e.Accepted = true;
            return;
        }

        var displayText = GetDisplayText(e.Item);

        e.Accepted = displayText.Contains(
            searchText,
            StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateNoResultsText()
    {
        if (_viewSource.View == null)
            return;

        var hasAnyVisibleItems = _viewSource.View.Cast<object>().Any();

        NoResultsTextBlock.Visibility = hasAnyVisibleItems
            ? Visibility.Collapsed
            : Visibility.Visible;

        ResultsListBox.Visibility = hasAnyVisibleItems
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /*
      #############################################################################
                              Selection Handling
      #############################################################################
    */

    private void ResultItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item)
            return;

        SelectedItem = item.DataContext;

        SelectorPopup.IsOpen = false;
        Keyboard.ClearFocus();

        e.Handled = true;
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectorPopup.IsOpen = false;
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && ResultsListBox.Items.Count > 0)
        {
            SelectedItem = ResultsListBox.Items[0];

            SelectorPopup.IsOpen = false;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    /*
      #############################################################################
                              Display Helpers
      #############################################################################
    */

    private void UpdateSelectedText()
    {
        var selectedText = GetDisplayText(SelectedItem);

        SelectedTextBlock.Text = selectedText;

        PlaceholderTextBlock.Visibility = string.IsNullOrWhiteSpace(selectedText)
            ? Visibility.Visible
            : Visibility.Collapsed;

        SelectedTextBlock.Visibility = string.IsNullOrWhiteSpace(selectedText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private string GetDisplayText(object? item)
    {
        if (item == null)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(DisplayMemberPath))
            return item.ToString() ?? string.Empty;

        var property = item.GetType().GetProperty(
            DisplayMemberPath,
            BindingFlags.Public | BindingFlags.Instance);

        if (property == null)
            return item.ToString() ?? string.Empty;

        return property.GetValue(item)?.ToString() ?? string.Empty;
    }

}
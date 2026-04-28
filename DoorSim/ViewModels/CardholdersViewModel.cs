using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DoorSim.Models;

namespace DoorSim.ViewModels;

// ViewModel for the Cardholders panel.
//
// Responsible for:
// - Holding cardholders loaded from SQL
// - Sorting them alphabetically
// - Filtering them using the search box
// - Providing data to the CardholdersView
public partial class CardholdersViewModel : ObservableObject
{
    // Full cardholder list loaded from SQL
    private List<Cardholder> _allCardholders = new List<Cardholder>();

    // Cardholders currently shown in the UI after filtering
    [ObservableProperty]
    private ObservableCollection<Cardholder> visibleCardholders = new ObservableCollection<Cardholder>();

    // Counts the number of cardholders currently shown in results table
    [ObservableProperty]
    private int cardholderCount;

    // Text entered into the search box
    [ObservableProperty]
    private string searchText = string.Empty;

    // Message shown when no cardholders are available
    [ObservableProperty]
    private string emptyMessage = "No cardholders found, please create some in Security Center.";

    // True when there are cardholders to show
    [ObservableProperty]
    private bool hasCardholders;

    // Loads cardholders into the ViewModel and sorts them alphabetically
    public void LoadCardholders(IEnumerable<Cardholder> cardholders)
    {
        _allCardholders = cardholders
            .OrderBy(c => c.CardholderName)
            .ToList();

        CardholderCount = _allCardholders.Count;

        ApplyFilter();
    }

    // Automatically called whenever SearchText changes
    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    // Filters cardholders based on the current search text
    private void ApplyFilter()
    {
        var filtered = _allCardholders;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = _allCardholders
                .Where(c =>
                    c.CardholderName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    c.RawCredential.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        VisibleCardholders = new ObservableCollection<Cardholder>(filtered);
        HasCardholders = VisibleCardholders.Any();
    }
}
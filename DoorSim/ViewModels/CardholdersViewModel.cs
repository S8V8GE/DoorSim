using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DoorSim.Models;

namespace DoorSim.ViewModels;

// ViewModel for the Cardholders panel.
//
// Responsibilities:
//      - Keep the full cardholder list loaded from SQL.
//      - Sort cardholders alphabetically.
//      - Expose the filtered cardholder list used by CardholdersView.
//      - Apply search filtering by name or credential value.
//
// Drag/drop behaviour is handled by CardholdersView.xaml.cs because it is UI-specific. This ViewModel only owns the cardholder data and filtering.
public partial class CardholdersViewModel : ObservableObject
{
    /*
      #############################################################################
                                      Source Data
      #############################################################################
    */

    // Full cardholder list loaded from SQL
    // This list is kept separate from VisibleCardholders so search filtering can be reapplied without needing to query SQL again.
    private List<Cardholder> _allCardholders = new List<Cardholder>();


    /*
      #############################################################################
                              UI-Bound Cardholder State
      #############################################################################
    */

    // Cardholders currently shown in the UI after search filtering
    [ObservableProperty]
    private ObservableCollection<Cardholder> visibleCardholders = new ObservableCollection<Cardholder>();

    // Number of cardholders (well... unique credentials actually) loaded from SQL.
    // This currently shows the total configured credential count, not the filtered result count.
    [ObservableProperty]
    private int cardholderCount;

    // True when the filtered list contains at least one visible cardholder.
    [ObservableProperty]
    private bool hasCardholders;

    // Message shown when no cardholders are available
    [ObservableProperty]
    private string emptyMessage = "No cardholders found. Please create some in Security Center, or ensure this simulator is running on the same server as the SQL instance used.";


    /*
      #############################################################################
                                  Search State
      #############################################################################
    */

    // Text entered into the search box.
    // Filtering is applied automatically whenever this value changes.
    [ObservableProperty]
    private string searchText = string.Empty;


    /*
      #############################################################################
                                  Data Loading
      #############################################################################
    */

    // Replaces the full cardholder list from SQL, sorts it alphabetically, then reapplies the current search filter.
    // This means the trainer can keep a search term entered while the background refresh updates the underlying cardholder list.
    public void LoadCardholders(IEnumerable<Cardholder> cardholders)
    {
        _allCardholders = cardholders
            .OrderBy(c => c.CardholderName)
            .ToList();

        CardholderCount = _allCardholders.Count;

        ApplyFilter();
    }


    /*
      #############################################################################
                          Generated Property Change Hooks 
      #############################################################################
    */

    // Automatically called whenever SearchText changes
    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }


    /*
      #############################################################################
                                Filtering Helpers
      #############################################################################
    */

    // Filters cardholders based on the current search text
    private void ApplyFilter()
    {
        var filtered = _allCardholders;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = _allCardholders
                .Where(c =>
                    c.CardholderName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    c.RawCredential.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    c.TrimmedCredential.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        VisibleCardholders = new ObservableCollection<Cardholder>(filtered);
        HasCardholders = VisibleCardholders.Any();
    }

}
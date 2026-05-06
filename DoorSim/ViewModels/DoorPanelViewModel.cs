using CommunityToolkit.Mvvm.ComponentModel;
using DoorSim.Models;
using System.Collections.ObjectModel;

namespace DoorSim.ViewModels;

// ViewModel for one side of Two Door View.
//
// Each instance owns:
//      - the selector list shown above one door panel,
//      - the selected door for that side,
//      - a DoorsViewModel instance used by the reusable DoorPanelView.
//
// Important distinction:
//      - DoorPanelViewModel.SelectedDoor belongs to the dropdown selector.
//      - DoorPanelViewModel.DoorState.SelectedDoor belongs to the live visual panel.
//
// Keeping those responsibilities separate prevents the slow door-list refresh from replacing the live panel object and causing UI flicker.
public partial class DoorPanelViewModel : ObservableObject
{
    /*
      #############################################################################
                               Selector Display State
      #############################################################################
    */

    // Friendly label for this side of Two Door View, such as "Left Door" or "Right Door".
    // Currently not shown in the selector title, but kept for future UI labelling if needed.
    public string PanelTitle { get; }

    // Doors available in this side's dropdown.
    // In Two Door View this list may be filtered by TwoDoorViewModel so the same door cannot be selected on both sides at once.
    public ObservableCollection<SoftwireDoor> Doors { get; } = new ObservableCollection<SoftwireDoor>();

    // Text shown in the selector bar.
    public string DoorSelectorTitle => $"Select a door ({Doors.Count})";

    // The door selected for this specific panel.
    // In Two Door View, the left and right panels will each have their own SelectedDoor instead of sharing DoorsViewModel.SelectedDoor.
    [ObservableProperty]
    private SoftwireDoor? selectedDoor;


    /*
      #############################################################################
                               Live Door Panel State
      #############################################################################
    */

    // Full state model used by DoorPanelView.
    // DoorPanelView binds to this DoorsViewModel because it exposes all calculated device state: images, status text, colours, visibility, tooltips, and temporary access feedback.
    // Do not refresh this with DoorState.LoadDoors(...) from here. The live panel state is updated by MainViewModel polling. Replacing it during dropdown-list refreshes can cause flicker.
    public DoorsViewModel DoorState { get; } = new();


    /*
      #############################################################################
                               Refresh Guard State
      #############################################################################
    */

    // True while LoadDoors is refreshing the dropdown list.
    // During refresh, SelectedDoor may be reassigned to a new object with the same Id.
    // That should keep the ComboBox aligned with the refreshed list, but should not be treated as a user selection change.
    private bool _isLoadingDoors;


    /*
      #############################################################################
                            Selection Change Handling
      #############################################################################
    */

    // Called by CommunityToolkit.Mvvm when SelectedDoor changes.
    // User-driven selection changes should update DoorState.SelectedDoor so the visual panel follows the dropdown.
    // Refresh-driven changes are ignored here because LoadDoors handles final synchronisation after the dropdown list has been rebuilt.
    partial void OnSelectedDoorChanged(SoftwireDoor? value)
    {
        if (_isLoadingDoors)
            return;

        DoorState.SelectedDoor = value;
    }


    /*
      #############################################################################
                                  Constructor
      #############################################################################
    */

    public DoorPanelViewModel(string panelTitle)
    {
        PanelTitle = panelTitle;
    }


    /*
      #############################################################################
                                  Door loading
      #############################################################################
    */

    // Refreshes the doors available in this panel's selector.
    // This method intentionally refreshes the selector list only. It does not call DoorState.LoadDoors(...), because DoorState is the live state object used by DoorPanelView and is updated by polling.
    //
    // Behaviour:
    //      - Preserve the selected door by Id if it still exists in the refreshed list.
    //      - Clear selection if the selected door disappears.
    //      - Do not auto-select the first door; this allows the dropdown placeholder text ("Select a door") to remain visible.
    //      - Only update DoorState.SelectedDoor if the selected door has changed.
    public void LoadDoors(IEnumerable<SoftwireDoor> doors)
    {
        var selectedDoorId = SelectedDoor?.Id;

        var orderedDoors = doors
            .OrderBy(d => d.Name)
            .ToList();

        _isLoadingDoors = true;

        try
        {
            Doors.Clear();

            foreach (var door in orderedDoors)
            {
                Doors.Add(door);
            }

            OnPropertyChanged(nameof(DoorSelectorTitle));

            if (!string.IsNullOrWhiteSpace(selectedDoorId))
            {
                var matchingDoor = Doors.FirstOrDefault(d => d.Id == selectedDoorId);

                if (matchingDoor != null)
                {
                    // Keep the ComboBox selected item aligned with the refreshed list. Do not update DoorState here if it is already showing the same door.
                    SelectedDoor = matchingDoor;
                }
                else
                {
                    // Previously selected door no longer exists. Clear selection so the dropdown shows "Select a door".
                    SelectedDoor = null;
                }
            }
            else
            {
                // Do not auto-select the first door. This allows the dropdown placeholder text to show.
                SelectedDoor = null;
            }
        }
        finally
        {
            _isLoadingDoors = false;
        }

        // Only change the live DoorState if:
        // - it has nothing selected yet
        // - the user-selected door is different
        // - the selected door no longer exists
        //
        // This prevents the 3-second list refresh from replacing the live/polled door object and causing visual flicker.
        if (SelectedDoor == null)
        {
            DoorState.SelectedDoor = null;
            return;
        }

        if (DoorState.SelectedDoor == null ||
            DoorState.SelectedDoor.Id != SelectedDoor.Id)
        {
            DoorState.SelectedDoor = SelectedDoor;
        }
    }

}
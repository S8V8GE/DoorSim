using CommunityToolkit.Mvvm.ComponentModel;
using DoorSim.Models;
using System.Collections.ObjectModel;

namespace DoorSim.ViewModels;

// ViewModel for one reusable door simulator panel.
//
// TwoDoorView will eventually use two instances:
// - Left door panel
// - Right door panel
//
// For now this class only owns independent door selection.
// Door interaction logic will be moved across gradually so we do not break
// the existing Single Door behaviour.
public partial class DoorPanelViewModel : ObservableObject
{
    /*
      #############################################################################
                                  Door selection
      #############################################################################
    */

    // Friendly label used by the UI, for example:
    // - Left Door
    // - Right Door
    public string PanelTitle { get; }

    // Doors available for this panel.
    //
    // This will be populated from the shared DoorsViewModel door list.
    public ObservableCollection<SoftwireDoor> Doors { get; } = new ObservableCollection<SoftwireDoor>();

    // Text shown in the selector bar.
    public string DoorSelectorTitle => $"Select a door ({Doors.Count})";

    // The door selected for this specific panel.
    //
    // In Two Door View, the left and right panels will each have their own
    // SelectedDoor instead of sharing DoorsViewModel.SelectedDoor.
    [ObservableProperty]
    private SoftwireDoor? selectedDoor;

    // Full DoorsViewModel used by DoorPanelView.
    //
    // DoorPanelView already expects a DoorsViewModel because it needs all the
    // existing calculated properties, colours, commands, and feedback methods.
    // This lets each Two Door side have its own independent selected door state
    // without rewriting DoorPanelView yet.
    public DoorsViewModel DoorState { get; } = new DoorsViewModel();

    // True while LoadDoors is refreshing the selector list.
    //
    // During refresh, SelectedDoor may be reassigned to a refreshed selector-list
    // object with the same Id. We do not want that to replace DoorState.SelectedDoor,
    // because DoorState is the live object used by DoorPanelView.
    private bool _isLoadingDoors;


    /*
      #############################################################################
                                  Constructor
      #############################################################################
    */

    public DoorPanelViewModel(string panelTitle)
    {
        PanelTitle = panelTitle;
    }

    // Called when the user changes the selected door in this panel's selector.
    //
    // If the change was caused by LoadDoors refreshing the selector list, we ignore
    // it here. LoadDoors will decide whether DoorState needs to change.
    //
    // If the user genuinely selects a different door, update DoorState so the
    // DoorPanelView follows that selection.
    partial void OnSelectedDoorChanged(SoftwireDoor? value)
    {
        if (_isLoadingDoors)
            return;

        DoorState.SelectedDoor = value;
    }


    /*
      #############################################################################
                                  Door loading
      #############################################################################
    */

    // Replaces the available door list for this panel while preserving the
    // currently selected door by Id whenever possible.
    //
    // Important:
    // This refreshes the selector list only.
    //
    // It does NOT call DoorState.LoadDoors(...), because DoorState is the live
    // state object used by DoorPanelView. Replacing DoorState.SelectedDoor every
    // 3 seconds causes the door image, lock text, reader status, and LED state to
    // flicker between old/default and live-polled values.
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
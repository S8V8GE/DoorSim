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

    // Selects a door for this panel while preserving live state from another already-polled door object.
    //
    // Used when switching from Single Door View to Two Door View.
    // The selector needs to use this panel's refreshed door object, but the visual panel should inherit the live state from Single Door View, such as:
    //      - door open/closed
    //      - breakglass active
    //      - REX active
    //      - shunted states
    //      - reader online/LED state
    public void SelectDoorPreservingLiveState(SoftwireDoor? selectorDoor, SoftwireDoor? liveStateSourceDoor)
    {
        _isLoadingDoors = true;

        try
        {
            SelectedDoor = selectorDoor;
        }
        finally
        {
            _isLoadingDoors = false;
        }

        if (selectorDoor == null)
        {
            DoorState.SelectedDoor = null;
            return;
        }

        if (liveStateSourceDoor != null &&
            liveStateSourceDoor.Id == selectorDoor.Id)
        {
            PreserveLiveState(
                source: liveStateSourceDoor,
                target: selectorDoor);
        }

        DoorState.SelectedDoor = selectorDoor;
    }

    // Refreshes the doors available in this panel's selector.
    // This method intentionally refreshes the selector list only. It does not call DoorState.LoadDoors(...),
    // because DoorState is the live state object used by DoorPanelView and is updated by polling.
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
            return;
        }

        // Same door is still selected.
        //
        // If the door's hardware configuration has changed while DoorSim is open
        // for example REX/breakglass/reader hardware was added or removed, update the
        // live DoorState object once while preserving its current live/polled state.
        //
        // This avoids the old 3-second flicker but still allows hardware changes to
        // appear in Two Door View without forcing the trainer to reselect the door.
        if (HasDoorConfigurationChanged(DoorState.SelectedDoor, SelectedDoor))
        {
            PreserveLiveState(
                source: DoorState.SelectedDoor,
                target: SelectedDoor);

            DoorState.SelectedDoor = SelectedDoor;
        }
    }

    // Returns true when the refreshed Softwire door has different hardware configuration from the door currently displayed in the live panel.
    //
    // This checks configuration/role fields only:
    //      - hardware role flags
    //      - device paths
    //      - reader Card + PIN configuration
    //
    // It intentionally does not compare live state such as:
    //      - door open/closed
    //      - REX active/inactive
    //      - reader online/LED
    private static bool HasDoorConfigurationChanged(SoftwireDoor currentDoor, SoftwireDoor refreshedDoor)
    {
        return
            currentDoor.Name != refreshedDoor.Name ||
            currentDoor.Href != refreshedDoor.Href ||

            currentDoor.HasDoorSensor != refreshedDoor.HasDoorSensor ||
            currentDoor.HasLock != refreshedDoor.HasLock ||
            currentDoor.DoorSensorDevicePath != refreshedDoor.DoorSensorDevicePath ||

            currentDoor.HasReaderSideIn != refreshedDoor.HasReaderSideIn ||
            currentDoor.HasReaderSideOut != refreshedDoor.HasReaderSideOut ||
            currentDoor.ReaderSideInDevicePath != refreshedDoor.ReaderSideInDevicePath ||
            currentDoor.ReaderSideOutDevicePath != refreshedDoor.ReaderSideOutDevicePath ||

            currentDoor.InReaderRequiresCardAndPin != refreshedDoor.InReaderRequiresCardAndPin ||
            currentDoor.InReaderPinTimeoutSeconds != refreshedDoor.InReaderPinTimeoutSeconds ||
            currentDoor.OutReaderRequiresCardAndPin != refreshedDoor.OutReaderRequiresCardAndPin ||
            currentDoor.OutReaderPinTimeoutSeconds != refreshedDoor.OutReaderPinTimeoutSeconds ||

            currentDoor.HasRexSideIn != refreshedDoor.HasRexSideIn ||
            currentDoor.HasRexSideOut != refreshedDoor.HasRexSideOut ||
            currentDoor.HasRexNoSide != refreshedDoor.HasRexNoSide ||
            currentDoor.RexSideInDevicePath != refreshedDoor.RexSideInDevicePath ||
            currentDoor.RexSideOutDevicePath != refreshedDoor.RexSideOutDevicePath ||
            currentDoor.RexNoSideDevicePath != refreshedDoor.RexNoSideDevicePath ||

            currentDoor.HasBreakGlass != refreshedDoor.HasBreakGlass ||
            currentDoor.BreakGlassDevicePath != refreshedDoor.BreakGlassDevicePath;
    }

    // Copies live/polled UI state from the currently displayed door into the refreshed door object before replacing DoorState.SelectedDoor.
    //
    // This prevents visual flicker when updating hardware configuration.
    //
    // The refreshed door keeps its new configuration fields, such as:
    //      - HasRexSideIn
    //      - RexSideInDevicePath
    //      - HasBreakGlass
    //      - ReaderSideInDevicePath
    //
    // The current door preserves live UI state, such as:
    //      - door open/closed
    //      - shunted states
    //      - reader online/LED state
    //      - REX/breakglass active state
    private static void PreserveLiveState(SoftwireDoor source, SoftwireDoor target)
    {
        // Door lock / sensor live state
        target.DoorIsLocked = source.DoorIsLocked;
        target.UnlockedForMaintenance = source.UnlockedForMaintenance;
        target.DoorSensorIsOpen = source.DoorSensorIsOpen;
        target.DoorSensorIsShunted = source.DoorSensorIsShunted;

        // In reader live state
        target.InReaderIsOnline = source.InReaderIsOnline;
        target.InReaderIsShunted = source.InReaderIsShunted;
        target.InReaderLedColor = source.InReaderLedColor;

        // Out reader live state
        target.OutReaderIsOnline = source.OutReaderIsOnline;
        target.OutReaderIsShunted = source.OutReaderIsShunted;
        target.OutReaderLedColor = source.OutReaderLedColor;

        // In REX live state
        target.RexSideInIsActive = source.RexSideInIsActive;
        target.RexSideInIsShunted = source.RexSideInIsShunted;

        // Out REX live state
        target.RexSideOutIsActive = source.RexSideOutIsActive;
        target.RexSideOutIsShunted = source.RexSideOutIsShunted;

        // No-side REX live state
        target.RexNoSideIsActive = source.RexNoSideIsActive;
        target.RexNoSideIsShunted = source.RexNoSideIsShunted;

        // Breakglass live state
        target.BreakGlassIsActive = source.BreakGlassIsActive;
        target.BreakGlassIsShunted = source.BreakGlassIsShunted;
    }

}
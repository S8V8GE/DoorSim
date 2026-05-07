using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoorSim.Models;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace DoorSim.ViewModels;

// ViewModel for the Door Interlocking Controls panel.
//
// Responsibilities:
//      - expose available Override and Lockdown input selector lists,
//      - prevent the same input being selected for both purposes,
//      - expose status text/colour for each selected input,
//      - expose Normal / Active commands for each selected input.
//
// Softwire loading/sending will be wired in later. This first version gives the UI a clean state model to bind to.
public partial class DoorInterlockingControlsViewModel : ObservableObject
{
    /*
      #############################################################################
                                  Source Data
      #############################################################################
    */

    // Master list of all simulated inputs available for interlocking.
    private readonly List<SimulatedInput> _allInputs = new();

    // Callback supplied by MainViewModel to send input state changes to Softwire.
    //
    // DoorInterlockingControlsViewModel does not directly know about SoftwireService.
    // This keeps the ViewModel testable and avoids giving this child ViewModel direct ownership of the HTTP service.
    private Func<SimulatedInput, string, Task<bool>>? _sendInputStateAsync;


    /*
      #############################################################################
                              Selector Lists and Selection
      #############################################################################
    */

    // Inputs available to the Override selector.
    // The currently selected Lockdown input is removed from this list.
    public ObservableCollection<SimulatedInput> AvailableOverrideInputs { get; } = new();

    // Inputs available to the Lockdown selector.
    // The currently selected Override input is removed from this list.
    public ObservableCollection<SimulatedInput> AvailableLockdownInputs { get; } = new();

    // Selected override input.
    [ObservableProperty]
    private SimulatedInput? overrideInput;

    // Selected lockdown input.
    [ObservableProperty]
    private SimulatedInput? lockdownInput;


    /*
      #############################################################################
                                  UI Brushes
      #############################################################################
    */

    private static readonly Brush GoodBrush = new SolidColorBrush(Color.FromRgb(40, 200, 120));
    private static readonly Brush BadBrush = new SolidColorBrush(Color.FromRgb(220, 80, 80));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(230, 170, 40));
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(170, 170, 170));


    /*
      #############################################################################
                              Status Text and Colours
      #############################################################################
    */

    public string OverrideStatusText => GetStatusText(OverrideInput);
    public Brush OverrideStatusColor => GetStatusColor(OverrideInput);

    public string LockdownStatusText => GetStatusText(LockdownInput);
    public Brush LockdownStatusColor => GetStatusColor(LockdownInput);


    /*
      #############################################################################
                                  Data Loading
      #############################################################################
    */

    // Configures the callback used to send Normal / Active state changes to Softwire.
    //
    // MainViewModel owns the actual Softwire service, so it passes in a small
    // callback rather than this ViewModel directly depending on ISoftwireService.
    public void ConfigureInputStateSender(Func<SimulatedInput, string, Task<bool>> sendInputStateAsync)
    {
        _sendInputStateAsync = sendInputStateAsync;

        RefreshCommandStates();
    }

    // Loads/reloads the master input list.
    //
    // Later this is called from MainViewModel after Softwire inputs are discovered.
    // Keeping this as a full replacement makes it easy to refresh when inputs are added/removed in Softwire.
    public void LoadInputs(IEnumerable<SimulatedInput> inputs)
    {
        var previousOverrideInput = OverrideInput;
        var previousLockdownInput = LockdownInput;

        var previousOverrideInputId = previousOverrideInput?.Id;
        var previousLockdownInputId = previousLockdownInput?.Id;

        _allInputs.Clear();

        _allInputs.AddRange(
            inputs
                .OrderBy(i => i.Name)
                .ThenBy(i => i.DevicePath));

        var refreshedOverrideInput = _allInputs
            .FirstOrDefault(i => i.Id == previousOverrideInputId);

        var refreshedLockdownInput = _allInputs
            .FirstOrDefault(i => i.Id == previousLockdownInputId);

        // Softwire may report Active=false while an input is shunted.
        // Preserve DoorSim's local visual state while shunted so user interaction
        // does not appear to be undone by the refresh loop.
        if (refreshedOverrideInput != null &&
            previousOverrideInput != null &&
            refreshedOverrideInput.IsShunted)
        {
            refreshedOverrideInput.IsActive = previousOverrideInput.IsActive;
        }

        if (refreshedLockdownInput != null &&
            previousLockdownInput != null &&
            refreshedLockdownInput.IsShunted)
        {
            refreshedLockdownInput.IsActive = previousLockdownInput.IsActive;
        }

        OverrideInput = refreshedOverrideInput;
        LockdownInput = refreshedLockdownInput;

        RefreshAvailableInputLists();
        RefreshAllStateProperties();
    }


    /*
      #############################################################################
                          Generated Property Change Hooks
      #############################################################################
    */

    partial void OnOverrideInputChanged(SimulatedInput? value)
    {
        RefreshAvailableInputLists();
        RefreshOverrideStateProperties();
        RefreshCommandStates();
    }

    partial void OnLockdownInputChanged(SimulatedInput? value)
    {
        RefreshAvailableInputLists();
        RefreshLockdownStateProperties();
        RefreshCommandStates();
    }


    /*
      #############################################################################
                              Available List Filtering
      #############################################################################
    */

    // Rebuilds both selector lists while preventing duplicate selection.
    //
    // If Override Input is selected, it is removed from the Lockdown selector.
    // If Lockdown Input is selected, it is removed from the Override selector.
    private void RefreshAvailableInputLists()
    {
        var overrideInputId = OverrideInput?.Id;
        var lockdownInputId = LockdownInput?.Id;

        AvailableOverrideInputs.Clear();

        foreach (var input in _allInputs.Where(i => i.Id != lockdownInputId))
        {
            AvailableOverrideInputs.Add(input);
        }

        AvailableLockdownInputs.Clear();

        foreach (var input in _allInputs.Where(i => i.Id != overrideInputId))
        {
            AvailableLockdownInputs.Add(input);
        }
    }


    /*
  #############################################################################
                          Normal / Active Commands
  #############################################################################
*/

    [RelayCommand(CanExecute = nameof(CanSetOverrideNormal))]
    private async Task SetOverrideNormalAsync()
    {
        await SetInputStateAsync(OverrideInput, "Inactive", false);

        RefreshOverrideStateProperties();
        RefreshCommandStates();
    }

    private bool CanSetOverrideNormal()
    {
        return OverrideInput != null && OverrideInput.IsActive;
    }

    [RelayCommand(CanExecute = nameof(CanSetOverrideActive))]
    private async Task SetOverrideActiveAsync()
    {
        await SetInputStateAsync(OverrideInput, "Active", true);

        RefreshOverrideStateProperties();
        RefreshCommandStates();
    }

    private bool CanSetOverrideActive()
    {
        return OverrideInput != null && !OverrideInput.IsActive;
    }

    [RelayCommand(CanExecute = nameof(CanSetLockdownNormal))]
    private async Task SetLockdownNormalAsync()
    {
        await SetInputStateAsync(LockdownInput, "Inactive", false);

        RefreshLockdownStateProperties();
        RefreshCommandStates();
    }

    private bool CanSetLockdownNormal()
    {
        return LockdownInput != null && LockdownInput.IsActive;
    }

    [RelayCommand(CanExecute = nameof(CanSetLockdownActive))]
    private async Task SetLockdownActiveAsync()
    {
        await SetInputStateAsync(LockdownInput, "Active", true);

        RefreshLockdownStateProperties();
        RefreshCommandStates();
    }

    private bool CanSetLockdownActive()
    {
        return LockdownInput != null && !LockdownInput.IsActive;
    }

    // Sends the requested input state to Softwire, then updates the local UI state.
    //
    // Important:
    // The UI labels the inactive state as "Normal", because that is what the trainer sees in Softwire.
    // The Softwire API command expects "Inactive" when returning the input to normal.
    //
    // If the input is shunted, Softwire may not report Active correctly during polling.
    // We still update local state so DoorSim remains interactive and the trainer can see what they attempted to simulate.
    private async Task SetInputStateAsync(SimulatedInput? input, string softwireState, bool localIsActive)
    {
        if (input == null)
            return;

        if (_sendInputStateAsync != null)
        {
            var success = await _sendInputStateAsync(input, softwireState);

            if (!success)
                return;
        }

        input.IsActive = localIsActive;
    }


    /*
      #############################################################################
                              State Refresh Helpers
      #############################################################################
    */

    private void RefreshAllStateProperties()
    {
        RefreshOverrideStateProperties();
        RefreshLockdownStateProperties();
        RefreshCommandStates();
    }

    private void RefreshOverrideStateProperties()
    {
        OnPropertyChanged(nameof(OverrideStatusText));
        OnPropertyChanged(nameof(OverrideStatusColor));
    }

    private void RefreshLockdownStateProperties()
    {
        OnPropertyChanged(nameof(LockdownStatusText));
        OnPropertyChanged(nameof(LockdownStatusColor));
    }

    private void RefreshCommandStates()
    {
        SetOverrideNormalCommand.NotifyCanExecuteChanged();
        SetOverrideActiveCommand.NotifyCanExecuteChanged();
        SetLockdownNormalCommand.NotifyCanExecuteChanged();
        SetLockdownActiveCommand.NotifyCanExecuteChanged();
    }

    private string GetStatusText(SimulatedInput? input)
    {
        if (input == null)
            return "No input selected";

        if (input.IsShunted)
            return "Shunted";

        return input.IsActive ? "Active" : "Normal";
    }

    private Brush GetStatusColor(SimulatedInput? input)
    {
        if (input == null)
            return NeutralBrush;

        if (input.IsShunted)
            return WarningBrush;

        return input.IsActive ? BadBrush : GoodBrush;
    }

}
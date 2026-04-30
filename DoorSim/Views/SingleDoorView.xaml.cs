using System;
using DoorSim.Services;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DoorSim.Views;

public partial class SingleDoorView : UserControl
{
    /*
      #############################################################################
                           Constructor and Initialisation
      #############################################################################
    */
    public SingleDoorView()
    {
        InitializeComponent(); // It's actually spelt Initialise... but we can let it go ;)

        _floatingToolTipTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        _floatingToolTipTimer.Tick += (s, e) =>
        {
            if (FloatingToolTip.IsOpen && _floatingToolTipTextProvider != null)
            {
                FloatingToolTipText.Text = _floatingToolTipTextProvider();
            }
        };
    }

    private Func<string>? _floatingToolTipTextProvider;
    private readonly DispatcherTimer _floatingToolTipTimer;


    /*
      #############################################################################
                                   Helper methods
      #############################################################################
    */

    // Temporary helper used by UI event handlers to access Softwire commands.
    // This keeps repeated service lookup code out of each click handler.
    // TODO: Later, this should be replaced with proper MVVM commands / dependency injection... or maybe i'll just leave it if it works...
    private ISoftwireService? GetSoftwireService()
    {
        var mainWindow = Application.Current.MainWindow;

        if (mainWindow?.DataContext is not DoorSim.ViewModels.MainViewModel mainVm)
            return null;

        return mainVm.GetType()
            .GetField("_softwireService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(mainVm) as ISoftwireService;
    }

    // Sends an Active/Inactive state change to a Softwire input.
    // Used by REX buttons and other simulated input devices.
    private async Task SetInputStateAsync(string inputPath, string state)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return;

        var service = GetSoftwireService();

        if (service == null)
            return;

        await service.SetInputStateAsync(inputPath, state);
    }

    // Shows a custom tooltip that follows the mouse.
    // Standard WPF ToolTips do not continuously follow the cursor once opened.
    private void ShowFloatingToolTip(Func<string> textProvider, MouseEventArgs e)
    {
        _floatingToolTipTextProvider = textProvider;

        FloatingToolTipText.Text = textProvider();

        var position = e.GetPosition(this);

        FloatingToolTip.HorizontalOffset = position.X + 16;
        FloatingToolTip.VerticalOffset = position.Y + 18;

        FloatingToolTip.IsOpen = true;
        _floatingToolTipTimer.Start();
    }

    // Moves the custom tooltip as the mouse moves.
    private void MoveFloatingToolTip(MouseEventArgs e)
    {
        if (!FloatingToolTip.IsOpen)
            return;

        var position = e.GetPosition(this);

        FloatingToolTip.HorizontalOffset = position.X + 16;
        FloatingToolTip.VerticalOffset = position.Y + 18;
    }

    // Hides the custom tooltip.
    private void HideFloatingToolTip()
    {
        FloatingToolTip.IsOpen = false;
        _floatingToolTipTimer.Stop();
        _floatingToolTipTextProvider = null;
    }


    /*
      #############################################################################
                                Door image handlers
      #############################################################################
    */

    // Opens the Door tooltip.
    private void DoorImage_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        ShowFloatingToolTip(() => vm.DoorActionTooltip, e);
    }

    // Moves tooltip around as mouse moves over the door image.
    private void DoorImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        FloatingToolTipText.Text = vm.DoorActionTooltip;
        MoveFloatingToolTip(e);
    }

    // Ensures the tooltip closes cleanly when the mouse leaves the door image.
    private void DoorImage_MouseLeave(object sender, MouseEventArgs e)
    {
        HideFloatingToolTip();
    }

    // Toggles the door sensor input when the door image is clicked (WPF closes tooltips on click, so this keeps the hover behaviour feeling live).
    private async void DoorImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        // Prevent interaction if door sensor is shunted
        if (vm.SelectedDoor.DoorSensorIsShunted)
            return;

        if (string.IsNullOrWhiteSpace(vm.SelectedDoor.DoorSensorDevicePath))
            return;

        // Toggle state
        var newState = vm.SelectedDoor.DoorSensorIsOpen ? "Inactive" : "Active";

        // Instant UI update (optimistic... dont want it lagging was taking between 0-1000ms before)
        var newIsOpen = newState == "Active";
        vm.UpdateSelectedDoorState(
            vm.SelectedDoor.DoorIsLocked,
            newIsOpen,
            vm.SelectedDoor.DoorSensorIsShunted);

        // Send to Softwire
        await SetInputStateAsync(
                vm.SelectedDoor.DoorSensorDevicePath,
                newState);

        // Reopen tooltip 
        if (DoorImage.ToolTip is ToolTip toolTip)
        {
            toolTip.IsOpen = false;

            await Task.Delay(50);

            if (DoorImage.IsMouseOver)
            {
                toolTip.DataContext = DoorImage.DataContext;
                toolTip.IsOpen = true;
            }
        }
    }



    /*
      #############################################################################
                              REX image handlers
      #############################################################################
    */
    // REX behaviour:
    // - Mouse down sets the input Active
    // - Mouse up sets the input Inactive after a short delay
    // - Mouse leave safely releases the input if it is still active
    // - Mouse move updates the tooltip position and text if needed
    // - Shunted REX inputs ignore mouse interaction

    //---------------------------------------------
    // Opens the In REX tooltip.
    private void InRexImage_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        ShowFloatingToolTip(() => vm.InRexActionTooltip, e);
    }

    // Moves tooltip around as mouse moves over the In REX image.
    private void InRexImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        FloatingToolTipText.Text = vm.InRexActionTooltip;
        MoveFloatingToolTip(e);
    }

    // Closes the In REX tooltip (also safely releases In REX if the mouse leaves while it is active). 
    private async void InRexImage_MouseLeave(object sender, MouseEventArgs e)
    {
        HideFloatingToolTip();

        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (!vm.SelectedDoor.RexSideInIsActive)
            return;

        vm.UpdateInRexState(false, vm.SelectedDoor.RexSideInIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.RexSideInDevicePath,
            "Inactive");
    }

    // Presses the In REX (Mouse down = set the In REX input Active).
    private async void InRexImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        // If the REX is shunted, do nothing.
        if (vm.SelectedDoor.RexSideInIsShunted)
            return;

        // Optimistic UI update so the image/status changes immediately.
        vm.UpdateInRexState(true, vm.SelectedDoor.RexSideInIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.RexSideInDevicePath,
            "Active");
    }

    // Releases the In REX (Mouse up = wait briefly, then set the REX input Inactive - The delay makes even quick clicks visibly register as a REX press).
    private async void InRexImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        // If the REX is shunted, do nothing.
        if (vm.SelectedDoor.RexSideInIsShunted)
            return;

        await Task.Delay(1000);

        // Optimistic UI update back to normal.
        vm.UpdateInRexState(false, vm.SelectedDoor.RexSideInIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.RexSideInDevicePath,
            "Inactive");
    }

    //---------------------------------------------
    // Opens the Out REX tooltip.
    private void OutRexImage_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        ShowFloatingToolTip(() => vm.OutRexActionTooltip, e);
    }

    // Moves tooltip around as mouse moves over the Out REX image.
    private void OutRexImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        FloatingToolTipText.Text = vm.OutRexActionTooltip;
        MoveFloatingToolTip(e);
    }

    // Closes the Out REX tooltip (also safely releases Out REX if the mouse leaves while it is active).
    private async void OutRexImage_MouseLeave(object sender, MouseEventArgs e)
    {
        HideFloatingToolTip();

        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm) 
            return;

        if (vm.SelectedDoor == null)
            return;

        if (!vm.SelectedDoor.RexSideOutIsActive)
            return;

        vm.UpdateOutRexState(false, vm.SelectedDoor.RexSideOutIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.RexSideOutDevicePath,
            "Inactive");
    }

    // Presses the Out REX (Mouse down = set the Out REX input Active).
    private async void OutRexImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (vm.SelectedDoor.RexSideOutIsShunted)
            return;

        vm.UpdateOutRexState(true, vm.SelectedDoor.RexSideOutIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.RexSideOutDevicePath,
            "Active");
    }

    // Releases the Out REX (Mouse up = wait briefly, then set the REX input Inactive - The delay makes even quick clicks visibly register as a REX press).
    private async void OutRexImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (vm.SelectedDoor.RexSideOutIsShunted)
            return;

        await Task.Delay(1000);

        vm.UpdateOutRexState(false, vm.SelectedDoor.RexSideOutIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.RexSideOutDevicePath,
            "Inactive");
    }

    //---------------------------------------------
    // Opens the No Side REX tooltip.
    private void NoSideRexImage_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        ShowFloatingToolTip(() => vm.NoSideRexActionTooltip, e);
    }

    // Moves tooltip around as mouse moves over the No Side REX image.
    private void NoSideRexImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        FloatingToolTipText.Text = vm.NoSideRexActionTooltip;
        MoveFloatingToolTip(e);
    }

    // Closes the No Side REX tooltip (also safely releases No Side REX if the mouse leaves while it is active). 
    private async void NoSideRexImage_MouseLeave(object sender, MouseEventArgs e)
    {
        HideFloatingToolTip();

        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (!vm.SelectedDoor.RexNoSideIsActive)
            return;

        vm.UpdateNoSideRexState(false, vm.SelectedDoor.RexNoSideIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.RexNoSideDevicePath,
            "Inactive");
    }

    // Presses the No Side REX (Mouse down = set the No Side REX input Active).
    private async void NoSideRexImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (vm.SelectedDoor.RexNoSideIsShunted)
            return;

        vm.UpdateNoSideRexState(true, vm.SelectedDoor.RexNoSideIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.RexNoSideDevicePath,
            "Active");
    }

    // Releases the No Side REX (Mouse up = wait briefly, then set the REX input Inactive - The delay makes even quick clicks visibly register as a REX press).
    private async void NoSideRexImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (vm.SelectedDoor.RexNoSideIsShunted)
            return;

        await Task.Delay(1000);

        vm.UpdateNoSideRexState(false, vm.SelectedDoor.RexNoSideIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.RexNoSideDevicePath,
            "Inactive");
    }


    /*
      #############################################################################
                              Breakglass image handlers
      #############################################################################
    */

    // Opens the Breakglass tooltip.
    private void BreakGlassImage_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        ShowFloatingToolTip(() => vm.BreakGlassActionTooltip, e);
    }

    // Moves tooltip around as mouse moves over the Breakglass image.
    private void BreakGlassImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        FloatingToolTipText.Text = vm.BreakGlassActionTooltip;
        MoveFloatingToolTip(e);
    }

    // Closes the Breakglass tooltip.
    private void BreakGlassImage_MouseLeave(object sender, MouseEventArgs e)
    {
        HideFloatingToolTip();
    }

    // Toggles Breakglass between Normal and Active (If Breakglass is shunted, interaction is ignored).
    private async void BreakGlassImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (vm.SelectedDoor.BreakGlassIsShunted)
            return;

        if (string.IsNullOrWhiteSpace(vm.SelectedDoor.BreakGlassDevicePath))
            return;

        var newIsActive = !vm.SelectedDoor.BreakGlassIsActive;
        var newState = newIsActive ? "Active" : "Inactive";

        // Optimistic UI update so the image/status changes immediately.
        vm.UpdateBreakGlassState(newIsActive, vm.SelectedDoor.BreakGlassIsShunted);

        await SetInputStateAsync(
            vm.SelectedDoor.BreakGlassDevicePath,
            newState);
    }


}
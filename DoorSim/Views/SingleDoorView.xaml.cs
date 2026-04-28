using DoorSim.Services;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoorSim.Views;

public partial class SingleDoorView : UserControl
{
    public SingleDoorView()
    {
        InitializeComponent();
    }

    // Reopens the tooltip after clicking the door image.
    // WPF closes tooltips on click, so this keeps the hover behaviour feeling live.
    private async void DoorImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (string.IsNullOrWhiteSpace(vm.SelectedDoor.DoorSensorDevicePath))
            return;

        // Get the service via Application (simple approach for now)
        var mainWindow = Application.Current.MainWindow;
        if (mainWindow?.DataContext is not DoorSim.ViewModels.MainViewModel mainVm)
            return;

        var service = mainVm.GetType()
            .GetField("_softwireService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(mainVm) as ISoftwireService;

        if (service == null)
            return;

        // Toggle state
        var newState = vm.SelectedDoor.DoorSensorIsOpen ? "Inactive" : "Active";

        // Instant UI update (optimistic... dont want it lagging was taking between 0-1000ms before)
        var newIsOpen = newState == "Active";
        vm.UpdateSelectedDoorState(vm.SelectedDoor.DoorIsLocked, newIsOpen);

        // Send to Softwire
        await service.SetInputStateAsync(
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

    // Ensures the tooltip closes cleanly when the mouse leaves the door image.
    private void DoorImage_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DoorImage.ToolTip is ToolTip toolTip)
        {
            toolTip.IsOpen = false;
        }
    }
}
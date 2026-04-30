using System;
using DoorSim.Services;
using DoorSim.Models;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Media;

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

        DataContextChanged += SingleDoorView_DataContextChanged;

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

    // Opens the PIN entry window and sends the entered PIN to Softwire.
    //
    // PINs are sent to Softwire as Wiegand26:
    // - Facility code = 0
    // - Card number   = entered PIN
    //
    // isInReader controls which temporary UI status is shown after sending:
    // - true  = In Reader shows "PIN sent"
    // - false = Out Reader shows "PIN sent"
    private async Task OpenPinDialogAndSendAsync(DoorSim.ViewModels.DoorsViewModel vm, string readerName, string readerPath, bool isInReader, int? timeoutSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(readerPath))
            return;

        var pinWindow = new PinEntryWindow(readerName, timeoutSeconds)
        {
            Owner = Window.GetWindow(this)
        };

        var result = pinWindow.ShowDialog();

        if (result != true)
        {
            if (pinWindow.TimedOut)
            {
                _ = PlayTripleBeepAsync();

                ShowAppMessage(
                    "PIN entry timed out. Access was not completed.",
                    "PIN timeout");
            }

            return;
        }

        var service = GetSoftwireService();

        if (service == null)
            return;

        if (!int.TryParse(pinWindow.EnteredPin, out var pin))
            return;

        var sent = await service.SwipeWiegand26Async(
            readerPath,
            0,
            pin);

        if (!sent)
            return;

        if (isInReader)
        {
            vm.InReaderPinSent = true;

            await Task.Delay(2000);

            vm.InReaderPinSent = false;
        }
        else
        {
            vm.OutReaderPinSent = true;

            await Task.Delay(2000);

            vm.OutReaderPinSent = false;
        }
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

    // Plays a short local sound when a card is presented to a reader.
    // Uses a WAV resource rather than SystemSounds because system event sounds
    // may be muted or disabled in Windows / VM environments.
    private void PlayCredentialPresentedSound()
    {
        try
        {
            var stream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Sounds/Credential_Beep.wav"));

            if (stream == null)
                return;

            using var player = new SoundPlayer(stream.Stream);
            player.Play();
        }
        catch
        {
            // Sound is non-critical. If playback fails, ignore it.
        }
    } 

    // Plays a sound when a reader LED colour changes.
    private void OnReaderLedChanged()
    {
        PlayCredentialPresentedSound();
    }

    // Called when the view receives or changes its DataContext.
    // Used to subscribe to reader LED change events from DoorsViewModel.
    private void SingleDoorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DoorSim.ViewModels.DoorsViewModel oldVm)
        {
            oldVm.ReaderLedChanged -= OnReaderLedChanged;
        }

        if (e.NewValue is DoorSim.ViewModels.DoorsViewModel newVm)
        {
            newVm.ReaderLedChanged += OnReaderLedChanged;
        }
    }

    // Shows a custom message window centred on the main application window.
    // Used instead of MessageBox because MessageBox does not always centre correctly in VM/RDP environments (or if that's not true, I didn't know how to do it).
    private void ShowAppMessage(string message, string title)
    {
        var messageWindow = new AppMessageWindow(title, message)
        {
            Owner = Window.GetWindow(this)
        };

        messageWindow.ShowDialog();
    }

    // Plays the credential beep three times (Used for timeout / warning feedback if PIN not entered within the allowed time).
    // Uses PlaySync inside a background task so each beep finishes before the next starts.
    private async Task PlayTripleBeepAsync()
    {
        await Task.Run(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    var streamInfo = Application.GetResourceStream(
                        new Uri("pack://application:,,,/Sounds/Credential_Beep.wav"));

                    if (streamInfo == null)
                        return;

                    using var player = new System.Media.SoundPlayer(streamInfo.Stream);

                    player.PlaySync();

                    System.Threading.Thread.Sleep(10);
                }
                catch
                {
                    // Sound is non-critical. If playback fails, ignore it.
                }
            }
        });
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

    }


    /*
      #############################################################################
                            Reader image handlers
      #############################################################################
    */

    // Shows the floating tooltip for the In Reader.
    private void InReader_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        ShowFloatingToolTip(() => vm.InReaderActionTooltip, e);
    }

    // Keeps the floating tooltip next to the mouse while hovering over the In Reader.
    private void InReader_MouseMove(object sender, MouseEventArgs e)
    {
        MoveFloatingToolTip(e);
    }

    // Hides the floating tooltip when leaving the In Reader.
    private void InReader_MouseLeave(object sender, MouseEventArgs e)
    {
        HideFloatingToolTip();
    }

    // Allows the In Reader to accept dragged Cardholder objects.
    // While a valid cardholder is hovering over the reader, the reader LED is shown as blue.
    private void InReader_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Cardholder)))
        {
            e.Effects = DragDropEffects.Copy;

            if (DataContext is DoorSim.ViewModels.DoorsViewModel vm)
            {
                vm.IsCardholderOverInReader = true;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    // Clears the drag-over state when the dragged cardholder leaves the In Reader.
    // This returns the LED to its normal live Softwire state.
    private void InReader_DragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is DoorSim.ViewModels.DoorsViewModel vm)
        {
            vm.IsCardholderOverInReader = false;
        }

        e.Handled = true;
    }

    // Handles dropping a cardholder onto the In Reader.
    // Sends the cardholder credential to Softwire using SwipeRaw.
    private async void InReader_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
        {
            e.Handled = true;
            return;
        }

        // Clear drag-over state so the LED returns to normal after drop.
        vm.IsCardholderOverInReader = false;

        if (!e.Data.GetDataPresent(typeof(Cardholder)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var cardholder = e.Data.GetData(typeof(Cardholder)) as Cardholder;

        if (cardholder == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (vm.SelectedDoor == null)
        {
            e.Handled = true;
            return;
        }

        // Do not send credentials to an unavailable reader.
        if (vm.SelectedDoor.InReaderIsShunted || !vm.SelectedDoor.InReaderIsOnline)
        {
            e.Handled = true;
            return;
        }

        var service = GetSoftwireService();

        if (service == null)
        {
            e.Handled = true;
            return;
        }

        // Local feedback: card has been presented to the reader.
        PlayCredentialPresentedSound();

        var swipeSent = await service.SwipeRawAsync(vm.SelectedDoor.ReaderSideInDevicePath, cardholder.TrimmedCredential, cardholder.BitCount);

        if (swipeSent && vm.SelectedDoor.InReaderRequiresCardAndPin)
        {
            if (!cardholder.HasPin)
            {
                ShowAppMessage(
                    "This reader requires Card + PIN, but this cardholder does not have a PIN configured.",
                    "PIN required");
            }
            else
            {
                await OpenPinDialogAndSendAsync(vm, "In Reader", vm.SelectedDoor.ReaderSideInDevicePath, true, vm.SelectedDoor.InReaderPinTimeoutSeconds);
            }

            e.Handled = true;
        }
    }

    // Opens PIN entry for the In Reader.
    // The dialog validates that the PIN is 4 or 5 digits before allowing OK.
    private async void InReaderEnterPin_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (vm.SelectedDoor.InReaderIsShunted)
        {
            ShowAppMessage(
                "In reader is shunted. PIN entry is not available.",
                "Reader unavailable");
            return;
        }

        if (!vm.SelectedDoor.InReaderIsOnline)
        {
            ShowAppMessage(
                "In reader is offline. PIN entry is not available.",
                "Reader unavailable");
            return;
        }

        await OpenPinDialogAndSendAsync(vm, "In Reader", vm.SelectedDoor.ReaderSideInDevicePath, true);
    }


    // Shows the floating tooltip for the Out Reader.
    private void OutReader_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        ShowFloatingToolTip(() => vm.OutReaderActionTooltip, e);
    }

    // Keeps the floating tooltip next to the mouse while hovering over the Out Reader.
    private void OutReader_MouseMove(object sender, MouseEventArgs e)
    {
        MoveFloatingToolTip(e);
    }

    // Hides the floating tooltip when leaving the Out Reader.
    private void OutReader_MouseLeave(object sender, MouseEventArgs e)
    {
        HideFloatingToolTip();
    }

    // Allows the Out Reader to accept dragged Cardholder objects.
    // While a valid cardholder is hovering over the reader, the reader LED is shown as blue.
    private void OutReader_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Cardholder)))
        {
            e.Effects = DragDropEffects.Copy;

            if (DataContext is DoorSim.ViewModels.DoorsViewModel vm)
            {
                vm.IsCardholderOverOutReader = true;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    // Clears the drag-over state when the dragged cardholder leaves the Out Reader.
    // This returns the LED to its normal live Softwire state.
    private void OutReader_DragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is DoorSim.ViewModels.DoorsViewModel vm)
        {
            vm.IsCardholderOverOutReader = false;
        }

        e.Handled = true;
    }

    // Handles dropping a cardholder onto the Out Reader.
    // Sends the cardholder credential to Softwire using SwipeRaw.
    private async void OutReader_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
        {
            e.Handled = true;
            return;
        }

        // Clear drag-over state so the LED returns to normal after drop.
        vm.IsCardholderOverOutReader = false;

        if (!e.Data.GetDataPresent(typeof(Cardholder)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var cardholder = e.Data.GetData(typeof(Cardholder)) as Cardholder;

        if (cardholder == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (vm.SelectedDoor == null)
        {
            e.Handled = true;
            return;
        }

        // Do not send credentials to an unavailable reader.
        if (vm.SelectedDoor.OutReaderIsShunted || !vm.SelectedDoor.OutReaderIsOnline)
        {
            e.Handled = true;
            return;
        }

        var service = GetSoftwireService();

        if (service == null)
        {
            e.Handled = true;
            return;
        }

        // Local feedback: card has been presented to the reader.
        PlayCredentialPresentedSound();

        var swipeSent = await service.SwipeRawAsync(vm.SelectedDoor.ReaderSideOutDevicePath, cardholder.TrimmedCredential, cardholder.BitCount);

        if (swipeSent && vm.SelectedDoor.OutReaderRequiresCardAndPin)
        {
            if (!cardholder.HasPin)
            {
                ShowAppMessage(
                    "This reader requires Card + PIN, but this cardholder does not have a PIN configured.",
                    "PIN required");
            }
            else
            {
                await OpenPinDialogAndSendAsync(vm, "Out Reader", vm.SelectedDoor.ReaderSideOutDevicePath, false, vm.SelectedDoor.InReaderPinTimeoutSeconds);
            }
        }

        e.Handled = true;
    }

    // Opens PIN entry for the Out Reader.
    // The dialog validates that the PIN is 4 or 5 digits before allowing OK.
    private async void OutReaderEnterPin_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DoorSim.ViewModels.DoorsViewModel vm)
            return;

        if (vm.SelectedDoor == null)
            return;

        if (vm.SelectedDoor.OutReaderIsShunted)
        {
            ShowAppMessage(
                "Out reader is shunted. PIN entry is not available.",
                "Reader unavailable");
            return;
        }

        if (!vm.SelectedDoor.OutReaderIsOnline)
        {
            ShowAppMessage(
                "Out reader is offline. PIN entry is not available.",
                "Reader unavailable");
            return;
        }

        await OpenPinDialogAndSendAsync(vm, "Out Reader", vm.SelectedDoor.ReaderSideOutDevicePath, false);
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
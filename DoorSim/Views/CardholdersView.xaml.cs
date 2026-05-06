using DoorSim.Models;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoorSim.Views;

// Code-behind for the Cardholders panel.
//
// The cardholder data/search logic lives in CardholdersViewModel. This file only handles UI-specific drag/drop behaviour:
//      - starting a WPF drag operation from the DataGrid,
//      - showing a floating credential image during drag,
//      - tracking the real mouse position while WPF drag/drop is active.
public partial class CardholdersView : UserControl
{
    /*
      #############################################################################
                          Constructor & Initialization
      #############################################################################
    */

    public CardholdersView()
    {
        InitializeComponent();
    }


    /*
      #############################################################################
                          Win32 Cursor Position Interop
      #############################################################################
    */

    // Used to read the real mouse position during a WPF drag operation.
    // WPF drag/drop is modal, so normal MouseMove events are not reliable once dragging starts.
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    private struct POINT
    {
        public int X;
        public int Y;
    }


    /*
      #############################################################################
                               Drag Popup Helpers
      #############################################################################
    */

    // Moves the floating credential image so it follows the current screen cursor.
    // Uses screen coordinates because Popup.Placement="Absolute".
    private void UpdateCredentialDragPopupPosition()
    {
        if (!GetCursorPos(out var point))
            return;

        CredentialDragPopup.HorizontalOffset = point.X + 1;
        CredentialDragPopup.VerticalOffset = point.Y + 1;
    }


    /*
      #############################################################################
                             DataGrid Drag Handlers
      #############################################################################
    */

    // Starts a drag operation when the user drags the selected cardholder row.
    // The drag payload is the Cardholder object itself. Reader drop targets can then access TrimmedCredential, BitCount, HasPin, and CardholderName.
    private void CardholdersGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (CardholdersGrid.SelectedItem is not Cardholder selectedCardholder)
            return;

        UpdateCredentialDragPopupPosition();
        CredentialDragPopup.IsOpen = true;

        try
        {
            DragDrop.DoDragDrop(
                CardholdersGrid,
                selectedCardholder,
                DragDropEffects.Copy);
        }
        finally
        {
            CredentialDragPopup.IsOpen = false;
        }
    }

    // Keeps the floating credential image following the mouse during drag/drop.
    private void CardholdersGrid_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (CredentialDragPopup.IsOpen)
        {
            UpdateCredentialDragPopupPosition();
        }

        // Keep the normal Windows drag cursor behaviour.
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

}

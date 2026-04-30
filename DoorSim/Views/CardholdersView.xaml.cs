using DoorSim.Models;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoorSim.Views;

public partial class CardholdersView : UserControl
{
    public CardholdersView()
    {
        InitializeComponent();
    }

    // Used to read the real mouse position during a WPF drag operation.
    // WPF drag/drop is modal, so normal MouseMove events are not reliable once dragging starts.
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    private struct POINT
    {
        public int X;
        public int Y;
    }

    // Moves the floating credential image so it follows the mouse cursor.
    private void UpdateCredentialDragPopupPosition()
    {
        if (!GetCursorPos(out var point))
            return;

        CredentialDragPopup.HorizontalOffset = point.X + 1;
        CredentialDragPopup.VerticalOffset = point.Y + 1;
    }

    // Starts a drag operation when the user drags a cardholder row.
    // The dragged data is the Cardholder object itself, allowing a reader drop target
    // to access TrimmedCredential and BitCount later.
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

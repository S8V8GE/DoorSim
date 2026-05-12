using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoorSim.Views;

// Code-behind for AutoModeView.
//
// This file intentionally contains only lightweight UI input filtering.
// Auto Mode simulation logic belongs in AutoModeViewModel.
//
// The numeric TextBoxes in AutoModeView bind directly to int properties.
// Without input filtering, users can temporarily clear a TextBox or paste text, which causes WPF to attempt invalid int conversions and log binding failures.
public partial class AutoModeView : UserControl
{
    // Matches one or more digits.
    // Used by the TextBox input/paste handlers to keep numeric fields numeric before values reach the ViewModel.
    private static readonly Regex DigitsOnlyRegex = new Regex("^[0-9]+$");

    // Constructor. Initialises the view.
    public AutoModeView()
    {
        InitializeComponent();
    }

    // Prevents non-numeric keyboard input in number-only fields.
    // This handles normal text input. Special keys such as Backspace/Delete are handled separately by NumberTextBox_PreviewKeyDown.
    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
    }

    // Prevents pasting non-numeric or empty text into number-only fields.
    //
    // This is needed because PreviewTextInput does not protect against paste operations. The ViewModel still performs final range validation.
    private void DigitsOnly_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string;

        if (string.IsNullOrWhiteSpace(pastedText) ||
            !DigitsOnlyRegex.IsMatch(pastedText))
        {
            e.CancelCommand();
        }
    }

}

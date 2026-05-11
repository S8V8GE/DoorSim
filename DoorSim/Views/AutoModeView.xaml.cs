using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoorSim.Views;

public partial class AutoModeView : UserControl
{
    private static readonly Regex DigitsOnlyRegex = new Regex("^[0-9]+$");

    public AutoModeView()
    {
        InitializeComponent();
    }

    // Prevents non-numeric keyboard input in number-only fields.
    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
    }

    // Prevents number-only TextBoxes from becoming empty.
    //
    // These TextBoxes are bound to int properties. If the user deletes all text,
    // WPF tries to convert "" into an int and logs a binding failure.
    // This blocks deleting the final digit or deleting a full selection.
    private void NumberTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (e.Key != Key.Back && e.Key != Key.Delete)
            return;

        var textLength = textBox.Text?.Length ?? 0;

        if (textLength == 0)
        {
            e.Handled = true;
            return;
        }

        // If the whole value is selected, Backspace/Delete would make it empty.
        if (textBox.SelectionLength >= textLength)
        {
            e.Handled = true;
            return;
        }

        // If there is only one digit left, Backspace/Delete would make it empty.
        if (textLength == 1)
        {
            e.Handled = true;
        }
    }

    // Prevents pasting non-numeric text into number-only fields.
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

using System.Windows;

namespace DoorSim.Views;

// Code-behind for the ConnectWindow (View).
//
// Responsible for:
// - Handling simple UI interactions (button clicks, mouse events)
// - Exposing user-entered values (hostname, password)
//
// This file does NOT perform any business logic or networking.
// It simply supports the UI and passes data back to the ViewModel.


// Represents the connection dialog window.
// Used to collect Softwire login details from the user.
public partial class ConnectWindow : Window
{
    // Retrieves the entered hostname from the UI.
    // Trimmed to remove accidental whitespace.
    public string Hostname => HostnameTextBox.Text.Trim();
    // Retrieves the entered password from the UI.
    // Uses PasswordBox for secure input.
    public string Password => PasswordBox.Password;

    // Initialises the window and its UI components.
    public ConnectWindow()
    {
        InitializeComponent();
    }

    // Triggered when the user clicks "Connect".
    //
    // Sets DialogResult to true, indicating success,
    // and closes the dialog so the ViewModel can proceed.
    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    // Triggered when the user clicks "Cancel".
    //
    // Sets DialogResult to false, indicating the operation was cancelled,
    // and closes the dialog without attempting a connection.
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // Triggered when the user presses the "view password" button.
    //
    // Copies the hidden password into a visible TextBox,
    // then swaps visibility so the password becomes readable.
    private void ViewPassword_MouseDown(object sender, RoutedEventArgs e)
    {
        PasswordTextBox.Text = PasswordBox.Password;

        PasswordBox.Visibility = Visibility.Collapsed;
        PasswordTextBox.Visibility = Visibility.Visible;
    }

    // Triggered when the user releases the "view password" button.
    //
    // Copies the visible password back into the PasswordBox,
    // then restores the hidden (secure) view.
    private void ViewPassword_MouseUp(object sender, RoutedEventArgs e)
    {
        PasswordBox.Password = PasswordTextBox.Text;

        PasswordTextBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
    }
}

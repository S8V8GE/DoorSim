using System.Windows;

namespace DoorSim.Views;

// Code-behind for the Softwire connection dialog.
//
// Responsibilities:
//      - expose the hostname and password entered by the user,
//      - handle OK/Cancel button clicks,
//      - handle temporary password reveal UI.
//
// This file intentionally performs no networking or login logic. MainViewModel reads the entered values and performs the connection attempt.
public partial class ConnectWindow : Window
{
    /*
      #############################################################################
                          Public Input Properties
      #############################################################################
    */

    // Retrieves the entered hostname from the UI. Trimmed to remove accidental whitespace.
    public string Hostname => HostnameTextBox.Text.Trim();
    // Retrieves the entered password from the UI. Uses PasswordBox for secure input.
    public string Password => PasswordBox.Password;


    /*
      #############################################################################
                               Constructor 
      #############################################################################
    */

    // Initialises the window and its UI components.
    public ConnectWindow()
    {
        InitializeComponent();

        PasswordBox.Focus();
    }


    /*
      #############################################################################
                           Dialog Button Handlers 
      #############################################################################
    */

    // Triggered when the user clicks "Connect".
    // Sets DialogResult to true, indicating success, and closes the dialog so the ViewModel can proceed.
    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Hostname))
        {
            HostnameTextBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    // Triggered when the user clicks "Cancel".
    // Sets DialogResult to false, indicating the operation was cancelled, and closes the dialog without attempting a connection.
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }


    /*
      #############################################################################
                           Password Reveal Handlers 
      #############################################################################
    */

    // Triggered when the user presses the "view password" button.
    // Copies the hidden password into a visible TextBox, then swaps visibility so the password becomes readable.
    private void ViewPassword_MouseDown(object sender, RoutedEventArgs e)
    {
        PasswordTextBox.Text = PasswordBox.Password;

        PasswordBox.Visibility = Visibility.Collapsed;
        PasswordTextBox.Visibility = Visibility.Visible;
    }

    // Triggered when the user releases the "view password" button.
    // Copies the visible password back into the PasswordBox, then restores the hidden (secure) view.
    private void ViewPassword_MouseUp(object sender, RoutedEventArgs e)
    {
        PasswordBox.Password = PasswordTextBox.Text;

        PasswordTextBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
    }

}

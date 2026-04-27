using System.Windows;

namespace DoorSim.Views;

public partial class ConnectWindow : Window
{
    public string Hostname => HostnameTextBox.Text.Trim();

    public string Password => PasswordBox.Password;

    public ConnectWindow()
    {
        InitializeComponent();
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ViewPassword_MouseDown(object sender, RoutedEventArgs e)
    {
        PasswordTextBox.Text = PasswordBox.Password;

        PasswordBox.Visibility = Visibility.Collapsed;
        PasswordTextBox.Visibility = Visibility.Visible;
    }

    private void ViewPassword_MouseUp(object sender, RoutedEventArgs e)
    {
        PasswordBox.Password = PasswordTextBox.Text;

        PasswordTextBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
    }
}

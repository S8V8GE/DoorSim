using System.Windows;

namespace DoorSim.Views;

public partial class AppMessageWindow : Window
{
    public AppMessageWindow(string title, string message)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
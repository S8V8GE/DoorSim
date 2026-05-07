using System.Windows;

namespace DoorSim.Views;

// Simple styled message dialog used by DoorSim.
//
// This is intentionally small. It replaces MessageBox where we want a dark themed, owner-centred modal message inside the application.
public partial class AppMessageWindow : Window
{
    // Creates a message dialog with caller-supplied title and body text.
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
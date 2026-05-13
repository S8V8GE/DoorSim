using System.Windows;
using System.Diagnostics;
using System.Windows.Navigation;

namespace DoorSim.Views;

// About window for DoorSim.
//
// Displays version, purpose, disclaimer, feature summary, and support guidance.
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void LinkedInButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.linkedin.com/in/jamesdavidsavage/",
            UseShellExecute = true
        });
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Opens About-window hyperlinks in the user's default browser.
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });

        e.Handled = true;
    }
}
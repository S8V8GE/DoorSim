using DoorSim.Services;
using DoorSim.ViewModels;
using DoorSim.Views;
using System.Diagnostics;
using System.Windows;
using System.IO;

namespace DoorSim;

// Main application shell.
//
// Responsibilities:
//      - Create and attach MainViewModel.
//      - Provide the main window as owner for modal child windows.
//      - Keep the application window fixed-size.
//      - Resize the fixed window when switching between Single Door and Two Door modes.
//
// Most application behaviour lives in MainViewModel and child ViewModels.
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var mainViewModel = new MainViewModel(
            new SoftwireService(),
            this);

        DataContext = mainViewModel;

        // Listen for view mode changes so the window can jump between the fixed Single Door and Two Door sizes. 
        mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

        // Apply the starting Single Door size.
        ApplyWindowSizeForViewMode(mainViewModel.CurrentViewMode);
    }

    // Handles property changes from MainViewModel. We only care when CurrentViewMode changes.
    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentViewMode))
            return;

        if (sender is not MainViewModel vm)
            return;

        ApplyWindowSizeForViewMode(vm.CurrentViewMode);
    }

    // Applies fixed window sizes for each main view mode.
    //      Single Door: current compact app width
    //      Two Door: wider layout so two interactive door panels can sit side by side
    private void ApplyWindowSizeForViewMode(string viewMode)
    {
        if (viewMode == "TwoDoor")
        {
            Width = 1600;
            MinWidth = 1600;
            MaxWidth = 1600;

            Height = 800;
            MinHeight = 800;
            MaxHeight = 800;
        }
        else
        {
            Width = 800;
            MinWidth = 800;
            MaxWidth = 800;

            Height = 800;
            MinHeight = 800;
            MaxHeight = 800;
        }
    }

    // Opens the DoorSim help guide.
    // The help guide is stored as a PDF in the application's output folder.
    // Windows opens it using the user's default PDF viewer or browser.
    //
    // NOTE:
    // -----
    // The guide must be called "DoorSim_User_Guide.pdf" and be located in a "Help" folder in the application's output directory for this to work.
    // The PDF must be set to:
    //      - Build Action: Content
    //      - Copy to Output Directory: Copy if newer (or Copy always)
    private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var helpFilePath = Path.Combine(AppContext.BaseDirectory, "Help", "DoorSim_User_Guide.pdf");

        if (!File.Exists(helpFilePath))
        {
            MessageBox.Show(
                "The DoorSim help guide could not be found.\n\n" +
                $"Expected location:\n{helpFilePath}",
                "Help guide not found",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = helpFilePath,
            UseShellExecute = true
        });
    }

    // Used for opening the about window from the menu. The about window is modal and owned by the main window.
    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow
        {
            Owner = this
        };

        aboutWindow.ShowDialog();
    }

}
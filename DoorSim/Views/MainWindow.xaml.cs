using System.Windows;
using DoorSim.Services;
using DoorSim.ViewModels;

namespace DoorSim;

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
    //
    // Single Door: current compact app width
    //
    // Two Door: wider layout so two interactive door panels can sit side by side
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

}
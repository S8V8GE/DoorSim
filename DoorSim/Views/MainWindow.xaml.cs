using System.Windows;
using DoorSim.Services;
using DoorSim.ViewModels;

namespace DoorSim;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContext = new MainViewModel(
            new SoftwireService(),
            this);
    }
}
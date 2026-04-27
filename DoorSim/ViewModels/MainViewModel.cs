using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

namespace DoorSim.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string statusText = "Softwire is not connected";
}

using System.Windows;
using System.Windows.Threading;

namespace DoorSim.Views;

public partial class PinEntryWindow : Window
{

    /*
    #############################################################################
                          Public result properties
    #############################################################################
    */

    // Entered PIN returned to the caller when the dialog closes with OK.
    public string EnteredPin { get; private set; } = string.Empty;

    // True when the dialog closes because the Card + PIN countdown expired.
    public bool TimedOut { get; private set; }

    // Reader name shown in the dialog title area.
    public string ReaderName { get; }


    /*
    #############################################################################
                               Internal state
    #############################################################################
    */

    // Countdown timer used only when the dialog is opened for automatic Card + PIN entry.
    private readonly DispatcherTimer _countdownTimer;

    // Remaining seconds allowed for PIN entry when countdown mode is active.
    private int _secondsRemaining;

    // Tracks whether the PIN is currently shown in plain text.
    private bool _isPinVisible;


    /*
    #############################################################################
                         Constructor and initialisation
    #############################################################################
    */

    // Creates a PIN entry dialog.
    // If timeoutSeconds is provided, countdown mode is enabled.
    // If timeoutSeconds is null, the dialog behaves as manual PIN entry with no timer.
    public PinEntryWindow(string readerName, int? timeoutSeconds = null)
    {
        ReaderName = readerName;
        _secondsRemaining = timeoutSeconds ?? 0;

        InitializeComponent();

        DataContext = this;

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _countdownTimer.Tick += CountdownTimer_Tick;

        if (timeoutSeconds.HasValue)
        {
            UpdateTimerText();
            TimerText.Visibility = Visibility.Visible;
            _countdownTimer.Start();
        }
        else
        {
            TimerText.Visibility = Visibility.Collapsed;
        }

        PinPasswordBox.Focus();
    }


    /*
    #############################################################################
                              Countdown timer
    #############################################################################
    */

    // Updates the countdown text shown under the PIN entry box.
    private void UpdateTimerText()
    {
        TimerText.Text = $"Time remaining: {_secondsRemaining} seconds";
    }

    // Handles each countdown tick (If time runs out, the dialog closes without sending a PIN).
    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        _secondsRemaining--;

        UpdateTimerText();

        if (_secondsRemaining > 0)
            return;

        _countdownTimer.Stop();

        TimedOut = true;

        DialogResult = false;
        Close();
    }


    /*
    #############################################################################
                         PIN validation and visibility
    #############################################################################
    */

    // Keeps the hidden PasswordBox in sync when the PIN is visible, then validates the current PIN.
    private void PinPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isPinVisible)
        {
            VisiblePinTextBox.Text = PinPasswordBox.Password;
        }

        ValidatePin(PinPasswordBox.Password);
    }

    // Keeps the hidden PasswordBox in sync when the PIN is visible, then validates the current PIN.
    private void VisiblePinTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isPinVisible)
        {
            PinPasswordBox.Password = VisiblePinTextBox.Text;
        }

        ValidatePin(VisiblePinTextBox.Text);
    }

    // Validates PIN input as the user types (OK is only enabled when the PIN is exactly 4 or 5 digits).
    private void ValidatePin(string pin)
    {
        var isValid =
            (pin.Length == 4 || pin.Length == 5) &&
            pin.All(char.IsDigit);

        OkButton.IsEnabled = isValid;
        ValidationText.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
    }

    // Toggles the PIN between hidden and visible.
    private void TogglePinVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinVisible = !_isPinVisible;

        if (_isPinVisible)
        {
            VisiblePinTextBox.Text = PinPasswordBox.Password;
            VisiblePinTextBox.Visibility = Visibility.Visible;
            PinPasswordBox.Visibility = Visibility.Collapsed;

            TogglePinVisibilityButton.Content = "🙈";

            VisiblePinTextBox.Focus();
            VisiblePinTextBox.CaretIndex = VisiblePinTextBox.Text.Length;
        }
        else
        {
            PinPasswordBox.Password = VisiblePinTextBox.Text;
            PinPasswordBox.Visibility = Visibility.Visible;
            VisiblePinTextBox.Visibility = Visibility.Collapsed;

            TogglePinVisibilityButton.Content = "👁";

            PinPasswordBox.Focus();
        }
    }


    /*
    #############################################################################
                               Dialog buttons
    #############################################################################
    */


    // Cancels PIN entry.
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _countdownTimer.Stop();

        DialogResult = false;
        Close();
    }

    // Accepts PIN entry.
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _countdownTimer.Stop();

        EnteredPin = _isPinVisible
            ? VisiblePinTextBox.Text
            : PinPasswordBox.Password;

        DialogResult = true;
        Close();
    }

}
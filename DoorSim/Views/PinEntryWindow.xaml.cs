using System.Windows;
using System.Windows.Threading;

namespace DoorSim.Views;

public partial class PinEntryWindow : Window
{
    // The PIN entered by the user.
    // Only populated when the dialog closes with OK.
    public string EnteredPin { get; private set; } = string.Empty;

    // True when the dialog closes because the PIN countdown expired.
    public bool TimedOut { get; private set; }

    // Name of the reader shown in the dialog title area.
    public string ReaderName { get; }

    // Countdown timer used for Card + PIN entry.
    private readonly DispatcherTimer _countdownTimer;

    // Remaining time allowed for PIN entry.
    private int _secondsRemaining;

    // For visibilty of PIN in the UI 
    private bool _isPinVisible;

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

    // Updates the countdown text shown under the PIN entry box.
    private void UpdateTimerText()
    {
        TimerText.Text = $"Time remaining: {_secondsRemaining} seconds";
    }

    // Handles each countdown tick.
    // If time runs out, the dialog closes without sending a PIN.
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

    // Keeps the visible PIN box in sync with the hidden PasswordBox,
    // then validates the current PIN.
    private void PinPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isPinVisible)
        {
            VisiblePinTextBox.Text = PinPasswordBox.Password;
        }

        ValidatePin(PinPasswordBox.Password);
    }

    // Keeps the hidden PasswordBox in sync when the PIN is visible,
    // then validates the current PIN.
    private void VisiblePinTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isPinVisible)
        {
            PinPasswordBox.Password = VisiblePinTextBox.Text;
        }

        ValidatePin(VisiblePinTextBox.Text);
    }

    // Validates PIN input as the user types.
    // OK is only enabled when the PIN is exactly 4 or 5 digits.
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
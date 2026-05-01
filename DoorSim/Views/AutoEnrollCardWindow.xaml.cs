using DoorSim.Models;
using DoorSim.Services;
using System.Reflection.PortableExecutable;
using System.Windows;
using System.Windows.Controls;

namespace DoorSim.Views;

public partial class AutoEnrollCardWindow : Window
{
    /*
      #############################################################################
                              Public result properties
      #############################################################################
    */

    // Generated credential returned to the caller when the dialog closes with Enrol.
    public CredentialFormatResult? Result { get; private set; }

    // Name of the reader shown in the dialog title area.
    public string ReaderName { get; }


    /*
      #############################################################################
                         Constructor and initialisation
      #############################################################################
    */

    // Creates the auto-enrol card dialog for a specific reader.
    public AutoEnrollCardWindow(string readerName)
    {
        ReaderName = readerName;

        InitializeComponent();

        DataContext = this;

        FacilityCodeTextBox.Focus();

        UpdateFormatPanels();
        ValidateAndPreviewCredential();
    }


    /*
      #############################################################################
                         Credential validation and preview
      #############################################################################
    */

    // Shows the correct input fields for the selected credential format.
    private void UpdateFormatPanels()
    {
        if (FormatComboBox == null ||
            FacilityCodePanel == null ||
            CardNumberPanel == null ||
            H10306Panel == null ||
            H10302Panel == null ||
            H10304Panel == null ||
            Corporate1000_35Panel == null ||
            Corporate1000_48Panel == null ||
            Csn32Panel == null ||
            FascN75Panel == null ||
            FascN200Panel == null ||
            CustomFormatPanel == null)
        {
            return;
        }

        var selectedIndex = FormatComboBox.SelectedIndex;

        var isStandard26 = selectedIndex == 0;
        var isH10306 = selectedIndex == 1;
        var isH10302 = selectedIndex == 2;
        var isH10304 = selectedIndex == 3;
        var isCorporate1000_35 = selectedIndex == 4;
        var isCorporate1000_48 = selectedIndex == 5;
        var isCsn32 = selectedIndex == 6;
        var isFascN75 = selectedIndex == 7;
        var isFascN200 = selectedIndex == 8;
        var isCustomFormat = selectedIndex == 9;

        FacilityCodePanel.Visibility = isStandard26 ? Visibility.Visible : Visibility.Collapsed;
        CardNumberPanel.Visibility = isStandard26 ? Visibility.Visible : Visibility.Collapsed;

        H10306Panel.Visibility = isH10306 ? Visibility.Visible : Visibility.Collapsed;

        H10302Panel.Visibility = isH10302 ? Visibility.Visible : Visibility.Collapsed;

        H10304Panel.Visibility = isH10304 ? Visibility.Visible : Visibility.Collapsed;

        Corporate1000_35Panel.Visibility = isCorporate1000_35 ? Visibility.Visible : Visibility.Collapsed;

        Corporate1000_48Panel.Visibility = isCorporate1000_48 ? Visibility.Visible : Visibility.Collapsed;

        Csn32Panel.Visibility = isCsn32 ? Visibility.Visible : Visibility.Collapsed;

        FascN75Panel.Visibility = isFascN75 ? Visibility.Visible : Visibility.Collapsed;

        FascN200Panel.Visibility = isFascN200 ? Visibility.Visible : Visibility.Collapsed;

        CustomFormatPanel.Visibility = isCustomFormat ? Visibility.Visible : Visibility.Collapsed;
    }


    // Revalidates the form if the selected credential format changes.
    private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFormatPanels();
        ValidateAndPreviewCredential();
    }


    // Revalidates the selected format whenever the user changes a credential field.
    private void CredentialField_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateAndPreviewCredential();
    }


    // Validates the current input fields and previews the generated raw credential.
    private void ValidateAndPreviewCredential()
    {
        // During InitializeComponent(), some events can fire before all controls exist.
        // If that happens, ignore validation until the window is fully loaded.
        if (EnrollButton == null ||
            GeneratedRawHexTextBox == null ||
            FacilityCodeTextBox == null ||
            CardNumberTextBox == null ||
            H10306SiteCodeTextBox == null ||
            H10306CardNumberTextBox == null ||
            H10302CardNumberTextBox == null ||
            H10304FacilityCodeTextBox == null ||
            H10304CardNumberTextBox == null ||
            Corporate1000_35FacilityCodeTextBox == null ||
            Corporate1000_35CardNumberTextBox == null ||
            Corporate1000_48FacilityCodeTextBox == null ||
            Corporate1000_48CardNumberTextBox == null ||
            Csn32RawHexTextBox == null ||
            FascN75AgencyCodeTextBox == null ||
            FascN75SystemCodeTextBox == null ||
            FascN75CredentialNumberTextBox == null ||
            FascN75ExpDateTextBox == null ||
            FascN200AgencyCodeTextBox == null ||
            FascN200SystemCodeTextBox == null ||
            FascN200CredentialNumberTextBox == null ||
            FascN200CsTextBox == null ||
            FascN200IciTextBox == null ||
            FascN200PiTextBox == null ||
            FascN200OcTextBox == null ||
            FascN200OiTextBox == null ||
            FascN200PoaTextBox == null ||
            FascN200LrcTextBox == null ||
            CustomBitCountTextBox == null ||
            CustomRawHexTextBox == null ||
            ValidationText == null ||
            FormatComboBox == null)
        {
            return;
        }

        Result = null;
        EnrollButton.IsEnabled = false;
        GeneratedRawHexTextBox.Text = string.Empty;

        var selectedIndex = FormatComboBox.SelectedIndex;

        // Standard 26-bit Wiegand / HID H10301
        if (selectedIndex == 0)
        {
            if (!int.TryParse(FacilityCodeTextBox.Text, out var facilityCode))
            {
                ValidationText.Text = "Error: Facility Code must be a number between 1 and 255.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(CardNumberTextBox.Text, out var cardNumber))
            {
                ValidationText.Text = "Error: Card Number must be a number between 1 and 65535.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            try
            {
                var result = CredentialFormatService.CreateStandard26BitWiegand(
                    facilityCode,
                    cardNumber);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                ValidationText.Text = "Credential is valid.";
                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;

                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate credential from the entered values.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }

        // HID H10306 34-bit
        if (selectedIndex == 1)
        {
            if (!int.TryParse(H10306SiteCodeTextBox.Text, out var siteCode))
            {
                ValidationText.Text = "Error: Facility Code must be a number between 1 and 65535.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(H10306CardNumberTextBox.Text, out var cardNumber))
            {
                ValidationText.Text = "Error: Card Number must be a number between 1 and 65535.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            try
            {
                var result = CredentialFormatService.CreateH10306_34Bit(
                    siteCode,
                    cardNumber);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                ValidationText.Text = "Credential is valid.";
                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;

                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate HID H10306 credential from the entered values.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }

        // HID H10302 37-bit
        if (selectedIndex == 2)
        {
            if (!long.TryParse(H10302CardNumberTextBox.Text, out var cardNumber))
            {
                ValidationText.Text = "Error: Card Number must be a number between 1 and 34,359,738,367.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            try
            {
                var result = CredentialFormatService.CreateH10302_37Bit(cardNumber);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                ValidationText.Text = "Credential is valid.";
                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;

                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate HID H10302 credential from the entered value.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }

        // HID H10304 37-bit
        if (selectedIndex == 3)
        {
            if (!int.TryParse(H10304FacilityCodeTextBox.Text, out var facilityCode))
            {
                ValidationText.Text = "Error: Facility Code must be a number between 1 and 65535.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(H10304CardNumberTextBox.Text, out var cardNumber))
            {
                ValidationText.Text = "Error: Card Number must be a number between 1 and 524287.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            try
            {
                var result = CredentialFormatService.CreateH10304_37Bit(
                    facilityCode,
                    cardNumber);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                ValidationText.Text = "Credential is valid.";
                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;

                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate HID H10304 credential from the entered values.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }

        // HID Corporate 1000 35-bit
        if (selectedIndex == 4)
        {
            if (!int.TryParse(Corporate1000_35FacilityCodeTextBox.Text, out var facilityCode))
            {
                ValidationText.Text = "Error: Facility Code must be a number between 1 and 4095.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(Corporate1000_35CardNumberTextBox.Text, out var cardNumber))
            {
                ValidationText.Text = "Error: Card Number must be a number between 1 and 1048575.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            try
            {
                var result = CredentialFormatService.CreateCorporate1000_35Bit(
                    facilityCode,
                    cardNumber);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                ValidationText.Text = "Credential is valid.";
                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;

                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate HID Corporate 1000 35-bit credential from the entered values.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }

        // HID Corporate 1000 48-bit
        if (selectedIndex == 5)
        {
            if (!int.TryParse(Corporate1000_48FacilityCodeTextBox.Text, out var facilityCode))
            {
                ValidationText.Text = "Error: Facility Code must be a number between 1 and 4,194,303.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(Corporate1000_48CardNumberTextBox.Text, out var cardNumber))
            {
                ValidationText.Text = "Error: Card Number must be a number between 1 and 8,388,607.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            try
            {
                var result = CredentialFormatService.CreateCorporate1000_48Bit(
                    facilityCode,
                    cardNumber);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                ValidationText.Text = "Credential is valid.";
                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;

                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate HID Corporate 1000 48-bit credential from the entered values.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }

        // CSN 32-bit UID
        if (selectedIndex == 6)
        {
            try
            {
                var result = CredentialFormatService.CreateCsn32Bit(
                    Csn32RawHexTextBox.Text);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                var cleanHex = Csn32RawHexTextBox.Text
                    .Trim()
                    .Replace(" ", "")
                    .Replace("-", "");

                if (cleanHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    cleanHex = cleanHex[2..];
                }

                if (cleanHex.Length < 8)
                {
                    ValidationText.Text = "Credential is valid. CSN will be left-padded to 8 hex characters.";
                }
                else
                {
                    ValidationText.Text = "Credential is valid.";
                }

                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;
                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate CSN credential from the entered value.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }

        // FASC-N 75-bit
        if (selectedIndex == 7)
        {
            if (!int.TryParse(FascN75AgencyCodeTextBox.Text, out var agencyCode))
            {
                ValidationText.Text = "Error: Agency Code must be a number between 1 and 16383.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN75SystemCodeTextBox.Text, out var systemCode))
            {
                ValidationText.Text = "Error: System Code must be a number between 1 and 16383.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN75CredentialNumberTextBox.Text, out var credentialNumber))
            {
                ValidationText.Text = "Error: Credential Number must be a number between 1 and 1048575.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN75ExpDateTextBox.Text, out var expDate))
            {
                ValidationText.Text = "Error: Exp Date must be a number between 1 and 33554431.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            try
            {
                var result = CredentialFormatService.CreateFascN_75Bit(
                    agencyCode,
                    systemCode,
                    credentialNumber,
                    expDate);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                ValidationText.Text = "Credential is valid.";
                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;

                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate FASC-N 75-bit credential from the entered values.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }

        // FASC-N 200-bit
        if (selectedIndex == 8)
        {
            if (!int.TryParse(FascN200AgencyCodeTextBox.Text, out var agencyCode))
            {
                ValidationText.Text = "Error: Agency Code must be a number between 1 and 9999.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN200SystemCodeTextBox.Text, out var systemCode))
            {
                ValidationText.Text = "Error: System Code must be a number between 1 and 9999.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN200CredentialNumberTextBox.Text, out var credentialNumber))
            {
                ValidationText.Text = "Error: Credential Number must be a number between 1 and 999999.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN200CsTextBox.Text, out var cs))
            {
                ValidationText.Text = "Error: CS must be a number between 1 and 9.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN200IciTextBox.Text, out var ici))
            {
                ValidationText.Text = "Error: ICI must be a number between 1 and 9.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!long.TryParse(FascN200PiTextBox.Text, out var pi))
            {
                ValidationText.Text = "Error: PI must be a number between 1 and 9999999999.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN200OcTextBox.Text, out var oc))
            {
                ValidationText.Text = "Error: OC must be a number between 1 and 9.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN200OiTextBox.Text, out var oi))
            {
                ValidationText.Text = "Error: OI must be a number between 1 and 9999.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            if (!int.TryParse(FascN200PoaTextBox.Text, out var poa))
            {
                ValidationText.Text = "Error: POA must be a number between 1 and 9.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            var lrcText = FascN200LrcTextBox.Text.Trim();

            if (lrcText.Length != 1)
            {
                ValidationText.Text = "Error: LRC must be a single character from 1–9 or A–F.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            try
            {
                var result = CredentialFormatService.CreateFascN_200Bit(
                    agencyCode,
                    systemCode,
                    credentialNumber,
                    cs,
                    ici,
                    pi,
                    oc,
                    oi,
                    poa,
                    lrcText[0]);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                ValidationText.Text = "Credential is valid.";
                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;

                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch (ArgumentException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate FASC-N 200-bit credential from the entered values.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }

        // Custom card format
        if (selectedIndex == 9)
        {
            if (!int.TryParse(CustomBitCountTextBox.Text, out var bitCount))
            {
                ValidationText.Text = "Error: Bit Count must be a number between 8 and 512.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            try
            {
                var result = CredentialFormatService.CreateCustomRawCredential(
                    CustomRawHexTextBox.Text,
                    bitCount);

                Result = result;
                GeneratedRawHexTextBox.Text = $"{result.RawHex}  ({result.BitCount} bits)";

                var bytesNeeded = (int)Math.Ceiling(bitCount / 8.0);
                var hexCharsNeeded = bytesNeeded * 2;

                var cleanHex = CustomRawHexTextBox.Text
                    .Trim()
                    .Replace(" ", "")
                    .Replace("-", "");

                if (cleanHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    cleanHex = cleanHex[2..];
                }

                if (cleanHex.Length < hexCharsNeeded)
                {
                    ValidationText.Text = $"Credential is valid. Raw hex will be left-padded to {hexCharsNeeded} hex characters for {bitCount} bits.";
                }
                else
                {
                    ValidationText.Text = "Credential is valid.";
                }

                ValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;
                EnrollButton.IsEnabled = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch (ArgumentException ex)
            {
                ValidationText.Text = $"Error: {ex.Message}";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            catch
            {
                ValidationText.Text = "Error: Unable to generate credential from the entered values.";
                ValidationText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            return;
        }
    }


    /*
      #############################################################################
                                  Dialog buttons
      #############################################################################
    */

    // Cancels auto-enrol.
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // Accepts the generated credential and returns it to the caller.
    private void EnrollButton_Click(object sender, RoutedEventArgs e)
    {
        if (Result == null)
            return;

        DialogResult = true;
        Close();
    }
}
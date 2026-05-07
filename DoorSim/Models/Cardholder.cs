namespace DoorSim.Models;

// Represents one cardholder credential retrieved from the Directory SQL database.
//
// Used by:
//      - CardholdersViewModel for filtering/searching.
//      - CardholdersView as the displayed row.
//      - DoorPanelView drag/drop as the payload presented to a reader.
public class Cardholder
{
    // Display name of the cardholder.
    public string CardholderName { get; set; } = string.Empty;

    // Credential name/label from Security Center, if available.
    public string CredentialName { get; set; } = string.Empty;

    // Raw credential value as returned by SQL.
    public string RawCredential { get; set; } = string.Empty;

    // Credential value normalised for Softwire SwipeRaw.
    public string TrimmedCredential { get; set; } = string.Empty;

    // Number of valid bits in TrimmedCredential.
    public int BitCount { get; set; }

    // True when the cardholder has a PIN configured.
    public bool HasPin { get; set; }

}
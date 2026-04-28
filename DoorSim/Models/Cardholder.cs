namespace DoorSim.Models;

// Represents one cardholder credential retrieved from the Directory SQL database.
// This is shown in the Cardholders panel and later used when dragging a cardholder onto a reader.
public class Cardholder
{
    public string CardholderName { get; set; } = string.Empty;

    public string CredentialName { get; set; } = string.Empty;

    public string RawCredential { get; set; } = string.Empty;

    public string TrimmedCredential { get; set; } = string.Empty;

    public int BitCount { get; set; }

    public bool HasPin { get; set; }
}
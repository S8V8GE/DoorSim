namespace DoorSim.Models;

// Represents a generated credential ready to send to Softwire.
//
// Auto-enrol card formats eventually produce:
// - RawHex: the raw hexadecimal credential value
// - BitCount: the number of valid bits in the credential
//
// This can then be sent using SwipeRawAsync(readerPath, RawHex, BitCount).
public class CredentialFormatResult
{
    public string RawHex { get; set; } = string.Empty;

    public int BitCount { get; set; }
}

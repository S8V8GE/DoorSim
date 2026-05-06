namespace DoorSim.Models;

// Represents a generated credential ready to send to a Softwire reader.
//
// Produced by CredentialFormatService and returned by AutoEnrollCardWindow.
// The caller sends this using SwipeRawAsync(readerPath, RawHex, BitCount).
public class CredentialFormatResult
{
    // Raw hexadecimal credential value to send to Softwire.
    public string RawHex { get; set; } = string.Empty;

    // Number of valid bits represented by RawHex.
    public int BitCount { get; set; }

}

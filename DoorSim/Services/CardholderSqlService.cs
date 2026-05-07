using DoorSim.Models;
using Microsoft.Data.SqlClient;

namespace DoorSim.Services;

// Service responsible for retrieving cardholder credential data from the Genetec Directory SQL database.
//
// Used by MainViewModel/CardholdersViewModel to populate the Cardholders panel.
//
// Notes:
//      - This is currently intended for local Softwire/demo environments.
//      - It reads directly from the Directory database.
//      - It does not modify SQL data.
public class CardholderSqlService
{
    /*
      #############################################################################
                            Connection Configuration
      #############################################################################
    */

    // Local SQL Server instance used by the Softwire training/demo machine.
    //
    // TODO (Maybe One-Day...): Move this to configuration if DoorSim needs to support non-local SQL Server instances or different Directory database names.
    private readonly string _connectionString =
        "Server=localhost\\SQLEXPRESS;Database=Directory;Trusted_Connection=True;TrustServerCertificate=True;";


    /*
      #############################################################################
                            Getting the Cardholders
      #############################################################################
    */

    // Retrieves cardholder credential rows from the Directory database.
    //
    // Returns:
    //      - one Cardholder model per card credential row,
    //      - with HasPin indicating whether the cardholder also has a PIN credential.
    //
    // The returned credential is normalised into TrimmedCredential so it can be displayed and later sent to Softwire during drag/drop.
    public async Task<List<Cardholder>> GetCardholdersAsync()
    {
        // Returns one row per card credential, not necessarily one row per person.
        // A cardholder with multiple card credentials may appear multiple times.
        var results = new List<Cardholder>();

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Retrieves cardholders, card credentials, and a derived HasPin flag.
        //
        // Credential.UniqueID appears to store card credentials as:
        //     <raw credential>|<bit count>
        //
        // The main credential join only includes UniqueID values that contain a pipe and have a numeric bit count after the pipe.
        //
        // HasPin is derived by checking whether the cardholder has another credential that does not match the raw-card format and is not an ALPR plate credential.
        //
        // NOTE: 
        // HasPin: Treats a cardholder as having a PIN if they have another credential record that does not look like a raw card credential(< hex >|< bitcount >) and is not an ALPR plate credential.
        var query = @"
                    SELECT
                        ce.Name AS CardholderName,
                        cre.Name AS CredentialName,
                        LEFT(cr.UniqueID, CHARINDEX('|', cr.UniqueID) - 1) AS RawCredential,
                        RIGHT(cr.UniqueID, LEN(cr.UniqueID) - CHARINDEX('|', cr.UniqueID)) AS BitCount,
                        CASE
                            WHEN EXISTS (
                                SELECT 1
                                FROM Credential crCheck
                                WHERE crCheck.Cardholder = c.Guid
                                  AND NOT (
                                        CHARINDEX('|', crCheck.UniqueID) > 0
                                        AND TRY_CONVERT(int, RIGHT(crCheck.UniqueID, LEN(crCheck.UniqueID) - CHARINDEX('|', crCheck.UniqueID))) IS NOT NULL
                                  )
                                  AND crCheck.UniqueID NOT LIKE 'Plate%'
                            )
                            THEN 1
                            ELSE 0
                        END AS HasPin
                    FROM Cardholder c
                    LEFT JOIN Entity ce
                        ON c.Guid = ce.Guid
                    LEFT JOIN Credential cr
                        ON c.Guid = cr.Cardholder
                        AND CHARINDEX('|', cr.UniqueID) > 0
                        AND TRY_CONVERT(int, RIGHT(cr.UniqueID, LEN(cr.UniqueID) - CHARINDEX('|', cr.UniqueID))) IS NOT NULL
                    LEFT JOIN Entity cre
                        ON cr.Guid = cre.Guid
                    ORDER BY ce.Name
                    ";

        using var command = new SqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var rawCredential = reader["RawCredential"]?.ToString() ?? "";

            var bitCount = reader["BitCount"] != DBNull.Value
                ? Convert.ToInt32(reader["BitCount"])
                : 0;

            results.Add(new Cardholder
            {
                CardholderName = reader["CardholderName"]?.ToString() ?? "",
                CredentialName = reader["CredentialName"]?.ToString() ?? "",

                // Keep original SQL value for debugging/reference
                RawCredential = string.IsNullOrWhiteSpace(rawCredential)
                    ? "No credential"
                    : rawCredential,

                // This is the value we display and later use for drag/drop reads
                TrimmedCredential = TrimCredentialToBitCount(rawCredential, bitCount),

                BitCount = bitCount,

                HasPin = reader["HasPin"] != DBNull.Value &&
                         Convert.ToInt32(reader["HasPin"]) == 1
            });
        }

        return results;
    }


    /*
      #############################################################################
                                 Helpers
      #############################################################################
    */

    // Trims a raw hexadecimal credential to the minimum whole-byte length required by its bit count.
    //
    // Example:
    //      - 37 bits requires 5 bytes.
    //      - 5 bytes requires 10 hex characters.
    //
    // Security Center / SQL may return credentials with leading padding.
    // Softwire SwipeRaw expects the credential value aligned to the supplied bit count, so DoorSim keeps the rightmost required bytes.
    private string TrimCredentialToBitCount(string rawCredential, int bitCount)
    {
        if (string.IsNullOrWhiteSpace(rawCredential) || bitCount <= 0)
            return "No credential";

        var cleanHex = rawCredential
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "")
            .Replace("-", "")
            .ToUpperInvariant();

        var bytesNeeded = (int)Math.Ceiling(bitCount / 8.0);
        var hexCharsNeeded = bytesNeeded * 2;

        if (cleanHex.Length <= hexCharsNeeded)
            return cleanHex;

        return cleanHex.Substring(cleanHex.Length - hexCharsNeeded);
    }

}
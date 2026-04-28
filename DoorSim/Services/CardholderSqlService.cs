using DoorSim.Models;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DoorSim.Services;

// Service responsible for retrieving cardholder data from the Directory SQL database.
public class CardholderSqlService
{
    // Connection string for local SQL Server (Softwire machine)
    private readonly string _connectionString =
    "Server=localhost\\SQLEXPRESS;Database=Directory;Trusted_Connection=True;TrustServerCertificate=True;";

    // Retrieves all cardholders and their credentials from SQL
    public async Task<List<Cardholder>> GetCardholdersAsync()
    {
        var results = new List<Cardholder>();

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // This is the SQL query that retrieves cardholder information, including their credentials and whether they have a PIN.
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

    // Trims a credential to the minimum whole-byte length required by its bit count.
    // Example: 37 bits -> 5 bytes -> 10 hex characters.
    private string TrimCredentialToBitCount(string rawCredential, int bitCount)
    {
        if (string.IsNullOrWhiteSpace(rawCredential) || bitCount <= 0)
            return "No credential";

        var cleanHex = rawCredential
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "")
            .ToUpper();

        var bytesNeeded = (int)Math.Ceiling(bitCount / 8.0);
        var hexCharsNeeded = bytesNeeded * 2;

        if (cleanHex.Length <= hexCharsNeeded)
            return cleanHex;

        return cleanHex.Substring(cleanHex.Length - hexCharsNeeded);
    }
}
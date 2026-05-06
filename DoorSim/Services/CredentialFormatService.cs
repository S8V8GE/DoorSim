using DoorSim.Models;

namespace DoorSim.Services;

// Converts user-entered credential format fields into raw credential data that Softwire can receive through SwipeRawAsync.
//
// Responsibilities:
//      - validate format-specific field ranges,
//      - calculate Wiegand/FASC-N parity where required,
//      - pack fields into the correct bit layout,
//      - return raw hexadecimal credential data and bit count.
//
// This service contains credential encoding logic only. UI validation messages live in AutoEnrollCardWindow.
//
// This service is UI-agnostic. It does not know about ComboBox indexes or visible panels. Callers choose the appropriate method for the selected format.
public static class CredentialFormatService
{

    /*
      #############################################################################
                            Standard 26-bit Wiegand (H10301)
      #############################################################################
    */
    //
    // Converts Standard 26-bit Wiegand / H10301 fields into raw hex.
    //
    // Format:
    //      - 1 leading even parity bit over the first 12 data bits
    //      - 8-bit facility code (1–255 allowed in this app; 0 is reserved for PIN usage)
    //      - 16-bit card number (1–65535 allowed in this app; 0 is reserved/avoided)
    //      - 1 trailing odd parity bit over the last 12 data bits
    //
    // Returns:
    //      - RawHex padded to 32 hex characters
    //      - BitCount = 26
    public static CredentialFormatResult CreateStandard26BitWiegand(int facilityCode, int cardNumber)
    {

        // I'm blocking off "0" as a value for both fields since it is used for PINs (an FC of 0 tells SC it's a PIN).
        if (facilityCode < 1 || facilityCode > 255)
            throw new ArgumentOutOfRangeException(nameof(facilityCode), "Facility code must be between 1 and 255.");

        if (cardNumber < 1 || cardNumber > 65535)
            throw new ArgumentOutOfRangeException(nameof(cardNumber), "Card number must be between 1 and 65535.");

        var data24 = (facilityCode << 16) | cardNumber;

        var first12 = (data24 >> 12) & 0xFFF;
        var last12 = data24 & 0xFFF;

        var first12Ones = CountSetBits(first12);
        var last12Ones = CountSetBits(last12);

        // Leading parity: even parity over the first 12 data bits.
        var p1 = first12Ones % 2 == 0 ? 0 : 1;

        // Trailing parity: odd parity over the last 12 data bits.
        var p2 = last12Ones % 2 == 1 ? 0 : 1;

        var full26 = (p1 << 25) | (data24 << 1) | p2;

        return new CredentialFormatResult
        {
            RawHex = full26.ToString("X8").PadLeft(32, '0'),
            BitCount = 26
        };
    }

    
    /*
      #############################################################################
                                   H10306 (34-bits)
      #############################################################################
    */
    //
    // Converts HID H10306 / Standard 34-bit Wiegand fields into raw hex.
    //
    // Format:
    //      - 1 leading even parity bit
    //      - 16-bit site code
    //      - 16-bit card number
    //      - 1 trailing odd parity bit
    //
    // Field ranges used by this app:
    //      - Site Code:   1–65535
    //      - Card Number: 1–65535
    //
    // Note:
    // Security Center documents the field range as 0–65535, but DoorSim blocks 0
    // for card auto-enrolment because facility/site/card number 0 is commonly
    // reserved or avoided, and facility code 0 is already used by PIN simulation.
    public static CredentialFormatResult CreateH10306_34Bit(int siteCode, int cardNumber)
    {
        if (siteCode < 1 || siteCode > 65535)
            throw new ArgumentOutOfRangeException(nameof(siteCode), "Facility Code must be between 1 and 65535.");

        if (cardNumber < 1 || cardNumber > 65535)
            throw new ArgumentOutOfRangeException(nameof(cardNumber), "Card Number must be between 1 and 65535.");

        // 32 data bits: 16-bit site code followed by 16-bit card number.
        var data32 = ((long)siteCode << 16) | (uint)cardNumber;

        // Even parity over the first 16 data bits.
        var first16 = (int)((data32 >> 16) & 0xFFFF);

        // Odd parity over the last 16 data bits.
        var last16 = (int)(data32 & 0xFFFF);

        var first16Ones = CountSetBits(first16);
        var last16Ones = CountSetBits(last16);

        // Leading parity: even parity over bits 1–16.
        var p1 = first16Ones % 2 == 0 ? 0L : 1L;

        // Trailing parity: odd parity over bits 17–33.
        var p2 = last16Ones % 2 == 1 ? 0L : 1L;

        var full34 = (p1 << 33) | (data32 << 1) | p2;

        return new CredentialFormatResult
        {
            // 34 bits requires 5 bytes, so pad to 10 hex characters.
            RawHex = full34.ToString("X").PadLeft(10, '0'),
            BitCount = 34
        };
    }


    /*
      #############################################################################
                                   H10302 (37-bits)
      #############################################################################
    */
    //
    // Converts HID H10302 / 37-bit Wiegand fields into raw hex.
    //
    // Format:
    //      - 1 leading even parity bit
    //      - 35-bit card number
    //      - 1 trailing odd parity bit
    //
    // H10302 has no facility/site code.
    // The card number range is 1–34,359,738,367 in this app.
    //
    // Parity:
    //      - Leading parity gives even parity over the first 18 data bits.
    //      - Trailing parity gives odd parity over the last 18 data bits.
    public static CredentialFormatResult CreateH10302_37Bit(long cardNumber)
    {
        const long maxCardNumber = 34359738367; // 35 bits, all data bits set.

        if (cardNumber < 1 || cardNumber > maxCardNumber)
            throw new ArgumentOutOfRangeException(nameof(cardNumber), "Card Number must be between 1 and 34,359,738,367.");

        // Card number is the 35-bit data field.
        var data35 = cardNumber;

        // First 18 data bits, counted from the most significant side of the 35-bit value.
        var first18 = (int)((data35 >> 17) & 0x3FFFF);

        // Last 18 data bits, counted from the least significant side of the 35-bit value.
        var last18 = (int)(data35 & 0x3FFFF);

        var first18Ones = CountSetBits(first18);
        var last18Ones = CountSetBits(last18);

        // Leading parity: even parity over the first 18 data bits.
        var p1 = first18Ones % 2 == 0 ? 0L : 1L;

        // Trailing parity: odd parity over the last 18 data bits.
        var p2 = last18Ones % 2 == 1 ? 0L : 1L;

        var full37 = (p1 << 36) | (data35 << 1) | p2;

        return new CredentialFormatResult
        {
            // 37 bits requires 5 bytes, so pad to 10 hex characters.
            RawHex = full37.ToString("X").PadLeft(10, '0'),
            BitCount = 37
        };
    }


    /*
      #############################################################################
                                   H10304 (37-bits)
      #############################################################################
    */
    //
    // Converts HID H10304 / 37-bit Wiegand fields into raw hex.
    //
    // Format:
    //      - 1 leading even parity bit
    //      - 16-bit facility code
    //      - 19-bit card number
    //      - 1 trailing odd parity bit
    //
    // Field ranges used by this app:
    //      - Facility Code: 1–65535
    //      - Card Number:   1–524287
    //
    // Parity:
    //      - Leading parity gives even parity over the 16 facility code bits.
    //      - Trailing parity gives odd parity over the 19 card number bits.
    public static CredentialFormatResult CreateH10304_37Bit(int facilityCode, int cardNumber)
    {
        if (facilityCode < 1 || facilityCode > 65535)
            throw new ArgumentOutOfRangeException(nameof(facilityCode), "Facility Code must be between 1 and 65535.");

        if (cardNumber < 1 || cardNumber > 524287)
            throw new ArgumentOutOfRangeException(nameof(cardNumber), "Card Number must be between 1 and 524287.");

        // 35 data bits:
        // - 16-bit facility code
        // - 19-bit card number
        var data35 = ((long)facilityCode << 19) | (uint)cardNumber;

        var facilityCodeBits = facilityCode & 0xFFFF;
        var cardNumberBits = cardNumber & 0x7FFFF;

        var facilityCodeOnes = CountSetBits(facilityCodeBits);
        var cardNumberOnes = CountSetBits(cardNumberBits);

        // Leading parity: even parity over the facility code bits.
        var p1 = facilityCodeOnes % 2 == 0 ? 0L : 1L;

        // Trailing parity: odd parity over the card number bits.
        var p2 = cardNumberOnes % 2 == 1 ? 0L : 1L;

        var full37 = (p1 << 36) | (data35 << 1) | p2;

        return new CredentialFormatResult
        {
            // 37 bits requires 5 bytes, so pad to 10 hex characters.
            RawHex = full37.ToString("X").PadLeft(10, '0'),
            BitCount = 37
        };
    }


    /*
      #############################################################################
                               HID Corporate 1000 (35-bits)
      #############################################################################
    */
    //
    // Converts HID Corporate 1000 / 35-bit Wiegand fields into raw hex.
    //
    // Format:
    //      - 1 leading parity bit
    //      - 1 fixed Corporate 1000 format bit
    //      - 12-bit Facility Code
    //      - 20-bit Card Number
    //      - 1 trailing parity bit
    //
    // Field ranges used by this app:
    //      - Facility Code: 1–4095
    //      - Card Number:   1–1048575
    public static CredentialFormatResult CreateCorporate1000_35Bit(int facilityCode, int cardNumber)
    {
        if (facilityCode < 1 || facilityCode > 4095)
            throw new ArgumentOutOfRangeException(nameof(facilityCode), "Facility Code must be between 1 and 4095.");

        if (cardNumber < 1 || cardNumber > 1048575)
            throw new ArgumentOutOfRangeException(nameof(cardNumber), "Card Number must be between 1 and 1048575.");

        var facilityBits = facilityCode & 0xFFF;
        var cardBits = cardNumber & 0xFFFFF;

        var facilityOnes = CountSetBits(facilityBits);
        var cardOnes = CountSetBits(cardBits);

        // Leading parity: even parity over the facility code bits.
        var p1 = facilityOnes % 2 == 0 ? 0L : 1L;

        // Trailing parity: even parity over the card number bits.
        var p2 = cardOnes % 2 == 0 ? 0L : 1L;

        // Corporate 1000 35-bit layout:
        // bit 34      = leading parity
        // bit 33      = fixed Corporate 1000 format bit, always 1
        // bits 32–21  = 12-bit facility code
        // bits 20–1   = 20-bit card number
        // bit 0       = trailing parity
        var full35 =
            (p1 << 34) |
            (1L << 33) |
            ((long)facilityBits << 21) |
            ((long)cardBits << 1) |
            p2;

        return new CredentialFormatResult
        {
            // 35 bits requires 5 bytes, so pad to 10 hex characters.
            RawHex = full35.ToString("X").PadLeft(10, '0'),
            BitCount = 35
        };
    }


    /*
      #############################################################################
                               HID Corporate 1000 (48-bits)
      #############################################################################
    */
    //
    // Converts HID Corporate 1000 / 48-bit Wiegand fields into raw hex.
    //
    // Format fields used by Security Center:
    //      - 22-bit Facility Code
    //      - 23-bit Card Number
    //
    // Field ranges used by this app:
    //      - Facility Code: 1–4,194,303
    //      - Card Number:   1–8,388,607
    //
    // Bit layout verified against Security Center examples:
    //      - Bit 47     = odd parity over the 23-bit card number
    //      - Bit 46     = even parity over the 22-bit facility code
    //      - Bits 45–24 = 22-bit facility code
    //      - Bits 23–1  = 23-bit card number
    //      - Bit 0      = fixed 0
    public static CredentialFormatResult CreateCorporate1000_48Bit(int facilityCode, int cardNumber)
    {
        if (facilityCode < 1 || facilityCode > 4194303)
            throw new ArgumentOutOfRangeException(nameof(facilityCode), "Facility Code must be between 1 and 4,194,303.");

        if (cardNumber < 1 || cardNumber > 8388607)
            throw new ArgumentOutOfRangeException(nameof(cardNumber), "Card Number must be between 1 and 8,388,607.");

        var facilityBits = facilityCode & 0x3FFFFF; // 22 bits
        var cardBits = cardNumber & 0x7FFFFF;       // 23 bits

        var facilityOnes = CountSetBits(facilityBits);
        var cardOnes = CountSetBits(cardBits);

        // Bit 47: odd parity over the card number bits.
        var p1 = cardOnes % 2 == 1 ? 0L : 1L;

        // Bit 46: even parity over the facility code bits.
        var p2 = facilityOnes % 2 == 0 ? 0L : 1L;

        var full48 =
            (p1 << 47) |
            (p2 << 46) |
            ((long)facilityBits << 24) |
            ((long)cardBits << 1);

        return new CredentialFormatResult
        {
            // 48 bits requires 6 bytes, so pad to 12 hex characters.
            RawHex = full48.ToString("X").PadLeft(12, '0'),
            BitCount = 48
        };
    }


    /*
      #############################################################################
                                      CSN (32-bits)
      #############################################################################
    */
    //
    // Converts a 32-bit CSN / UID into a Softwire-ready credential.
    //
    // CSN 32-bit format:
    //      - User enters exactly 4 bytes / 8 hex characters (Shorter values are left-padded with zeroes)
    //      - BitCount = 32
    //
    // Accepted input examples:
    //      - 12345678
    //      - 0x12345678
    //      - 12 34 56 78
    //      - 12-34-56-78
    public static CredentialFormatResult CreateCsn32Bit(string rawHex)
    {
        if (string.IsNullOrWhiteSpace(rawHex))
            throw new ArgumentException("CSN cannot be empty.", nameof(rawHex));

        var cleanHex = rawHex
            .Trim()
            .Replace(" ", "")
            .Replace("-", "");

        if (cleanHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            cleanHex = cleanHex[2..];
        }

        if (cleanHex.Length > 8)
            throw new ArgumentException("CSN 32-bit credentials cannot be longer than 8 hex characters.");

        if (!cleanHex.All(Uri.IsHexDigit))
            throw new ArgumentException("CSN can only contain characters 0-9 and A-F.");

        return new CredentialFormatResult
        {
            RawHex = cleanHex.ToUpperInvariant().PadLeft(8, '0'),
            BitCount = 32
        };
    }


    /*
      #############################################################################
                                     FASC-N (75-bits)
      #############################################################################
    */
    //
    // Converts FASC-N / 75-bit fields into raw hex.
    //
    // Format fields used by Security Center:
    //      - 14-bit Agency Code
    //      - 14-bit System Code
    //      - 20-bit Credential Number
    //      - 25-bit Exp Date
    //
    // Field ranges used by this app:
    //      - Agency Code:       1–16383
    //      - System Code:       1–16383
    //      - Credential Number: 1–1048575
    //      - Exp Date:          1–33554431
    //
    // Bit layout verified against Security Center examples:
    //      - Bit 74       = leading parity bit
    //      - Bits 73–60   = 14-bit Agency Code
    //      - Bits 59–46   = 14-bit System Code
    //      - Bits 45–26   = 20-bit Credential Number
    //      - Bits 25–1    = 25-bit Exp Date
    //      - Bit 0        = trailing parity bit
    //
    // Parity:
    //      - Leading parity gives even parity over the first 37 data bits.
    //      - Trailing parity gives odd parity over the remaining 36 data bits.
    public static CredentialFormatResult CreateFascN_75Bit(int agencyCode, int systemCode, int credentialNumber, int expDate)
    {
        if (agencyCode < 1 || agencyCode > 16383)
            throw new ArgumentOutOfRangeException(nameof(agencyCode), "Agency Code must be between 1 and 16383.");

        if (systemCode < 1 || systemCode > 16383)
            throw new ArgumentOutOfRangeException(nameof(systemCode), "System Code must be between 1 and 16383.");

        if (credentialNumber < 1 || credentialNumber > 1048575)
            throw new ArgumentOutOfRangeException(nameof(credentialNumber), "Credential Number must be between 1 and 1048575.");

        if (expDate < 1 || expDate > 33554431)
            throw new ArgumentOutOfRangeException(nameof(expDate), "Exp Date must be between 1 and 33554431.");

        var agencyBits = agencyCode & 0x3FFF;              // 14 bits
        var systemBits = systemCode & 0x3FFF;              // 14 bits
        var credentialBits = credentialNumber & 0xFFFFF;   // 20 bits
        var expDateBits = expDate & 0x1FFFFFF;             // 25 bits

        // First 37 data bits:
        // - Agency Code: 14 bits
        // - System Code: 14 bits
        // - Top 9 bits of Credential Number
        var credentialHigh9 = (credentialBits >> 11) & 0x1FF;

        // Remaining 36 data bits:
        // - Bottom 11 bits of Credential Number
        // - Exp Date: 25 bits
        var credentialLow11 = credentialBits & 0x7FF;

        var first37Ones =
            CountSetBits(agencyBits) +
            CountSetBits(systemBits) +
            CountSetBits(credentialHigh9);

        var last36Ones =
            CountSetBits(credentialLow11) +
            CountSetBits(expDateBits);

        // Leading parity: even parity over the first 37 data bits.
        var p1 = first37Ones % 2 == 0 ? 0 : 1;

        // Trailing parity: odd parity over the remaining 36 data bits.
        var p2 = last36Ones % 2 == 1 ? 0 : 1;

        var full75 =
            ((System.Numerics.BigInteger)p1 << 74) |
            ((System.Numerics.BigInteger)agencyBits << 60) |
            ((System.Numerics.BigInteger)systemBits << 46) |
            ((System.Numerics.BigInteger)credentialBits << 26) |
            ((System.Numerics.BigInteger)expDateBits << 1) |
            p2;

        return new CredentialFormatResult
        {
            // 75 bits requires 10 bytes, so pad to 20 hex characters.
            RawHex = full75.ToString("X").PadLeft(20, '0'),
            BitCount = 75
        };
    }


    /*
      #############################################################################
                                    FASC-N (200-bits)
      #############################################################################
    */
    //
    // Converts FASC-N / 200-bit fields into raw hex.
    //
    // Unlike the simpler Wiegand formats, FASC-N 200 is represented as 40 x 5-bit encoded characters:
    //
    // D + Agency(4) + B + System(4) + B + Credential(6) + B + CS + B + ICI + B + PI(10) + OC + OI(4) + POA + F + LRC
    //
    // 40 characters x 5 bits = 200 bits.
    //
    // Field ranges used by this app:
    //      - Agency Code:       1–9999
    //      - System Code:       1–9999
    //      - Credential Number: 1–999999
    //      - CS:                1–9
    //      - ICI:               1–9
    //      - PI:                1–9999999999
    //      - OC:                1–9
    //      - OI:                1–9999
    //      - POA:               1–9
    //      - LRC:               1–9 or A–F
    public static CredentialFormatResult CreateFascN_200Bit(int agencyCode, int systemCode, int credentialNumber, int cs, int ici, long pi, int oc, int oi, int poa, char lrc)
    {
        if (agencyCode < 1 || agencyCode > 9999)
            throw new ArgumentOutOfRangeException(nameof(agencyCode), "Agency Code must be between 1 and 9999.");

        if (systemCode < 1 || systemCode > 9999)
            throw new ArgumentOutOfRangeException(nameof(systemCode), "System Code must be between 1 and 9999.");

        if (credentialNumber < 1 || credentialNumber > 999999)
            throw new ArgumentOutOfRangeException(nameof(credentialNumber), "Credential Number must be between 1 and 999999.");

        if (cs < 1 || cs > 9)
            throw new ArgumentOutOfRangeException(nameof(cs), "CS must be between 1 and 9.");

        if (ici < 1 || ici > 9)
            throw new ArgumentOutOfRangeException(nameof(ici), "ICI must be between 1 and 9.");

        if (pi < 1 || pi > 9999999999)
            throw new ArgumentOutOfRangeException(nameof(pi), "PI must be between 1 and 9999999999.");

        if (oc < 1 || oc > 9)
            throw new ArgumentOutOfRangeException(nameof(oc), "OC must be between 1 and 9.");

        if (oi < 1 || oi > 9999)
            throw new ArgumentOutOfRangeException(nameof(oi), "OI must be between 1 and 9999.");

        if (poa < 1 || poa > 9)
            throw new ArgumentOutOfRangeException(nameof(poa), "POA must be between 1 and 9.");

        lrc = char.ToUpperInvariant(lrc);

        if (!"123456789ABCDEF".Contains(lrc))
            throw new ArgumentOutOfRangeException(nameof(lrc), "LRC must be 1–9 or A–F.");

        var bitString = string.Empty;

        // Start sentinel.
        bitString += EncodeFascNHexCharacter('D');

        // Agency Code: 4 decimal digits.
        bitString += EncodeFascNDecimalString(agencyCode.ToString().PadLeft(4, '0'));

        // Field separator.
        bitString += EncodeFascNHexCharacter('B');

        // System Code: 4 decimal digits.
        bitString += EncodeFascNDecimalString(systemCode.ToString().PadLeft(4, '0'));

        // Field separator.
        bitString += EncodeFascNHexCharacter('B');

        // Credential Number: 6 decimal digits.
        bitString += EncodeFascNDecimalString(credentialNumber.ToString().PadLeft(6, '0'));

        // Field separator.
        bitString += EncodeFascNHexCharacter('B');

        // CS: 1 decimal digit.
        bitString += EncodeFascNDecimalString(cs.ToString());

        // Field separator.
        bitString += EncodeFascNHexCharacter('B');

        // ICI: 1 decimal digit.
        bitString += EncodeFascNDecimalString(ici.ToString());

        // Field separator.
        bitString += EncodeFascNHexCharacter('B');

        // PI: 10 decimal digits.
        bitString += EncodeFascNDecimalString(pi.ToString().PadLeft(10, '0'));

        // OC: 1 decimal digit.
        bitString += EncodeFascNDecimalString(oc.ToString());

        // OI: 4 decimal digits.
        bitString += EncodeFascNDecimalString(oi.ToString().PadLeft(4, '0'));

        // POA: 1 decimal digit.
        bitString += EncodeFascNDecimalString(poa.ToString());

        // End sentinel.
        bitString += EncodeFascNHexCharacter('F');

        // LRC/check character.
        bitString += EncodeFascNHexCharacter(lrc);

        return new CredentialFormatResult
        {
            RawHex = BitStringToHex(bitString),
            BitCount = 200
        };
    }

    // Encodes one or more decimal digits for FASC-N 200.
    //
    // Decimal field digits are encoded as:
    //      - 4 data bits in least-significant-bit-first order
    //      - 1 parity bit so the 5-bit character has odd parity
    private static string EncodeFascNDecimalString(string digits)
    {
        var result = string.Empty;

        foreach (var digit in digits)
        {
            if (!char.IsDigit(digit))
                throw new ArgumentException("FASC-N decimal fields can only contain digits.");

            var value = digit - '0';

            var normalBits = Convert.ToString(value, 2).PadLeft(4, '0');
            var reversedBits = new string(normalBits.Reverse().ToArray());

            var ones = reversedBits.Count(c => c == '1');

            // Add parity bit so the 5-bit character has odd parity.
            var parityBit = ones % 2 == 0 ? '1' : '0';

            result += reversedBits + parityBit;
        }

        return result;
    }

    // Encodes a hexadecimal FASC-N control/check character.
    //
    // Sentinels and LRC use:
    //      - 4 data bits in normal most-significant-bit-first order
    //      - 1 parity bit so the 5-bit character has odd parity
    private static string EncodeFascNHexCharacter(char value)
    {
        value = char.ToUpperInvariant(value);

        if (!Uri.IsHexDigit(value))
            throw new ArgumentException("FASC-N hex character must be 0-9 or A-F.");

        var number = Convert.ToInt32(value.ToString(), 16);

        var bits = Convert.ToString(number, 2).PadLeft(4, '0');
        var ones = bits.Count(c => c == '1');

        // Add parity bit so the 5-bit character has odd parity.
        var parityBit = ones % 2 == 0 ? '1' : '0';

        return bits + parityBit;
    }

    // Converts a binary string to uppercase hexadecimal.
    //
    // FASC-N 200 always produces 200 bits, which is exactly 25 bytes, so the bit string should already be byte-aligned.
    private static string BitStringToHex(string bitString)
    {
        if (bitString.Length % 8 != 0)
            throw new ArgumentException("Bit string length must be divisible by 8.");

        var bytes = new byte[bitString.Length / 8];

        for (var i = 0; i < bytes.Length; i++)
        {
            var byteBits = bitString.Substring(i * 8, 8);
            bytes[i] = Convert.ToByte(byteBits, 2);
        }

        return Convert.ToHexString(bytes);
    }

    /*
      #############################################################################
                               Custom Card Format (Raw Hex)
      #############################################################################
    */
    //
    // Converts a custom raw hexadecimal credential into a Softwire-ready credential.
    //
    // DoorSim does not interpret Security Center card format definitions for this option. The user provides:
    //      - Bit count
    //      - Raw hexadecimal credential value
    //
    // Rules:
    //      - Bit count must be 8–512 bits (matching Security Centers values for card formats)
    //      - Raw hex must contain only 0-9 / A-F
    //      - Raw hex may be shorter than required and will be left-padded with zeroes
    //      - Raw hex may not be longer than the number of hex characters required by the bit count
    //
    // Examples:
    //      - 37 bits requires ceiling (37 / 8) = 5 bytes = 10 hex characters
    //      - 512 bits requires 64 bytes = 128 hex characters
    public static CredentialFormatResult CreateCustomRawCredential(string rawHex, int bitCount)
    {
        if (bitCount < 8 || bitCount > 512)
            throw new ArgumentOutOfRangeException(nameof(bitCount), "Bit count must be between 8 and 512.");

        if (string.IsNullOrWhiteSpace(rawHex))
            throw new ArgumentException("Raw hex cannot be empty.", nameof(rawHex));

        var cleanHex = rawHex
            .Trim()
            .Replace(" ", "")
            .Replace("-", "");

        if (cleanHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            cleanHex = cleanHex[2..];
        }

        if (cleanHex.Length == 0)
            throw new ArgumentException("Raw hex cannot be empty.", nameof(rawHex));

        if (!cleanHex.All(Uri.IsHexDigit))
            throw new ArgumentException("Raw hex can only contain characters 0-9 and A-F.", nameof(rawHex));

        cleanHex = cleanHex.ToUpperInvariant();

        var bytesNeeded = (int)Math.Ceiling(bitCount / 8.0);
        var hexCharsNeeded = bytesNeeded * 2;

        if (cleanHex.Length > hexCharsNeeded)
        {
            throw new ArgumentException(
                $"Raw hex is too long for {bitCount} bits. Maximum is {hexCharsNeeded} hex characters.");
        }

        var normalisedHex = cleanHex.PadLeft(hexCharsNeeded, '0');

        return new CredentialFormatResult
        {
            RawHex = normalisedHex,
            BitCount = bitCount
        };
    }


    /*
      #############################################################################
                              Wiegand / Parity Helpers
      #############################################################################
    */
    //
    // Counts how many binary 1s are present in an integer.
    // Used when calculating even/odd parity bits for Wiegand-style formats.
    private static int CountSetBits(int value)
    {
        var count = 0;

        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }

}

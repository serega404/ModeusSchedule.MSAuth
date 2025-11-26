using System.Security.Cryptography;
using System.Text;

namespace ModeusSchedule.MSAuth.Services;

/// <summary>
/// RFC 6238 compatible TOTP generator that accepts Base32 encoded secrets.
/// </summary>
public sealed class TotpGenerator
{
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int DefaultDigits = 6;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private readonly byte[] _secret;
    private readonly int _digits;
    private readonly TimeSpan _step;

    /// <summary>
    /// Creates a TOTP generator instance.
    /// </summary>
    /// <param name="secret">Base32 encoded shared secret.</param>
    /// <param name="digits">Number of digits in the generated one-time password.</param>
    /// <param name="step">Time step (defaults to 30 seconds).</param>
    public TotpGenerator(string secret, int digits = DefaultDigits, TimeSpan? step = null)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("TOTP secret must be provided", nameof(secret));

        _secret = DecodeBase32(secret);
        if (_secret.Length == 0)
            throw new ArgumentException("TOTP secret could not be decoded", nameof(secret));

        _digits = digits;
        _step = step ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Generates a one-time password for the provided point in time (or now by default).
    /// </summary>
    /// <param name="timestamp">Optional timestamp to generate the code for.</param>
    public string Generate(DateTime? timestamp = null)
    {
        var counter = GetCurrentCounter(timestamp);
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(_secret);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                         | ((hash[offset + 1] & 0xFF) << 16)
                         | ((hash[offset + 2] & 0xFF) << 8)
                         | (hash[offset + 3] & 0xFF);

        var otp = binaryCode % (int)Math.Pow(10, _digits);
        return otp.ToString("D" + _digits);
    }

    /// <summary>
    /// Returns the RFC 6238 time counter value for the supplied timestamp.
    /// </summary>
    private long GetCurrentCounter(DateTime? timestamp)
    {
        var effectiveTime = timestamp?.ToUniversalTime() ?? DateTime.UtcNow;
        var elapsed = effectiveTime - Epoch;
        return (long)(elapsed.TotalSeconds / _step.TotalSeconds);
    }

    /// <summary>
    /// Decodes a Base32 string into raw bytes.
    /// </summary>
    private static byte[] DecodeBase32(string input)
    {
        var sanitized = Sanitize(input);
        var bitBuffer = 0;
        var bitsInBuffer = 0;
        var output = new List<byte>(sanitized.Length * 5 / 8);

        foreach (var c in sanitized)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException($"Symbol '{c}' is not valid for Base32");

            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                var byteValue = (byte)((bitBuffer >> bitsInBuffer) & 0xFF);
                output.Add(byteValue);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Normalizes secret formatting by removing padding and separators.
    /// </summary>
    private static string Sanitize(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c == '=' || c == ' ' || c == '-')
                continue;

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }
}

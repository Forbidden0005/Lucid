using System.Security.Cryptography;
using System.Text;

namespace Lucid.Services.Distributed;

/// <summary>
/// Authenticated-encryption envelope for local device sync payloads.
///
/// Envelope layout (v2):
///   [senderDeviceId 36B ASCII, space-padded] [nonce 12B] [tag 16B] [ciphertext NB]
///
/// AES-256-GCM replaces the earlier AES-CBC envelope: anything read off a
/// socket must be authenticated, not merely decrypted — CBC without a MAC is
/// malleable (an on-path peer could flip ciphertext bits undetected). GCM's
/// tag check makes any tampering fail closed in <see cref="TryDecrypt"/>.
///
/// Wire compatibility: v2 envelopes are NOT readable by the earlier CBC
/// format. Device sync is opt-in (off by default) and pairings re-derive the
/// shared key, so mixed-version peers simply fail decryption and skip.
///
/// Pure and static — no I/O, no state — so the crypto core is unit-testable
/// without sockets or the trusted-device registry.
/// </summary>
public static class SyncEnvelopeCrypto
{
    public const int DeviceIdBytes = 36;
    public const int NonceBytes    = 12;
    public const int TagBytes      = 16;

    /// <summary>Minimum structurally-valid envelope length (empty payload).</summary>
    public const int HeaderBytes = DeviceIdBytes + NonceBytes + TagBytes;

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with AES-256-GCM under
    /// <paramref name="key"/> and returns the full envelope.
    /// </summary>
    public static byte[] Encrypt(byte[] key, string senderDeviceId, string plaintext)
    {
        var data  = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        var cipher = new byte[data.Length];
        var tag    = new byte[TagBytes];

        using var gcm = new AesGcm(key, TagBytes);
        gcm.Encrypt(nonce, data, cipher, tag);

        // Pad/truncate device ID to exactly 36 ASCII bytes.
        var idBytes = Encoding.ASCII.GetBytes(senderDeviceId.PadRight(DeviceIdBytes)[..DeviceIdBytes]);

        var envelope = new byte[HeaderBytes + cipher.Length];
        Buffer.BlockCopy(idBytes, 0, envelope, 0,                            DeviceIdBytes);
        Buffer.BlockCopy(nonce,   0, envelope, DeviceIdBytes,                NonceBytes);
        Buffer.BlockCopy(tag,     0, envelope, DeviceIdBytes + NonceBytes,   TagBytes);
        Buffer.BlockCopy(cipher,  0, envelope, HeaderBytes,                  cipher.Length);
        return envelope;
    }

    /// <summary>
    /// Reads the (trimmed) sender device ID from an envelope without any key —
    /// callers use it to look up the pairing before attempting decryption.
    /// Returns null when the envelope is structurally too short.
    /// </summary>
    public static string? ReadSenderId(byte[] envelope) =>
        envelope.Length < HeaderBytes
            ? null
            : Encoding.ASCII.GetString(envelope, 0, DeviceIdBytes).Trim();

    /// <summary>
    /// Authenticates and decrypts an envelope. Returns false — never throws —
    /// when the envelope is malformed, the key is wrong, or the ciphertext or
    /// tag was tampered with in transit.
    /// </summary>
    public static bool TryDecrypt(byte[] key, byte[] envelope, out string? plaintext)
    {
        plaintext = null;
        if (envelope.Length < HeaderBytes) return false;

        try
        {
            var nonce  = new byte[NonceBytes];
            var tag    = new byte[TagBytes];
            var cipher = new byte[envelope.Length - HeaderBytes];
            Buffer.BlockCopy(envelope, DeviceIdBytes,              nonce,  0, NonceBytes);
            Buffer.BlockCopy(envelope, DeviceIdBytes + NonceBytes, tag,    0, TagBytes);
            Buffer.BlockCopy(envelope, HeaderBytes,                cipher, 0, cipher.Length);

            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(key, TagBytes);
            gcm.Decrypt(nonce, cipher, tag, plain);

            plaintext = Encoding.UTF8.GetString(plain);
            return true;
        }
        catch (CryptographicException) { return false; }  // tampered / wrong key
        catch (ArgumentException)      { return false; }  // malformed sizes
    }
}

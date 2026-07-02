using System.Security.Cryptography;
using FluentAssertions;
using Lucid.Services.Distributed;
using Xunit;

namespace Lucid.Tests.Distributed;

/// <summary>
/// Covers the authenticated sync envelope — the crypto core of Lucid's only
/// inbound network surface. Anything read off a socket must fail closed on
/// tampering, truncation, or a wrong key; the CBC-era envelope had no such
/// integrity guarantee.
/// </summary>
public sealed class SyncEnvelopeCryptoTests
{
    private const string SenderId  = "11111111-2222-3333-4444-555555555555";
    private const string Plaintext = """{"deviceId":"abc","cpu":12.5}""";

    private static byte[] NewKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    [Fact]
    public void EncryptThenDecrypt_RoundTripsPlaintext_AndSenderId()
    {
        var key = NewKey();

        var envelope = SyncEnvelopeCrypto.Encrypt(key, SenderId, Plaintext);

        SyncEnvelopeCrypto.ReadSenderId(envelope).Should().Be(SenderId);
        SyncEnvelopeCrypto.TryDecrypt(key, envelope, out var plaintext).Should().BeTrue();
        plaintext.Should().Be(Plaintext);
    }

    [Fact]
    public void TamperedCiphertext_FailsClosed()
    {
        var key = NewKey();
        var envelope = SyncEnvelopeCrypto.Encrypt(key, SenderId, Plaintext);

        // Flip one bit in the ciphertext region — under CBC this produced
        // garbled-but-accepted output; under GCM the tag check must reject it.
        envelope[SyncEnvelopeCrypto.HeaderBytes] ^= 0x01;

        SyncEnvelopeCrypto.TryDecrypt(key, envelope, out var plaintext).Should().BeFalse();
        plaintext.Should().BeNull();
    }

    [Fact]
    public void TamperedTag_FailsClosed()
    {
        var key = NewKey();
        var envelope = SyncEnvelopeCrypto.Encrypt(key, SenderId, Plaintext);

        envelope[SyncEnvelopeCrypto.DeviceIdBytes + SyncEnvelopeCrypto.NonceBytes] ^= 0x01;

        SyncEnvelopeCrypto.TryDecrypt(key, envelope, out _).Should().BeFalse();
    }

    [Fact]
    public void WrongKey_FailsClosed()
    {
        var envelope = SyncEnvelopeCrypto.Encrypt(NewKey(), SenderId, Plaintext);

        SyncEnvelopeCrypto.TryDecrypt(NewKey(), envelope, out _).Should().BeFalse();
    }

    [Fact]
    public void TruncatedEnvelope_FailsClosed_WithoutThrowing()
    {
        var key = NewKey();
        var envelope = SyncEnvelopeCrypto.Encrypt(key, SenderId, Plaintext);
        var truncated = envelope[..(SyncEnvelopeCrypto.HeaderBytes - 1)];

        SyncEnvelopeCrypto.ReadSenderId(truncated).Should().BeNull();
        SyncEnvelopeCrypto.TryDecrypt(key, truncated, out _).Should().BeFalse();
    }

    [Fact]
    public void EachEncryption_UsesAFreshNonce()
    {
        var key = NewKey();

        var a = SyncEnvelopeCrypto.Encrypt(key, SenderId, Plaintext);
        var b = SyncEnvelopeCrypto.Encrypt(key, SenderId, Plaintext);

        a.Should().NotEqual(b, "identical plaintext must never produce an identical envelope");
        a[SyncEnvelopeCrypto.DeviceIdBytes..(SyncEnvelopeCrypto.DeviceIdBytes + SyncEnvelopeCrypto.NonceBytes)]
            .Should().NotEqual(
                b[SyncEnvelopeCrypto.DeviceIdBytes..(SyncEnvelopeCrypto.DeviceIdBytes + SyncEnvelopeCrypto.NonceBytes)]);
    }

    [Fact]
    public void ShortDeviceId_IsPaddedAndTrimmedSymmetrically()
    {
        var key = NewKey();

        var envelope = SyncEnvelopeCrypto.Encrypt(key, "short-id", Plaintext);

        SyncEnvelopeCrypto.ReadSenderId(envelope).Should().Be("short-id");
        SyncEnvelopeCrypto.TryDecrypt(key, envelope, out var plaintext).Should().BeTrue();
        plaintext.Should().Be(Plaintext);
    }
}

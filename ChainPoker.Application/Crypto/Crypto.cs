using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Utilities.IO.Pem;
using System.Text;

namespace ChainPoker.Application.Crypto;

public readonly struct PrivateKey
{
    public const int SeedLength = 32;
    public const int PrivKeyLenght = 64;

    public byte[] Seed { get; }

    private PrivateKey(byte[] seed)
    {
        Seed = seed;
    }

    public static PrivateKey Generate()
    {
        var priv = new Ed25519PrivateKeyParameters(new SecureRandom());

        byte[] seed = priv.GetEncoded(); // extract 32-byte seed

        return new PrivateKey(seed);
    }

    // Build from a 32-byte seed
    public static Ed25519PrivateKeyParameters GenerateFromSeed(byte[] seed)
    {
        if (seed == null)
            throw new ArgumentNullException(nameof(seed));

        if (seed.Length != SeedLength)
            throw new ArgumentException("Seed must be 32 bytes for Ed25519.", nameof(seed));

        // second parameter is offset
        return new Ed25519PrivateKeyParameters(seed, 0);
    }

    public static Ed25519PrivateKeyParameters GenerateFromHex(string hex)
    {
        //string hexSeed = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"; // example 32-byte hex
        byte[] seed = Hex.Decode(hex);

        return GenerateFromSeed(seed);
    }

    /// <summary>
    /// Generates a 64 bytes signature
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public byte[] Sign(byte[] message)
    {
        var priv = new Ed25519PrivateKeyParameters(Seed, 0);
        //byte[] msg = Encoding.UTF8.GetBytes(message);

        var signer = new Ed25519Signer();

        signer.Init(true, priv);

        signer.BlockUpdate(message, 0, message.Length);

        return signer.GenerateSignature();
    }

    public bool Verify(byte[] message, byte[] signature)
    {
        var priv = new Ed25519PrivateKeyParameters(Seed, 0);
        var pub = priv.GeneratePublicKey();
        var verifier = new Ed25519Signer();

        verifier.Init(false, pub);
        verifier.BlockUpdate(message, 0, message.Length);

        return verifier.VerifySignature(signature);
    }

    // Get the 64-byte "libsodium-style" secret (seed || public)
    public FullPublicKey GetSeedPlusPublic()
    {
        var priv = GenerateFromSeed(Seed);

        byte[] pub = priv.GeneratePublicKey().GetEncoded();  // 32 bytes

        // If you need compatibility with libsodium, you may need the 64 - byte form(seed + public key).
        byte[] fullKey = new byte[64]; // Seed.Length + pub.Length

        Buffer.BlockCopy(Seed, 0, fullKey, 0, SeedLength);
        Buffer.BlockCopy(pub, 0, fullKey, SeedLength, pub.Length);
        // fullKey is 64 bytes(like libsodium’s format)

        return new FullPublicKey(fullKey);
    }

    public PublicKey GetPublicKey()
    {
        var priv = GenerateFromSeed(Seed);

        byte[] pub = priv.GeneratePublicKey().GetEncoded();

        return new PublicKey(pub);
    }

    // Get the 64-byte "libsodium-style" secret (seed || public)
    //public static byte[] GetSeedPlusPublic(Ed25519PrivateKeyParameters priv)
    //{
    //    byte[] seed = priv.GetEncoded();                     // 32
    //    byte[] pub = priv.GeneratePublicKey().GetEncoded();  // 32

    //    var combined = new byte[64];
    //    Buffer.BlockCopy(seed, 0, combined, 0, 32);
    //    Buffer.BlockCopy(pub, 0, combined, 32, 32);
    //    return combined;
    //}

    // Export private key as PKCS#8 DER bytes (useful for interoperability)
    public static byte[] ExportPrivateKeyPkcs8(Ed25519PrivateKeyParameters priv)
    {
        // Creates a PrivateKeyInfo structure with the correct OID for Ed25519
        var privInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(priv); // returns Asn1 PrivateKeyInfo
        return privInfo.GetDerEncoded();
    }

    // Export private key as PEM (PKCS#8)
    public static string ExportPrivateKeyPkcs8Pem(Ed25519PrivateKeyParameters priv)
    {
        var der = ExportPrivateKeyPkcs8(priv);
        // write PEM
        using var sw = new StringWriter();
        var pemWriter = new PemWriter(sw);
        pemWriter.WriteObject(new PemObject("PRIVATE KEY", der));
        pemWriter.Writer.Flush();
        return sw.ToString();
    }
}

public readonly struct PublicKey
{
    public const int PubKeyLenght = 32;
    public byte[] Key { get; }

    public PublicKey(byte[] key)
    {
        if (key.Length != PubKeyLenght)
            throw new ArgumentException($"Lenght must be {PubKeyLenght} bytes for Ed25519 public key.", nameof(key));

        Key = key;
    }

    public Address GetAddress() => new(Key[^Address.Lenght..]); //new(Key[(PubKeyLenght - Address.Lenght)..]);
}

public readonly struct FullPublicKey
{
    public const int FullPubKeyLenght = 64;
    public byte[] Key { get; }

    public FullPublicKey(byte[] key)
    {
        if (key.Length != FullPubKeyLenght)
            throw new ArgumentException($"Lenght must be {FullPubKeyLenght} bytes for Ed25519 full public key.", nameof(key));

        Key = key;
    }

    public Address GetAddress() => new(Key[^Address.Lenght..]);
}

public readonly struct Address(byte[] value)
{
    public const int Lenght = 20;
    public byte[] Value { get; } = value;

    public override string ToString() => Encoding.UTF8.GetString(Value);
}

public readonly struct Signature
{
    public const int SignatureLenght = 64;
    public byte[] Value { get; }

    public Signature(byte[] value)
    {
        if (value.Length != SignatureLenght)
            throw new ArgumentException("Lenght must be 64 bytes for Ed25519.", nameof(value));

        Value = value;
    }

    public bool Verify(PublicKey pubKey, byte[] message)
    {
        //️ Ed25519 has variants.Verify with the same variant used for signing:
        // pure: new Ed25519Signer()(no prehash)
        // ph(prehash): new Ed25519phSigner()
        // ctx(with context): new Ed25519ctxSigner(contextBytes)

        byte[] signature = Value; /* 64 bytes from signer */
        byte[] pubKey32 = pubKey.Key; /* 32-byte Ed25519 public key */

        var pub = new Ed25519PublicKeyParameters(pubKey32, 0);
        var verifier = new Ed25519Signer();   // Ed25519 "pure", no prehash, no context

        verifier.Init(false, pub);            // false = verify
        verifier.BlockUpdate(message, 0, message.Length);

        return verifier.VerifySignature(signature);
    }
}


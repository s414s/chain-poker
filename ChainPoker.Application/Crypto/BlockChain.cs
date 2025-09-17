using Org.BouncyCastle.Asn1.Ocsp;
using ProtoBuf;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChainPoker.Application.Crypto;

/// <summary>
/// 1.- we sign a block with a private key
/// 2.- we take the hash of the block
/// 3.- we sign those bytes, gettings a 64 bytes signature
/// 4.- we are going to verify that signature by giving the public key and the hash of that block and that should match
/// </summary>

public class Block
{
    public required Header Header { get; set; }
    public required Transaction Transactions { get; set; }

    public byte[] SignBlock(PrivateKey pk) => pk.Sign(HashBlock());

    /// <summary>
    /// Creates a SHA256 of the header
    /// </summary>
    /// <returns></returns>
    public byte[] HashBlock()
    {
        //var jsonInput = JsonSerializer.SerializeToUtf8Bytes(Header);
        var input = ProtoHelper.ProtoSerialize(Header);

        return SHA256.HashData(input);
    }

    public string HashBlockAsHex()
    {
        using var sha = SHA256.Create();

        var input = JsonSerializer.SerializeToUtf8Bytes(Header);
        byte[] hash = SHA256.HashData(input);
        //string hex = Convert.ToHexString(hash); // "B94D27B9..."
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

[ProtoContract]
public class Header
{
    [ProtoMember(1)]
    public required int Version { get; init; }
    [ProtoMember(2)]
    public required int Height { get; init; }
    [ProtoMember(3)]
    public required byte[] PrevHash { get; init; }
    [ProtoMember(4)]
    public required byte[] RootHash { get; init; } // merkle root of trees
    [ProtoMember(5)]
    public required int Timestamp { get; init; }
}

public static class ProtoHelper
{
    public static byte[] ProtoSerialize<T>(T record) where T : class
    {
        using var stream = new MemoryStream();

        Serializer.Serialize(stream, record);

        return stream.ToArray();
    }
}

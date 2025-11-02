using ProtoBuf;
using System.Security.Cryptography;

namespace ChainPoker.Application.Crypto;

[ProtoContract]
public record Transaction
{
    [ProtoMember(1)]
    public required int Version { get; init; }
    [ProtoMember(2)]
    public required TransactionInput[] Inputs { get; init; }
    [ProtoMember(3)]
    public required TransactionOutput[] Outputs { get; init; }

    public byte[] HashTransaction()
    {
        var input = ProtoHelper.ProtoSerialize(this);

        return SHA256.HashData(input);
    }

    public byte[] HashTransactionCore()
    {
        // IMPORTANT: exclude signature from hash
        var signable = this with { Inputs = [.. Inputs.Select(i => i with { Signature = null })] };

        var bytes = ProtoHelper.ProtoSerialize(signable); // Ensure serializer is deterministic!
        return SHA256.HashData(bytes);
    }

    public Signature Sign(PrivateKey pk) => new(pk.Sign(HashTransactionCore()));

    public bool Verify()
    {
        var sighash = HashTransactionCore();

        foreach (var input in Inputs)
        {
            if (input.Signature is null)
                return false;

            var sig = new Signature(input.Signature);
            var pub = new PublicKey(input.PublicKey);

            if (!sig.Verify(pub, sighash))
                return false;
        }

        return true;
    }
}

[ProtoContract]
public record TransactionInput
{
    /// <summary>
    /// The previous hash of the transation containing the output we want to spend
    /// </summary>
    [ProtoMember(1)]
    public required byte[] PrevTxHash { get; init; }
    /// <summary>
    /// The index of the output of the previous transaction we wat to spend
    /// </summary>
    [ProtoMember(2)]
    public required uint PrevOutTxIndex { get; init; }
    [ProtoMember(3)]
    public required byte[] PublicKey { get; init; }
    [ProtoMember(4)]
    public byte[]? Signature { get; set; }
}

[ProtoContract]
public record TransactionOutput
{
    [ProtoMember(1)]
    public required long Amount { get; init; }
    [ProtoMember(2)]
    public required byte[] Address { get; init; }
}


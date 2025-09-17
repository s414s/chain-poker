using System.Security.Cryptography;

namespace ChainPoker.Application.Crypto;

public class Transaction
{
    public required int Version { get; init; }
    public required TransactionInput[] TransactionInputs { get; init; }
    public required TransactionOutput[] TransactionOutputs { get; init; }

    public byte[] HashTransaction()
    {
        var input = ProtoHelper.ProtoSerialize(this);

        return SHA256.HashData(input);
    }
}

public record TransactionInput
{
    /// <summary>
    /// The previous hash of the transation containing the output we want to spend
    /// </summary>
    public required byte[] PrevTxHash { get; init; }
    /// <summary>
    /// The index of the output of the previous transaction we wat to spend
    /// </summary>
    public required uint PrevOutTx { get; init; }
    public required byte[] PublicKey { get; init; }
    public required byte[] Signature { get; init; }
}

public record TransactionOutput
{
    public required long Amount { get; init; }
    public required byte[] Address { get; init; }
}


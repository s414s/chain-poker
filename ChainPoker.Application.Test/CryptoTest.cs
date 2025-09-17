using ChainPoker.Application.Crypto;
using System.Security.Cryptography;
using System.Text;

namespace ChainPoker.Application.Test;

public class CryptoTest
{
    [Fact]
    public void SeedLength_As_Expected()
    {
        // Arrange
        var pk = PrivateKey.Generate();

        // Assert
        Assert.Equal(PrivateKey.SeedLength, pk.Seed.Length);
    }

    [Fact]
    public void Verify_Ok_When_Private_Key()
    {
        // Arrange
        var pk = PrivateKey.Generate();
        var pk2 = PrivateKey.Generate();

        var message = Encoding.UTF8.GetBytes("My message");

        // Act
        var signature = pk.Sign(message);

        // Assert
        Assert.True(pk.Verify(message, signature));
        Assert.False(pk2.Verify(message, signature));
    }

    [Fact]
    public void Test_New_Transaction()
    {
        // my balance is 100
        // want to send 5 coins to "AAA"
        // 2 outputs
        // 5 to the person we want to send
        // 95 back to our address or some other adddress

        // Arrange
        var totalInitialBalance = 100;

        var fromPrivteKey = PrivateKey.Generate();
        var fromAdddress = fromPrivteKey.GetSeedPlusPublic().GetAddress().Value;

        var toPrivteKey = PrivateKey.Generate();
        var toAdddress = toPrivteKey.GetSeedPlusPublic().GetAddress().Value;

        var randomNumber = BitConverter.GetBytes(Random.Shared.Next(29));

        // The information about ourselves or previous receivers
        var input = new TransactionInput
        {
            PrevTxHash = SHA256.HashData(randomNumber),
            PrevOutTxIndex = 0,
            PublicKey = fromPrivteKey.GetPublicKey().Key,
        };

        // The information about the destination of the coins
        // we send a certain amount
        var output1 = new TransactionOutput
        {
            Amount = 5,
            Address = toAdddress,
        };

        // we need to spend the rest of the balance of our account
        var output2 = new TransactionOutput
        {
            Amount = totalInitialBalance - output1.Amount,
            Address = fromAdddress,
        };

        var transaction = new Transaction
        {
            Version = 1,
            Inputs = [input],
            Outputs = [output1, output2],
        };

        // Act
        input.Signature = transaction.Sign(fromPrivteKey).Value;

        // Assert
        Assert.True(transaction.Verify());
    }
}

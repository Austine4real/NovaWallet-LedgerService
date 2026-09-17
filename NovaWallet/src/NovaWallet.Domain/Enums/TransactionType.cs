namespace NovaWallet.Domain.Enums;

public enum TransactionType
{
    CreditIn = 0,      // Inbound NIP credit (deposit)
    TransferOut = 1,   // Debit leg of a P2P transfer
    TransferIn = 2     // Credit leg of a P2P transfer
}

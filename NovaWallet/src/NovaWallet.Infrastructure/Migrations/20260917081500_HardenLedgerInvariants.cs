using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaWallet.Infrastructure.Migrations;

public partial class HardenLedgerInvariants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "CK_Wallets_BalanceKobo_NonNegative",
            table: "Wallets",
            sql: "[BalanceKobo] >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Wallets_Currency_NGN",
            table: "Wallets",
            sql: "[Currency] = 'NGN'");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Transactions_AmountKobo_Positive",
            table: "Transactions",
            sql: "[AmountKobo] > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AuditLog_ResultingBalance_NonNegative",
            table: "AuditLogEntries",
            sql: "[ResultingBalanceKobo] >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AuditLog_Delta_NonZero",
            table: "AuditLogEntries",
            sql: "[DeltaKobo] <> 0");

        migrationBuilder.AddForeignKey(
            name: "FK_Transactions_Wallets_WalletId",
            table: "Transactions",
            column: "WalletId",
            principalTable: "Wallets",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_AuditLogEntries_Wallets_WalletId",
            table: "AuditLogEntries",
            column: "WalletId",
            principalTable: "Wallets",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
            CREATE TRIGGER [TR_AuditLogEntries_Immutable]
            ON [AuditLogEntries]
            AFTER UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51001, 'AuditLogEntries is append-only; UPDATE and DELETE are not permitted.', 1;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_AuditLogEntries_Immutable];");

        migrationBuilder.DropForeignKey(
            name: "FK_Transactions_Wallets_WalletId",
            table: "Transactions");

        migrationBuilder.DropForeignKey(
            name: "FK_AuditLogEntries_Wallets_WalletId",
            table: "AuditLogEntries");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Wallets_BalanceKobo_NonNegative",
            table: "Wallets");
        migrationBuilder.DropCheckConstraint(
            name: "CK_Wallets_Currency_NGN",
            table: "Wallets");
        migrationBuilder.DropCheckConstraint(
            name: "CK_Transactions_AmountKobo_Positive",
            table: "Transactions");
        migrationBuilder.DropCheckConstraint(
            name: "CK_AuditLog_ResultingBalance_NonNegative",
            table: "AuditLogEntries");
        migrationBuilder.DropCheckConstraint(
            name: "CK_AuditLog_Delta_NonZero",
            table: "AuditLogEntries");
    }
}

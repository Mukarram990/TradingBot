using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CleanBaseEntityFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsExecuted",
                table: "TradeSignals");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "TradeSignals");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "TradeSignals");

            migrationBuilder.DropColumn(
                name: "SuggestedStopLoss",
                table: "TradeSignals");

            migrationBuilder.DropColumn(
                name: "OrderType",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "SuggestedTakeProfit",
                table: "TradeSignals",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "BinanceOrderId",
                table: "Orders",
                newName: "ExternalOrderId");

            migrationBuilder.AlterColumn<decimal>(
                name: "Confidence",
                table: "TradeSignals",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Orders",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<decimal>(
                name: "ExecutedPrice",
                table: "Orders",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutedPrice",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "TradeSignals",
                newName: "SuggestedTakeProfit");

            migrationBuilder.RenameColumn(
                name: "ExternalOrderId",
                table: "Orders",
                newName: "BinanceOrderId");

            migrationBuilder.AlterColumn<int>(
                name: "Confidence",
                table: "TradeSignals",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,8)",
                oldPrecision: 18,
                oldScale: 8,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExecuted",
                table: "TradeSignals",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "TradeSignals",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "TradeSignals",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedStopLoss",
                table: "TradeSignals",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "OrderType",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Orders",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
        }
    }
}

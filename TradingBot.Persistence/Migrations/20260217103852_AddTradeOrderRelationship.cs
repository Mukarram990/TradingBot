using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    public partial class AddTradeOrderRelationship : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only add a nullable GUID FK to Orders to reference existing Trades.Id (no PK type changes)
            migrationBuilder.AddColumn<Guid>(
                name: "TradeId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TradeId",
                table: "Orders",
                column: "TradeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Trades_TradeId",
                table: "Orders",
                column: "TradeId",
                principalTable: "Trades",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Trades_TradeId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TradeId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TradeId",
                table: "Orders");
        }
    }
}

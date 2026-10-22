using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryClaimedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_in_flight",
                table: "deliveries",
                column: "claimed_at",
                filter: "status IN ('InFlight')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deliveries_in_flight",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "claimed_at",
                table: "deliveries");
        }
    }
}

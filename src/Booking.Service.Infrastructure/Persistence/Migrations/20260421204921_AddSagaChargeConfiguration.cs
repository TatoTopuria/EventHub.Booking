using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Booking.Service.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaChargeConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ChargeAmount",
                table: "booking_payment_saga_states",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ChargeCurrency",
                table: "booking_payment_saga_states",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ChargeIdempotencyKey",
                table: "booking_payment_saga_states",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ChargeMetadataJson",
                table: "booking_payment_saga_states",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ChargeProvider",
                table: "booking_payment_saga_states",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChargeAmount",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "ChargeCurrency",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "ChargeIdempotencyKey",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "ChargeMetadataJson",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "ChargeProvider",
                table: "booking_payment_saga_states");
        }
    }
}

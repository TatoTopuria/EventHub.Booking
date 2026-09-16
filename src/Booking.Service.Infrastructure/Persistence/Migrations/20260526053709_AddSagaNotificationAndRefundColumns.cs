using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Booking.Service.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaNotificationAndRefundColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NotificationCompletedAtUtc",
                table: "booking_payment_saga_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationFailureReason",
                table: "booking_payment_saga_states",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationStatus",
                table: "booking_payment_saga_states",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundCompletedAtUtc",
                table: "booking_payment_saga_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "booking_payment_saga_states",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundStatus",
                table: "booking_payment_saga_states",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationCompletedAtUtc",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "NotificationFailureReason",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "NotificationStatus",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "RefundCompletedAtUtc",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "RefundStatus",
                table: "booking_payment_saga_states");
        }
    }
}

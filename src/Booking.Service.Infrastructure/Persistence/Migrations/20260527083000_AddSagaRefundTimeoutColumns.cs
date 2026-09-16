using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Booking.Service.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaRefundTimeoutColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RefundRequestedAtUtc",
                table: "booking_payment_saga_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundTimeoutElapsedSeconds",
                table: "booking_payment_saga_states",
                type: "integer",
                nullable: true);

            // Composite index supports the BookingSagaTimeoutWorker query:
            //   WHERE CurrentState = 'Refunding' AND RefundRequestedAtUtc < @cutoff
            // ORDER BY RefundRequestedAtUtc. PostgreSQL picks this over a sequential scan once the
            // saga state table grows past a few hundred rows.
            migrationBuilder.CreateIndex(
                name: "IX_booking_payment_saga_states_CurrentState_RefundRequestedAtUtc",
                table: "booking_payment_saga_states",
                columns: new[] { "CurrentState", "RefundRequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_booking_payment_saga_states_CurrentState_RefundRequestedAtUtc",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "RefundRequestedAtUtc",
                table: "booking_payment_saga_states");

            migrationBuilder.DropColumn(
                name: "RefundTimeoutElapsedSeconds",
                table: "booking_payment_saga_states");
        }
    }
}

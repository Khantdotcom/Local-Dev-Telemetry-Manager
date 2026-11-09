using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agoda.DevExTelemetry.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionIdAndSpooledAtToBuildMetric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SessionId",
                table: "BuildMetrics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SpooledAt",
                table: "BuildMetrics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildMetrics_SessionId",
                table: "BuildMetrics",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BuildMetrics_SessionId",
                table: "BuildMetrics");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "BuildMetrics");

            migrationBuilder.DropColumn(
                name: "SpooledAt",
                table: "BuildMetrics");
        }
    }
}

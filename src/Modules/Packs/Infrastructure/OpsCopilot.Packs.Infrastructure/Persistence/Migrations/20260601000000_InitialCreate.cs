using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.Packs.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "packs");

            migrationBuilder.CreateTable(
                name: "ProposalDeadLetterEntries",
                schema: "packs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TriageRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActionId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReplayAttempts = table.Column<int>(type: "int", nullable: false),
                    LastReplayedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplayError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposalDeadLetterEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProposalDeadLetterEntries_AttemptId",
                schema: "packs",
                table: "ProposalDeadLetterEntries",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProposalDeadLetterEntries_TenantId",
                schema: "packs",
                table: "ProposalDeadLetterEntries",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProposalDeadLetterEntries",
                schema: "packs");
        }
    }
}

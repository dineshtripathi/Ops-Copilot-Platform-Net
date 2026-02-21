using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyEvents",
                schema: "agentRuns",
                columns: table => new
                {
                    PolicyEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Allowed = table.Column<bool>(type: "bit", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyEvents", x => x.PolicyEventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyEvents_OccurredAtUtc",
                schema: "agentRuns",
                table: "PolicyEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyEvents_RunId",
                schema: "agentRuns",
                table: "PolicyEvents",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyEvents",
                schema: "agentRuns");
        }
    }
}

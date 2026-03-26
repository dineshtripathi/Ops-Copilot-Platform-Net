using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence.Migrations;

public partial class AddRunFeedbackTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RunFeedback",
            schema: "agentRuns",
            columns: table => new
            {
                FeedbackId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Rating = table.Column<int>(type: "int", nullable: false),
                Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RunFeedback", x => x.FeedbackId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RunFeedback_RunId",
            schema: "agentRuns",
            table: "RunFeedback",
            column: "RunId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RunFeedback_TenantId_SubmittedAtUtc",
            schema: "agentRuns",
            table: "RunFeedback",
            columns: new[] { "TenantId", "SubmittedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RunFeedback",
            schema: "agentRuns");
    }
}

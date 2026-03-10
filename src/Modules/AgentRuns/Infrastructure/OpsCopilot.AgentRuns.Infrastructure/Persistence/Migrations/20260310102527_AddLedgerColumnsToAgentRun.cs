using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerColumnsToAgentRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCost",
                schema: "agentRuns",
                table: "AgentRuns",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                schema: "agentRuns",
                table: "AgentRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                schema: "agentRuns",
                table: "AgentRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                schema: "agentRuns",
                table: "AgentRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptVersionId",
                schema: "agentRuns",
                table: "AgentRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunType",
                schema: "agentRuns",
                table: "AgentRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTokens",
                schema: "agentRuns",
                table: "AgentRuns",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedCost",
                schema: "agentRuns",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                schema: "agentRuns",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "ModelId",
                schema: "agentRuns",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                schema: "agentRuns",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "PromptVersionId",
                schema: "agentRuns",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "RunType",
                schema: "agentRuns",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "TotalTokens",
                schema: "agentRuns",
                table: "AgentRuns");
        }
    }
}

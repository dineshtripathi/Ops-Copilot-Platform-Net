using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence.Migrations;

public partial class AddAzureAlertContextColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AlertProvider",
            schema: "agentRuns",
            table: "AgentRuns",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AlertSourceType",
            schema: "agentRuns",
            table: "AgentRuns",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AzureApplication",
            schema: "agentRuns",
            table: "AgentRuns",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AzureResourceGroup",
            schema: "agentRuns",
            table: "AgentRuns",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AzureResourceId",
            schema: "agentRuns",
            table: "AgentRuns",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AzureSubscriptionId",
            schema: "agentRuns",
            table: "AgentRuns",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AzureWorkspaceId",
            schema: "agentRuns",
            table: "AgentRuns",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsExceptionSignal",
            schema: "agentRuns",
            table: "AgentRuns",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_AgentRuns_TenantId_AzureResourceGroup_CreatedAtUtc",
            schema: "agentRuns",
            table: "AgentRuns",
            columns: new[] { "TenantId", "AzureResourceGroup", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AgentRuns_TenantId_IsExceptionSignal_CreatedAtUtc",
            schema: "agentRuns",
            table: "AgentRuns",
            columns: new[] { "TenantId", "IsExceptionSignal", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AgentRuns_TenantId_AzureResourceGroup_CreatedAtUtc",
            schema: "agentRuns",
            table: "AgentRuns");

        migrationBuilder.DropIndex(
            name: "IX_AgentRuns_TenantId_IsExceptionSignal_CreatedAtUtc",
            schema: "agentRuns",
            table: "AgentRuns");

        migrationBuilder.DropColumn(name: "AlertProvider", schema: "agentRuns", table: "AgentRuns");
        migrationBuilder.DropColumn(name: "AlertSourceType", schema: "agentRuns", table: "AgentRuns");
        migrationBuilder.DropColumn(name: "AzureApplication", schema: "agentRuns", table: "AgentRuns");
        migrationBuilder.DropColumn(name: "AzureResourceGroup", schema: "agentRuns", table: "AgentRuns");
        migrationBuilder.DropColumn(name: "AzureResourceId", schema: "agentRuns", table: "AgentRuns");
        migrationBuilder.DropColumn(name: "AzureSubscriptionId", schema: "agentRuns", table: "AgentRuns");
        migrationBuilder.DropColumn(name: "AzureWorkspaceId", schema: "agentRuns", table: "AgentRuns");
        migrationBuilder.DropColumn(name: "IsExceptionSignal", schema: "agentRuns", table: "AgentRuns");
    }
}

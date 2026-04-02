using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.Prompting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanaryExperiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CanaryExperiments",
                schema: "prompting",
                columns: table => new
                {
                    PromptKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CandidateVersion = table.Column<int>(type: "int", nullable: false),
                    CandidateContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TrafficPercent = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanaryExperiments", x => x.PromptKey);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CanaryExperiments",
                schema: "prompting");
        }
    }
}

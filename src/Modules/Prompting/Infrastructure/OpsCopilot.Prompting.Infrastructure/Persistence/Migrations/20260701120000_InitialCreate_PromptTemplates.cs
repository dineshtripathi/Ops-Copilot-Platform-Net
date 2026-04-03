using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.Prompting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate_PromptTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prompting");

            migrationBuilder.CreateTable(
                name: "PromptTemplates",
                schema: "prompting",
                columns: table => new
                {
                    PromptTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTemplates", x => x.PromptTemplateId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplates_PromptKey_IsActive",
                schema: "prompting",
                table: "PromptTemplates",
                columns: new[] { "PromptKey", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptTemplates",
                schema: "prompting");
        }
    }
}

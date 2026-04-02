using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.Evaluation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineEvalEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "eval");

            migrationBuilder.CreateTable(
                name: "OnlineEvalEntries",
                schema: "eval",
                columns: table => new
                {
                    Id                  = table.Column<int>(type: "int", nullable: false)
                                            .Annotation("SqlServer:Identity", "1, 1"),
                    RunId               = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetrievalConfidence = table.Column<double>(type: "float", nullable: false),
                    FeedbackScore       = table.Column<float>(type: "real", nullable: true),
                    ModelVersion        = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PromptVersionId     = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAt          = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineEvalEntries", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnlineEvalEntries",
                schema: "eval");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCam.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecognitionFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecognitionFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PredictedLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PredictedProbability = table.Column<double>(type: "float", nullable: false),
                    CandidatesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrectWordId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CorrectText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ModelSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VocabVersion = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecognitionFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecognitionFeedback_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecognitionFeedback_CreatedAt",
                table: "RecognitionFeedback",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RecognitionFeedback_UserId",
                table: "RecognitionFeedback",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecognitionFeedback");
        }
    }
}

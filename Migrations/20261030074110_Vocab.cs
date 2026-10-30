using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCam.Api.Migrations
{
    /// <inheritdoc />
    public partial class Vocab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaFiles",
                columns: table => new
                {
                    Path = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Bundled = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Path);
                });

            migrationBuilder.CreateTable(
                name: "VocabCategories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Vi = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    En = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Emoji = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabReleases",
                columns: table => new
                {
                    Version = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VocabJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WordCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabReleases", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "VocabWords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    En = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Vi = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phonetic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExampleEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ExampleVi = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Audio = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExampleAudio = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Image = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Emoji = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Hunt = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabWords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VocabWords_VocabCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "VocabCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VocabWords_CategoryId",
                table: "VocabWords",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_VocabWords_Label",
                table: "VocabWords",
                column: "Label",
                unique: true,
                filter: "[Label] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaFiles");

            migrationBuilder.DropTable(
                name: "VocabReleases");

            migrationBuilder.DropTable(
                name: "VocabWords");

            migrationBuilder.DropTable(
                name: "VocabCategories");
        }
    }
}

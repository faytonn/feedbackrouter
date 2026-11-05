using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeedbackRouter.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedbackItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RawText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmitterEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Embedding = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    MergedIntoId = table.Column<int>(type: "int", nullable: true),
                    TriageAttempts = table.Column<int>(type: "int", nullable: false),
                    LastTriageAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TriageError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FilingClaimedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackItems_FeedbackItems_MergedIntoId",
                        column: x => x.MergedIntoId,
                        principalTable: "FeedbackItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TriageDecisions",
                columns: table => new
                {
                    FeedbackItemId = table.Column<int>(type: "int", nullable: false),
                    ReviewerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FinalArea = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FinalType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    FinalSeverity = table.Column<int>(type: "int", nullable: true),
                    FinalTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FinalSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriageDecisions", x => x.FeedbackItemId);
                    table.ForeignKey(
                        name: "FK_TriageDecisions_FeedbackItems_FeedbackItemId",
                        column: x => x.FeedbackItemId,
                        principalTable: "FeedbackItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TriageProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedbackItemId = table.Column<int>(type: "int", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    ProposedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriageProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TriageProposals_FeedbackItems_FeedbackItemId",
                        column: x => x.FeedbackItemId,
                        principalTable: "FeedbackItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemLinks",
                columns: table => new
                {
                    FeedbackItemId = table.Column<int>(type: "int", nullable: false),
                    AdoWorkItemId = table.Column<int>(type: "int", nullable: true),
                    AdoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FiledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DryRun = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemLinks", x => x.FeedbackItemId);
                    table.ForeignKey(
                        name: "FK_WorkItemLinks_FeedbackItems_FeedbackItemId",
                        column: x => x.FeedbackItemId,
                        principalTable: "FeedbackItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DuplicateCandidates",
                columns: table => new
                {
                    FeedbackItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TriageProposalId = table.Column<int>(type: "int", nullable: false),
                    Similarity = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateCandidates", x => new { x.TriageProposalId, x.FeedbackItemId });
                    table.ForeignKey(
                        name: "FK_DuplicateCandidates_TriageProposals_TriageProposalId",
                        column: x => x.TriageProposalId,
                        principalTable: "TriageProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackItems_MergedIntoId",
                table: "FeedbackItems",
                column: "MergedIntoId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackItems_Status",
                table: "FeedbackItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TriageProposals_FeedbackItemId_ProposedAt",
                table: "TriageProposals",
                columns: new[] { "FeedbackItemId", "ProposedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuplicateCandidates");

            migrationBuilder.DropTable(
                name: "TriageDecisions");

            migrationBuilder.DropTable(
                name: "WorkItemLinks");

            migrationBuilder.DropTable(
                name: "TriageProposals");

            migrationBuilder.DropTable(
                name: "FeedbackItems");
        }
    }
}

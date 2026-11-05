using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeedbackRouter.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewActivityAt",
                table: "FeedbackItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerName",
                table: "FeedbackItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewActivityAt",
                table: "FeedbackItems");

            migrationBuilder.DropColumn(
                name: "ReviewerName",
                table: "FeedbackItems");
        }
    }
}

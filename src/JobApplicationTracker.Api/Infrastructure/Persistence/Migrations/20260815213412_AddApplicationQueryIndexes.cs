using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobApplicationTracker.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_AppliedOn",
                table: "JobApplications",
                column: "AppliedOn");

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_Source_UpdatedAt",
                table: "JobApplications",
                columns: new[] { "Source", "UpdatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_UpdatedAt",
                table: "JobApplications",
                column: "UpdatedAt",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobApplications_AppliedOn",
                table: "JobApplications");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_Source_UpdatedAt",
                table: "JobApplications");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_UpdatedAt",
                table: "JobApplications");
        }
    }
}

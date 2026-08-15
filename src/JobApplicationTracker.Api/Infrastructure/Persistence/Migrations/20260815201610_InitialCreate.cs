using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobApplicationTracker.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PositionTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    JobPostingUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AppliedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NextActionDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NextActionDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobApplications", x => x.Id);
                    table.CheckConstraint("CK_JobApplications_NextActionPair", "([NextActionDescription] IS NULL AND [NextActionDueAt] IS NULL) OR ([NextActionDescription] IS NOT NULL AND [NextActionDueAt] IS NOT NULL)");
                    table.CheckConstraint("CK_JobApplications_Status", "[Status] IN ('Saved', 'Applied', 'Screening', 'Interview', 'Offer', 'Rejected', 'Withdrawn')");
                });

            migrationBuilder.CreateTable(
                name: "StatusChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    NewStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusChanges", x => x.Id);
                    table.CheckConstraint("CK_StatusChanges_DifferentStatuses", "[PreviousStatus] IS NULL OR [PreviousStatus] <> [NewStatus]");
                    table.CheckConstraint("CK_StatusChanges_NewStatus", "[NewStatus] IN ('Saved', 'Applied', 'Screening', 'Interview', 'Offer', 'Rejected', 'Withdrawn')");
                    table.CheckConstraint("CK_StatusChanges_PreviousStatus", "[PreviousStatus] IS NULL OR [PreviousStatus] IN ('Saved', 'Applied', 'Screening', 'Interview', 'Offer', 'Rejected', 'Withdrawn')");
                    table.ForeignKey(
                        name: "FK_StatusChanges_JobApplications_JobApplicationId",
                        column: x => x.JobApplicationId,
                        principalTable: "JobApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_NextActionDueAt",
                table: "JobApplications",
                column: "NextActionDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_Status_UpdatedAt",
                table: "JobApplications",
                columns: new[] { "Status", "UpdatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_StatusChanges_JobApplicationId_ChangedAt",
                table: "StatusChanges",
                columns: new[] { "JobApplicationId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StatusChanges");

            migrationBuilder.DropTable(
                name: "JobApplications");
        }
    }
}

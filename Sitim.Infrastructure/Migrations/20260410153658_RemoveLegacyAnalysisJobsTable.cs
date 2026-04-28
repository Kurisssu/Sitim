using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sitim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyAnalysisJobsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analysis_jobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analysis_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    finished_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hangfire_job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    orthanc_study_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_json_path = table.Column<string>(type: "text", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    study_archive_path = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_jobs_institution_id",
                table: "analysis_jobs",
                column: "institution_id");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_jobs_orthanc_study_id",
                table: "analysis_jobs",
                column: "orthanc_study_id");
        }
    }
}

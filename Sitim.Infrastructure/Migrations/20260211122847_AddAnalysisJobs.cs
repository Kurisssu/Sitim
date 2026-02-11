using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sitim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_analysis_jobs_status",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "input_archive_path",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "output_json_path",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "study_instance_uid",
                table: "analysis_jobs");

            migrationBuilder.AlterColumn<string>(
                name: "model_key",
                table: "analysis_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "analysis_jobs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hangfire_job_id",
                table: "analysis_jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_json_path",
                table: "analysis_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "study_archive_path",
                table: "analysis_jobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hangfire_job_id",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "result_json_path",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "study_archive_path",
                table: "analysis_jobs");

            migrationBuilder.AlterColumn<string>(
                name: "model_key",
                table: "analysis_jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "analysis_jobs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "input_archive_path",
                table: "analysis_jobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_json_path",
                table: "analysis_jobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "study_instance_uid",
                table: "analysis_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_analysis_jobs_status",
                table: "analysis_jobs",
                column: "status");
        }
    }
}

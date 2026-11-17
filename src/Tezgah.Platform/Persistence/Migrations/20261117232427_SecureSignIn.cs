using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tezgah.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecureSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Accounts that exist already stay active, start at session version 1 and keep their password.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                schema: "platform",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "platform",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_sign_in_at",
                schema: "platform",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                schema: "platform",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "pending_totp_secret",
                schema: "platform",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "session_version",
                schema: "platform",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "totp_last_step",
                schema: "platform",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "totp_secret",
                schema: "platform",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recovery_codes",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "platform",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    session_version = table.Column<int>(type: "integer", nullable: false),
                    device = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    family_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replaced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "platform",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sign_in_throttles",
                schema: "platform",
                columns: table => new
                {
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    failures = table.Column<int>(type: "integer", nullable: false),
                    window_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_in_throttles", x => x.email);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recovery_codes_user_id_code_hash",
                schema: "platform",
                table: "recovery_codes",
                columns: new[] { "user_id", "code_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                schema: "platform",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "platform",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                schema: "platform",
                table: "refresh_tokens",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recovery_codes",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "sign_in_throttles",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "platform",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "platform",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_sign_in_at",
                schema: "platform",
                table: "users");

            migrationBuilder.DropColumn(
                name: "must_change_password",
                schema: "platform",
                table: "users");

            migrationBuilder.DropColumn(
                name: "pending_totp_secret",
                schema: "platform",
                table: "users");

            migrationBuilder.DropColumn(
                name: "session_version",
                schema: "platform",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_last_step",
                schema: "platform",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_secret",
                schema: "platform",
                table: "users");
        }
    }
}

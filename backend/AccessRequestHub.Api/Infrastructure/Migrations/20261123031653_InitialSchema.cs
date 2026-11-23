using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AccessRequestHub.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    ManagerId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Users_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequesterId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    AccessLevel = table.Column<string>(type: "TEXT", nullable: false),
                    Justification = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                    table.CheckConstraint("CK_Requests_AccessLevel", "AccessLevel IN ('Read', 'Admin')");
                    table.CheckConstraint("CK_Requests_Environment", "Environment IN ('NonProduction', 'Production')");
                    table.CheckConstraint("CK_Requests_Required", "Version > 0 AND length(trim(Justification)) BETWEEN 1 AND 2000 AND length(trim(PolicyVersion)) > 0");
                    table.CheckConstraint("CK_Requests_Status", "Status IN ('PendingManager', 'PendingSystemOwner', 'Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_Requests_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Requests_Users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousStatus = table.Column<string>(type: "TEXT", nullable: true),
                    ResultingStatus = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.CheckConstraint("CK_AuditEvents_Action", "Action IN ('Created', 'Approved', 'Rejected') AND RequestVersion > 0");
                    table.CheckConstraint("CK_AuditEvents_Reason", "Action <> 'Rejected' OR (Reason IS NOT NULL AND length(trim(Reason)) BETWEEN 1 AND 1000)");
                    table.ForeignKey(
                        name: "FK_AuditEvents_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Email", "ManagerId", "Role" },
                values: new object[,]
                {
                    { 2, "bob@example.local", null, "Manager" },
                    { 3, "carol@example.local", null, "SystemOwner" },
                    { 4, "dana@example.local", null, "SystemOwner" },
                    { 5, "erin@example.local", null, "Auditor" }
                });

            migrationBuilder.InsertData(
                table: "Applications",
                columns: new[] { "Id", "Name", "OwnerId" },
                values: new object[,]
                {
                    { 1, "CRM", 3 },
                    { 2, "Finance Portal", 4 }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Email", "ManagerId", "Role" },
                values: new object[] { 1, "alice@example.local", 2, "Requester" });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Name",
                table: "Applications",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_OwnerId",
                table: "Applications",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorId",
                table: "AuditEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_RequestId_RequestVersion",
                table: "AuditEvents",
                columns: new[] { "RequestId", "RequestVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_ApplicationId_Status",
                table: "Requests",
                columns: new[] { "ApplicationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_RequesterId_ClientRequestId",
                table: "Requests",
                columns: new[] { "RequesterId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ManagerId",
                table: "Users",
                column: "ManagerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "Requests");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}

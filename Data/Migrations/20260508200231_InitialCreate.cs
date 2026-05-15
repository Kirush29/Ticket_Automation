using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficTicketAutomation.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RA = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Plate = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Start = table.Column<DateTime>(type: "datetime2", nullable: false),
                    End = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Customer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExcelFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BillFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<int>(type: "int", nullable: false),
                    Plate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TicketDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RA = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Customer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CustomerEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RentalDays = table.Column<int>(type: "int", nullable: true),
                    HighlightedBillPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContractPdfPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailSubject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FoundInBill = table.Column<bool>(type: "bit", nullable: false),
                    ContractFound = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketResults_ProcessingJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ProcessingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_Plate",
                table: "Contracts",
                column: "Plate");

            migrationBuilder.CreateIndex(
                name: "IX_TicketResults_JobId",
                table: "TicketResults",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "TicketResults");

            migrationBuilder.DropTable(
                name: "ProcessingJobs");
        }
    }
}

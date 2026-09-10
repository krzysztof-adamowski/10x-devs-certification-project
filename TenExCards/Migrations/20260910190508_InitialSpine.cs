using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenExCards.Migrations
{
    /// <inheritdoc />
    public partial class InitialSpine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpineProbes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WrittenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpineProbes", x => x.Id);
                });
        }

        /// <inheritdoc />
        /// <remarks>
        /// EF generated this; it was not authored, and nothing relies on it. Migrations in this
        /// project are FORWARD-ONLY. The rollback path for a bad deploy is redeploying the retained
        /// previous archive, which does not reverse schema — and Program.cs runs Migrate() on the
        /// boot path, so a failed migration means the container does not serve at all. Do not read
        /// the presence of this method as a rollback story.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "SpineProbes");
        }
    }
}

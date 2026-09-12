using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenExCards.Migrations
{
    /// <inheritdoc />
    public partial class AddCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cards_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_OwnerId",
                table: "Cards",
                column: "OwnerId");
        }

        /// <inheritdoc />
        /// <remarks>
        /// EF generated this; it was not authored, and nothing relies on it. Migrations in this
        /// project are FORWARD-ONLY. The rollback path for a bad deploy is redeploying the retained
        /// previous archive, which does not reverse schema — and Program.cs runs Migrate() on the
        /// boot path, so a failed migration means the container does not serve at all. Do not read
        /// the presence of this method as a rollback story.
        ///
        /// This one matters more than its predecessors: it is the first Down() in this project that
        /// would destroy real product data. Running it drops every card every learner has saved.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cards");
        }
    }
}

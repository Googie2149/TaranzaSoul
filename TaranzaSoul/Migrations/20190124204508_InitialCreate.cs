using Microsoft.EntityFrameworkCore.Migrations;

namespace TaranzaSoul.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserList",
                columns: table => new
                {
                    UserId = table.Column<ulong>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApprovedAccess = table.Column<bool>(nullable: false),
                    NewAccount = table.Column<bool>(nullable: false),
                    ApprovalModId = table.Column<ulong>(nullable: false),
                    ApprovalReason = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserList", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "WatchedUserList",
                columns: table => new
                {
                    UserId = table.Column<ulong>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reason = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedUserList", x => x.UserId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserList");

            migrationBuilder.DropTable(
                name: "WatchedUserList");
        }
    }
}

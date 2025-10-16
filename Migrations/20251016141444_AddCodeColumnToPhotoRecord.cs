using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoApp.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeColumnToPhotoRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Title",
                table: "Photos",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Photos",
                newName: "PhotoPath");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Photos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Photos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Photos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Supplier",
                table: "Photos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Photos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "Supplier",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Photos");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Photos",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "PhotoPath",
                table: "Photos",
                newName: "Description");
        }
    }
}

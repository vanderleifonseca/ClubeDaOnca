using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubeDaOnca.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DependenteUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UsuarioId",
                table: "Dependentes",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dependentes_UsuarioId",
                table: "Dependentes",
                column: "UsuarioId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Dependentes_AspNetUsers_UsuarioId",
                table: "Dependentes",
                column: "UsuarioId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Dependentes_AspNetUsers_UsuarioId",
                table: "Dependentes");

            migrationBuilder.DropIndex(
                name: "IX_Dependentes_UsuarioId",
                table: "Dependentes");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "Dependentes");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaAvaliacao.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarNumeroAlternativasProva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NumeroAlternativas",
                table: "Provas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumeroAlternativas",
                table: "Provas");
        }
    }
}

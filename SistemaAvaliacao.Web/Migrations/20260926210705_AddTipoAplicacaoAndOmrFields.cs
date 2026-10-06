using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaAvaliacao.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddTipoAplicacaoAndOmrFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TipoAplicacao",
                table: "Provas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CaminhoDebugOmr",
                table: "LotesCorrecao",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConfiancaMedia",
                table: "LotesCorrecao",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TipoAplicacao",
                table: "Provas");

            migrationBuilder.DropColumn(
                name: "CaminhoDebugOmr",
                table: "LotesCorrecao");

            migrationBuilder.DropColumn(
                name: "ConfiancaMedia",
                table: "LotesCorrecao");
        }
    }
}

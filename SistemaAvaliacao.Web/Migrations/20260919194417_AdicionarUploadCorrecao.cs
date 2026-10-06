using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaAvaliacao.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarUploadCorrecao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaminhoArquivo",
                table: "LotesCorrecao",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CaminhoArquivo",
                table: "LotesCorrecao");
        }
    }
}

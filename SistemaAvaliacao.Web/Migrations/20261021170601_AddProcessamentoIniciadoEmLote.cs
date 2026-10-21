using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaAvaliacao.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessamentoIniciadoEmLote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessamentoIniciadoEm",
                table: "LotesCorrecao",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessamentoIniciadoEm",
                table: "LotesCorrecao");
        }
    }
}

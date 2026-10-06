using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaAvaliacao.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarCorrecaoProvasImpressas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<char>(
                name: "AlternativaMarcada",
                table: "Respostas",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(char),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "Observacao",
                table: "Respostas",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Revisada",
                table: "Respostas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Respostas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LotesCorrecao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProvaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NomeArquivo = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalFolhas = table.Column<int>(type: "INTEGER", nullable: false),
                    FolhasProcessadas = table.Column<int>(type: "INTEGER", nullable: false),
                    Erro = table.Column<string>(type: "TEXT", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinalizadoEm = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotesCorrecao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotesCorrecao_Provas_ProvaId",
                        column: x => x.ProvaId,
                        principalTable: "Provas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FolhasResposta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoteCorrecaoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProvaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AlunoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenIdentificacao = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    NumeroPagina = table.Column<int>(type: "INTEGER", nullable: false),
                    Processada = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProcessadaEm = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolhasResposta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FolhasResposta_LotesCorrecao_LoteCorrecaoId",
                        column: x => x.LoteCorrecaoId,
                        principalTable: "LotesCorrecao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FolhasResposta_Provas_ProvaId",
                        column: x => x.ProvaId,
                        principalTable: "Provas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FolhasResposta_Usuarios_AlunoId",
                        column: x => x.AlunoId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeiturasResposta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FolhaRespostaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestaoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AlternativaDetectada = table.Column<char>(type: "TEXT", nullable: true),
                    AlternativaConfirmada = table.Column<char>(type: "TEXT", nullable: true),
                    Confianca = table.Column<decimal>(type: "TEXT", nullable: true),
                    Revisada = table.Column<bool>(type: "INTEGER", nullable: false),
                    Observacao = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeiturasResposta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeiturasResposta_FolhasResposta_FolhaRespostaId",
                        column: x => x.FolhaRespostaId,
                        principalTable: "FolhasResposta",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeiturasResposta_Questoes_QuestaoId",
                        column: x => x.QuestaoId,
                        principalTable: "Questoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolhasResposta_AlunoId",
                table: "FolhasResposta",
                column: "AlunoId");

            migrationBuilder.CreateIndex(
                name: "IX_FolhasResposta_LoteCorrecaoId",
                table: "FolhasResposta",
                column: "LoteCorrecaoId");

            migrationBuilder.CreateIndex(
                name: "IX_FolhasResposta_ProvaId_AlunoId",
                table: "FolhasResposta",
                columns: new[] { "ProvaId", "AlunoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FolhasResposta_TokenIdentificacao",
                table: "FolhasResposta",
                column: "TokenIdentificacao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeiturasResposta_FolhaRespostaId_QuestaoId",
                table: "LeiturasResposta",
                columns: new[] { "FolhaRespostaId", "QuestaoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeiturasResposta_QuestaoId",
                table: "LeiturasResposta",
                column: "QuestaoId");

            migrationBuilder.CreateIndex(
                name: "IX_LotesCorrecao_ProvaId",
                table: "LotesCorrecao",
                column: "ProvaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeiturasResposta");

            migrationBuilder.DropTable(
                name: "FolhasResposta");

            migrationBuilder.DropTable(
                name: "LotesCorrecao");

            migrationBuilder.DropColumn(
                name: "Observacao",
                table: "Respostas");

            migrationBuilder.DropColumn(
                name: "Revisada",
                table: "Respostas");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Respostas");

            migrationBuilder.AlterColumn<char>(
                name: "AlternativaMarcada",
                table: "Respostas",
                type: "TEXT",
                nullable: false,
                defaultValue: '\0',
                oldClrType: typeof(char),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaAvaliacao.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Questoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enunciado = table.Column<string>(type: "TEXT", nullable: true),
                    TextoBase = table.Column<string>(type: "TEXT", nullable: true),
                    ImagemUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Comando = table.Column<string>(type: "TEXT", nullable: false),
                    DescricaoAcessibilidade = table.Column<string>(type: "TEXT", nullable: true),
                    AlternativaA = table.Column<string>(type: "TEXT", nullable: false),
                    AlternativaB = table.Column<string>(type: "TEXT", nullable: false),
                    AlternativaC = table.Column<string>(type: "TEXT", nullable: true),
                    AlternativaD = table.Column<string>(type: "TEXT", nullable: true),
                    AlternativaE = table.Column<string>(type: "TEXT", nullable: true),
                    NumeroAlternativas = table.Column<int>(type: "INTEGER", nullable: false),
                    AlternativaCorreta = table.Column<char>(type: "TEXT", nullable: false),
                    Fonte = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CPF = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    Matricula = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Perfil = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Provas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DataInicio = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DataFim = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TokenPublico = table.Column<Guid>(type: "TEXT", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Provas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Provas_Usuarios_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProvaAlunos",
                columns: table => new
                {
                    ProvaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AlunoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IniciadaEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinalizadaEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TokenTentativa = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvaAlunos", x => new { x.ProvaId, x.AlunoId });
                    table.ForeignKey(
                        name: "FK_ProvaAlunos_Provas_ProvaId",
                        column: x => x.ProvaId,
                        principalTable: "Provas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProvaAlunos_Usuarios_AlunoId",
                        column: x => x.AlunoId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProvaQuestoes",
                columns: table => new
                {
                    ProvaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestaoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvaQuestoes", x => new { x.ProvaId, x.QuestaoId });
                    table.ForeignKey(
                        name: "FK_ProvaQuestoes_Provas_ProvaId",
                        column: x => x.ProvaId,
                        principalTable: "Provas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProvaQuestoes_Questoes_QuestaoId",
                        column: x => x.QuestaoId,
                        principalTable: "Questoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Respostas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProvaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AlunoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestaoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AlternativaMarcada = table.Column<char>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Respostas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Respostas_ProvaAlunos_ProvaId_AlunoId",
                        columns: x => new { x.ProvaId, x.AlunoId },
                        principalTable: "ProvaAlunos",
                        principalColumns: new[] { "ProvaId", "AlunoId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Respostas_Questoes_QuestaoId",
                        column: x => x.QuestaoId,
                        principalTable: "Questoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProvaAlunos_AlunoId",
                table: "ProvaAlunos",
                column: "AlunoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvaQuestoes_ProvaId_Ordem",
                table: "ProvaQuestoes",
                columns: new[] { "ProvaId", "Ordem" });

            migrationBuilder.CreateIndex(
                name: "IX_ProvaQuestoes_QuestaoId",
                table: "ProvaQuestoes",
                column: "QuestaoId");

            migrationBuilder.CreateIndex(
                name: "IX_Provas_CriadoPorId",
                table: "Provas",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Provas_TokenPublico",
                table: "Provas",
                column: "TokenPublico",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Respostas_ProvaId_AlunoId_QuestaoId",
                table: "Respostas",
                columns: new[] { "ProvaId", "AlunoId", "QuestaoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Respostas_QuestaoId",
                table: "Respostas",
                column: "QuestaoId");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_CPF",
                table: "Usuarios",
                column: "CPF",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProvaQuestoes");

            migrationBuilder.DropTable(
                name: "Respostas");

            migrationBuilder.DropTable(
                name: "ProvaAlunos");

            migrationBuilder.DropTable(
                name: "Questoes");

            migrationBuilder.DropTable(
                name: "Provas");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}

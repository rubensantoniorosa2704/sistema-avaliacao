using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Resultado consolidado de uma importação de alunos via CSV.
/// </summary>
public class ResultadoImportacaoAlunos
{
    public int Inseridos { get; set; }
    public int Vinculados { get; set; }
    public int Ignorados { get; set; }
    public List<string> Erros { get; } = new();

    /// <summary>Monta a mensagem de resumo para exibir ao usuário.</summary>
    public string Resumo()
    {
        var msg = $"{Vinculados} aluno(s) vinculado(s) à prova";
        if (Inseridos > 0) msg += $", {Inseridos} novo(s) cadastrado(s)";
        if (Ignorados > 0) msg += $", {Ignorados} já vinculado(s)";
        return msg + ".";
    }
}

/// <summary>
/// Importa alunos de um CSV (Nome; Matrícula; Email; CPF) e os vincula a uma prova.
/// Upsert por CPF: aluno existente é apenas vinculado; aluno novo é criado com senha = CPF.
/// </summary>
public class ImportacaoAlunosService
{
    private readonly ApplicationDbContext _context;
    private readonly PasswordService _passwordService;

    public ImportacaoAlunosService(ApplicationDbContext context, PasswordService passwordService)
    {
        _context = context;
        _passwordService = passwordService;
    }

    public async Task<ResultadoImportacaoAlunos> ImportarAsync(Prova prova, Stream csv)
    {
        var resultado = new ResultadoImportacaoAlunos();
        
        // Usar transação para garantir atomicidade: tudo-ou-nada
        using var transaction = await _context.Database.BeginTransactionAsync();
        
        try
        {
            var jaVinculados = prova.ProvaAlunos.Select(pa => pa.AlunoId).ToHashSet();
            var novosPorCpf = new Dictionary<string, Usuario>();

            using var reader = new StreamReader(csv);
            var linhaNum = 0;

            while (!reader.EndOfStream)
            {
                var linha = await reader.ReadLineAsync();
                linhaNum++;

                if (string.IsNullOrWhiteSpace(linha)) continue;

                // Pular cabeçalho
                if (linhaNum == 1 && linha.Contains("nome", StringComparison.OrdinalIgnoreCase))
                    continue;

                var (usuario, erro) = await ProcessarLinhaAsync(linha, linhaNum, novosPorCpf);

                if (erro != null)
                {
                    resultado.Erros.Add(erro);
                    continue;
                }

                // Vincular à prova (se ainda não estiver)
                if (jaVinculados.Add(usuario!.Id))
                {
                    _context.ProvaAlunos.Add(new ProvaAluno
                    {
                        ProvaId = prova.Id,
                        AlunoId = usuario.Id,
                        Status = StatusProvaAluno.NaoIniciada
                    });
                    resultado.Vinculados++;
                }
                else
                {
                    resultado.Ignorados++;
                }
            }

            resultado.Inseridos = novosPorCpf.Count;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return resultado;
    }

    /// <summary>
    /// Faz o parse de uma linha e devolve o usuário (existente ou novo) ou uma mensagem de erro.
    /// </summary>
    private async Task<(Usuario? usuario, string? erro)> ProcessarLinhaAsync(
        string linha, int linhaNum, Dictionary<string, Usuario> novosPorCpf)
    {
        var sep = DetectarSeparador(linha);
        var campos = LerCamposCsv(linha, sep);

        if (campos.Count < 4)
            return (null, $"Linha {linhaNum}: esperados 4 campos (Nome, Matrícula, Email, CPF), encontrados {campos.Count}");

        var nome = campos[0].Trim();
        var matricula = campos[1].Trim();
        var email = campos[2].Trim().ToLowerInvariant();
        var cpf = new string(campos[3].Where(char.IsDigit).ToArray());

        if (string.IsNullOrWhiteSpace(nome))
            return (null, $"Linha {linhaNum}: nome vazio");

        // CPF deve ter EXATAMENTE 11 dígitos. O erro mais comum aqui é o Excel formatar o
        // CPF como número e comer o(s) zero(s) à esquerda ao salvar o CSV (ex.: 01234567890
        // vira 1234567890). Por isso a mensagem para "menos de 11" aponta a causa provável,
        // em vez de um "CPF inválido" genérico que não ajuda quem está importando.
        if (cpf.Length < 11)
            return (null, $"Linha {linhaNum}: CPF com {cpf.Length} dígito(s) (esperado 11). " +
                          "Provável zero à esquerda removido pelo Excel — formate a coluna de CPF como Texto antes de exportar o CSV.");

        if (cpf.Length > 11)
            return (null, $"Linha {linhaNum}: CPF com {cpf.Length} dígitos (esperado 11). Verifique se há dígitos a mais.");

        // Já criado nesta mesma importação?
        if (novosPorCpf.TryGetValue(cpf, out var existenteNoLote))
            return (existenteNoLote, null);

        // Já existe no banco?
        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.CPF == cpf);
        if (usuario != null)
            return (usuario, null);

        // Novo: validar email único
        if (!string.IsNullOrWhiteSpace(email) && await _context.Usuarios.AnyAsync(u => u.Email == email))
            return (null, $"Linha {linhaNum}: email {email} já cadastrado para outro usuário");

        usuario = new Usuario
        {
            Nome = nome,
            Email = string.IsNullOrWhiteSpace(email) ? $"{cpf}@aluno.local" : email,
            CPF = cpf,
            Matricula = string.IsNullOrWhiteSpace(matricula) ? null : matricula,
            PasswordHash = _passwordService.HashPassword(cpf), // aluno não faz login; campo obrigatório
            Perfil = PerfilUsuario.Aluno,
            CreatedAt = DateTime.UtcNow
        };

        _context.Usuarios.Add(usuario);
        novosPorCpf[cpf] = usuario;
        return (usuario, null);
    }

    /// <summary>
    /// Identifica o separador sem se confundir com vírgulas presentes em campos entre aspas.
    /// O modelo oficial usa ponto e vírgula, mas arquivos CSV exportados pelo Excel podem usar
    /// vírgula dependendo da configuração regional.
    /// </summary>
    private static char DetectarSeparador(string linha)
    {
        var dentroDeAspas = false;
        var pontoEVirgula = 0;
        var virgulas = 0;

        for (var i = 0; i < linha.Length; i++)
        {
            if (linha[i] == '"')
            {
                if (dentroDeAspas && i + 1 < linha.Length && linha[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                dentroDeAspas = !dentroDeAspas;
            }
            else if (!dentroDeAspas)
            {
                if (linha[i] == ';') pontoEVirgula++;
                if (linha[i] == ',') virgulas++;
            }
        }

        return pontoEVirgula >= virgulas ? ';' : ',';
    }

    /// <summary>Parser CSV pequeno, compatível com aspas duplas e aspas escapadas ("").</summary>
    private static List<string> LerCamposCsv(string linha, char separador)
    {
        var campos = new List<string>();
        var atual = new System.Text.StringBuilder();
        var dentroDeAspas = false;

        for (var i = 0; i < linha.Length; i++)
        {
            var c = linha[i];
            if (c == '"')
            {
                if (dentroDeAspas && i + 1 < linha.Length && linha[i + 1] == '"')
                {
                    atual.Append('"');
                    i++;
                }
                else
                {
                    dentroDeAspas = !dentroDeAspas;
                }
            }
            else if (c == separador && !dentroDeAspas)
            {
                campos.Add(atual.ToString());
                atual.Clear();
            }
            else
            {
                atual.Append(c);
            }
        }

        campos.Add(atual.ToString());
        return campos;
    }
}

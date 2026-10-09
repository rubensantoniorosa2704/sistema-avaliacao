using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Acesso a dados e regras dos alunos vinculados a uma prova (vínculo/desvínculo).
/// O parse e a gravação em lote do CSV permanecem em <see cref="ImportacaoAlunosService"/>;
/// este serviço cuida de carregar a prova para importação e remover vínculos individuais.
/// </summary>
public class AlunoProvaService(ApplicationDbContext context)
{
    /// <summary>
    /// Carrega a prova com seus alunos (necessário para a importação em lote).
    /// Null se a prova não existe.
    /// </summary>
    public Task<Prova?> ObterProvaParaImportacaoAsync(Guid provaId)
        => context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == provaId);

    public enum RemocaoResultado { Ok, NaoEncontrado, JaIniciou }

    /// <summary>
    /// Remove o vínculo de um aluno com a prova. Bloqueado quando o aluno já iniciou
    /// (IniciadaEm != null). NaoEncontrado se o vínculo não existe.
    /// </summary>
    public async Task<RemocaoResultado> RemoverAsync(Guid provaId, Guid alunoId)
    {
        var provaAluno = await context.ProvaAlunos
            .FirstOrDefaultAsync(pa => pa.ProvaId == provaId && pa.AlunoId == alunoId);

        if (provaAluno is null) return RemocaoResultado.NaoEncontrado;

        if (provaAluno.IniciadaEm != null) return RemocaoResultado.JaIniciou;

        context.ProvaAlunos.Remove(provaAluno);
        await context.SaveChangesAsync();
        return RemocaoResultado.Ok;
    }
}

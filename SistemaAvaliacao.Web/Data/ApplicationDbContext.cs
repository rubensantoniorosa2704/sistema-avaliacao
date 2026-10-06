using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Models.Entities;

namespace SistemaAvaliacao.Web.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Usuario> Usuarios { get; set; }
    public DbSet<Questao> Questoes { get; set; }
    public DbSet<Prova> Provas { get; set; }
    public DbSet<ProvaQuestao> ProvaQuestoes { get; set; }
    public DbSet<ProvaAluno> ProvaAlunos { get; set; }
    public DbSet<Resposta> Respostas { get; set; }
    public DbSet<LoteCorrecao> LotesCorrecao { get; set; }
    public DbSet<FolhaResposta> FolhasResposta { get; set; }
    public DbSet<LeituraResposta> LeiturasResposta { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ===== USUARIO =====

        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.CPF)
            .IsUnique();

        // ===== PROVA =====

        // Link público da prova: único.
        modelBuilder.Entity<Prova>()
            .HasIndex(p => p.TokenPublico)
            .IsUnique();

        // Prova -> Usuario (Admin que criou)
        modelBuilder.Entity<Prova>()
            .HasOne(p => p.CriadoPor)
            .WithMany(u => u.ProvasCriadas)
            .HasForeignKey(p => p.CriadoPorId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== PROVA-QUESTAO =====

        modelBuilder.Entity<ProvaQuestao>()
            .HasKey(pq => new { pq.ProvaId, pq.QuestaoId });

        modelBuilder.Entity<ProvaQuestao>()
            .HasOne(pq => pq.Prova)
            .WithMany(p => p.ProvaQuestoes)
            .HasForeignKey(pq => pq.ProvaId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProvaQuestao>()
            .HasOne(pq => pq.Questao)
            .WithMany(q => q.ProvaQuestoes)
            .HasForeignKey(pq => pq.QuestaoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProvaQuestao>()
            .HasIndex(pq => new { pq.ProvaId, pq.Ordem });

        // ===== PROVA-ALUNO =====

        modelBuilder.Entity<ProvaAluno>()
            .HasKey(pa => new { pa.ProvaId, pa.AlunoId });

        modelBuilder.Entity<ProvaAluno>()
            .HasOne(pa => pa.Prova)
            .WithMany(p => p.ProvaAlunos)
            .HasForeignKey(pa => pa.ProvaId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProvaAluno>()
            .HasOne(pa => pa.Aluno)
            .WithMany(u => u.ProvasComoAluno)
            .HasForeignKey(pa => pa.AlunoId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== RESPOSTA =====

        modelBuilder.Entity<Resposta>()
            .HasOne(r => r.ProvaAluno)
            .WithMany(pa => pa.Respostas)
            .HasForeignKey(r => new { r.ProvaId, r.AlunoId })
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Resposta>()
            .HasOne(r => r.Questao)
            .WithMany(q => q.Respostas)
            .HasForeignKey(r => r.QuestaoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Um aluno responde cada questão no máximo uma vez por prova.
        modelBuilder.Entity<Resposta>()
            .HasIndex(r => new { r.ProvaId, r.AlunoId, r.QuestaoId })
            .IsUnique();

        // ===== CORREÇÃO DE PROVAS IMPRESSAS =====

        modelBuilder.Entity<LoteCorrecao>()
            .HasOne(l => l.Prova)
            .WithMany()
            .HasForeignKey(l => l.ProvaId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FolhaResposta>()
            .HasOne(f => f.LoteCorrecao)
            .WithMany(l => l.Folhas)
            .HasForeignKey(f => f.LoteCorrecaoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FolhaResposta>()
            .HasOne(f => f.Prova)
            .WithMany()
            .HasForeignKey(f => f.ProvaId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FolhaResposta>()
            .HasOne(f => f.Aluno)
            .WithMany()
            .HasForeignKey(f => f.AlunoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FolhaResposta>()
            .HasIndex(f => new { f.ProvaId, f.AlunoId })
            .IsUnique();

        modelBuilder.Entity<FolhaResposta>()
            .HasIndex(f => f.TokenIdentificacao)
            .IsUnique();

        modelBuilder.Entity<LeituraResposta>()
            .HasOne(l => l.FolhaResposta)
            .WithMany(f => f.Leituras)
            .HasForeignKey(l => l.FolhaRespostaId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LeituraResposta>()
            .HasOne(l => l.Questao)
            .WithMany()
            .HasForeignKey(l => l.QuestaoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LeituraResposta>()
            .HasIndex(l => new { l.FolhaRespostaId, l.QuestaoId })
            .IsUnique();
    }
}

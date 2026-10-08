# Migração de SQLite para PostgreSQL (Neon/Supabase)

Este documento descreve como trocar o banco de dados da aplicação de **SQLite**
(provedor atual) para **PostgreSQL** gerenciado (Neon ou Supabase), no futuro.

A codebase já foi **desacoplada** do provedor: toda a dependência de banco está
isolada em um único arquivo, `SistemaAvaliacao.Web/Data/DatabaseConfiguration.cs`.
Nenhum controller, serviço ou entidade precisa mudar para migrar.

---

## 1. Contexto e por que migrar

### Arquitetura atual
- **SistemaAvaliacao.Web** (ASP.NET Core MVC) e **SistemaAvaliacao.CorrecaoWorker**
  (worker de OMR) compartilham o **mesmo arquivo SQLite**, num volume persistente
  do Fly.io (`/data/sistema_avaliacao.db`).
- Acesso 100% via EF Core 8, sem SQL cru dependente de SQLite (exceto PRAGMAs,
  já isolados — ver seção 5).

### Ganho da migração
O ganho é **arquitetural**, não de velocidade bruta de query:
- **Concorrência de escrita real** — SQLite serializa escritas num lock de arquivo
  único; PostgreSQL usa MVCC (lock por linha). Com muitos alunos respondendo ao
  mesmo tempo, cada aluno escreve na sua própria linha de `Respostas` em paralelo.
- **Escala horizontal** — hoje é impossível rodar mais de uma instância Web
  (o arquivo SQLite vive no volume local de uma máquina). Com Postgres externo,
  Web e Worker podem escalar e rodar em máquinas separadas.
- **Durabilidade** — backups automáticos e point-in-time recovery, em vez de
  copiar um arquivo.

### Quando NÃO migrar
Um teste de carga local (1000 escritas concorrentes, com WAL + busy_timeout=5000)
mostrou **0 falhas `database is locked`** e todas as 1000 gravações concluídas —
porém com latência de cauda alta (p99 ≈ 1,2s; máx ≈ 2,1s). Ou seja: no volume
atual o SQLite **não quebra**, apenas serializa. Migre quando precisar de escala
horizontal, menor latência de cauda sob pico, ou durabilidade gerenciada.

---

## 2. Qual provedor escolher: Neon vs Supabase

**Recomendação: Neon.** Justificativas específicas para este projeto:
- O sistema já tem autenticação própria (cookie + BCrypt) e upload em disco.
  Os diferenciais do Supabase (Auth, Storage, Realtime) **não agregam** aqui.
- Precisamos de **PostgreSQL puro** acessado via EF Core/Npgsql — é o que o Neon
  entrega de forma mais enxuta, com scale-to-zero (bom para uso em picos de prova)
  e branching de banco (útil para testar migrations antes de produção).

Escolha a região **`sa-east-1` (São Paulo)** para ficar próximo do Fly.io `gru`
e minimizar a latência de rede (que passa a existir, já que o banco deixa de ser
in-process).

Supabase só valeria se o plano fosse abandonar a stack .NET e usar as APIs/Auth
prontas — não é o caso.

---

## 3. Passo a passo da migração

### Passo 1 — Provisionar o banco
1. Crie um projeto PostgreSQL no Neon (região `sa-east-1`).
2. Copie a **connection string** com SSL. Formato esperado pelo Npgsql:
   ```
   Host=<host>;Database=<db>;Username=<user>;Password=<senha>;SSL Mode=Require;Trust Server Certificate=true
   ```
   > Dica: Neon fornece a URL no formato `postgres://...`. Converta para o formato
   > chave=valor do Npgsql, ou use `Npgsql.NpgsqlConnectionStringBuilder`.
3. Use o endpoint com **pooling** (PgBouncer do Neon) para suportar muitas
   conexões concorrentes.

### Passo 2 — Adicionar o pacote Npgsql
No projeto `SistemaAvaliacao.Web` (o Worker herda via ProjectReference):
```bash
dotnet add SistemaAvaliacao.Web/SistemaAvaliacao.Web.csproj \
  package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.*
```
> Use a versão **8.0.x** para casar com o EF Core 8 já usado no projeto.

### Passo 3 — Habilitar o provedor no código
Edite **somente** `SistemaAvaliacao.Web/Data/DatabaseConfiguration.cs`,
no método `Configure`, trocando o `throw` do ramo Postgres por:
```csharp
case DatabaseProvider.Postgres:
    options.UseNpgsql(connectionString);
    break;
```
Nenhuma outra mudança de código é necessária. O gate de PRAGMAs em
`DbInitializer` (`if (provider.EhSqlite())`) já evita rodar comandos SQLite
no Postgres.

### Passo 4 — Regenerar as migrations
As migrations atuais têm tipos específicos de SQLite (`TEXT`, `INTEGER`) e **não**
podem ser reaproveitadas. Com `Database:Provider=Postgres` e a connection string
definida (ver Passo 6), regenere do zero:
```bash
# Apague a pasta de migrations antiga (faça isso num branch!)
#   SistemaAvaliacao.Web/Migrations/

# Gere a migration inicial para Postgres
dotnet ef migrations add InitialCreatePostgres \
  --project SistemaAvaliacao.Web/SistemaAvaliacao.Web.csproj
```
As migrations serão aplicadas automaticamente no startup
(`DbInitializer.Initialize` chama `context.Database.Migrate()`).

### Passo 5 — Migrar os dados existentes
O banco SQLite atual tem dados reais. Opções:
- **pgloader** (recomendado para carga única):
  ```bash
  pgloader ./data/sistema_avaliacao.db "postgresql://<conn>"
  ```
  Valide tipos após a carga (ver seção 5: `char`, `decimal`, `uuid`, datas).
- **Script EF/ETL** próprio: ler via DbContext SQLite e gravar via DbContext
  Postgres. Mais trabalhoso, porém mais controlável para normalizações.

> Se o sistema puder começar "do zero" no Postgres (sem histórico), pule este
> passo — o seed de admin via variáveis de ambiente recria o usuário inicial.

### Passo 6 — Configurar provedor e connection string
Defina em produção (Fly.io) e em dev:
- `Database:Provider = Postgres`
  - via appsettings: `"Database": { "Provider": "Postgres" }`
  - ou env var: `DATABASE__PROVIDER=Postgres`
- Connection string via env var: `SISTEMA_AVALIACAO_CONNECTION_STRING=<conn Npgsql>`

No Fly.io, trate a connection string como **secret**:
```bash
fly secrets set SISTEMA_AVALIACAO_CONNECTION_STRING="Host=...;SSL Mode=Require;..."
fly secrets set DATABASE__PROVIDER="Postgres"
```

### Passo 7 — Ajustar o deploy (Fly.io / Docker)
Com o banco externo, o volume e o seed de arquivo SQLite deixam de ser necessários:
- **`fly.toml`**: remover o bloco `[[mounts]]` (volume `/data`) **se** nenhuma
  outra coisa usar `/data`. Atenção: as chaves de Data Protection usam
  `DATA_PROTECTION_KEYS_PATH=/data/keys` — se remover o volume, aponte as chaves
  para outro armazenamento persistente, senão os cookies de sessão serão
  invalidados a cada deploy.
- **`deploy/entrypoint.sh`**: remover a cópia de `/seed/sistema_avaliacao.db`
  (o `if [ ! -f "$SISTEMA_AVALIACAO_DB" ]`).
- **`Dockerfile`**: remover `COPY data/sistema_avaliacao.db /seed/...` e as
  env vars `SISTEMA_AVALIACAO_DB`.
- Com Postgres, Web e Worker **podem** ser separados em máquinas/processos
  distintos (não precisam mais do mesmo arquivo).

### Passo 8 — Testar
Valide em um branch do Neon antes de produção:
- App sobe, aplica migrations, seed de admin funciona.
- Fluxo de prova: identificar aluno, salvar/limpar resposta, finalizar.
- Login case-insensitive por email (ver seção 5, item 2).
- Worker de OMR grava leituras.
- Teste de concorrência (muitos alunos salvando ao mesmo tempo).

---

## 4. Rollback

A troca é controlada por configuração. Para voltar ao SQLite:
- `Database:Provider=Sqlite` (ou remover a variável; o default é Sqlite).
- Garantir que `SISTEMA_AVALIACAO_DB` aponte para o arquivo e que o volume exista.
- Reverter o branch de código que regenerou as migrations (as migrations SQLite
  e as Postgres **não** são intercambiáveis).

Mantenha o arquivo SQLite de origem preservado durante a janela de migração.

---

## 5. Pontos de atenção técnicos

Diferenças concretas entre SQLite e PostgreSQL que afetam este projeto:

1. **PRAGMAs** — já isolados em `DbInitializer` atrás de `provider.EhSqlite()`.
   Não executam no Postgres. Nenhuma ação.

2. **`.ToLower()` em query de login** (`ExameController.Identificar`):
   `pa.Aluno.Email.ToLower() == email`. Traduz para `lower()` em ambos, mas o
   comportamento de case/acentuação difere. Como os emails **já são gravados em
   lowercase** (seed e importação), o risco é baixo — mas valide no Passo 8.
   Alternativa robusta: coluna `citext` ou comparação `ILIKE`.

3. **Tipo `char`** (`AlternativaMarcada`, `AlternativaCorreta`,
   `AlternativaDetectada`): no SQLite vira `TEXT`; no Postgres o EF mapeia para
   `character(1)`. Funciona; confirme na migration regenerada.

4. **`decimal? Confianca`** (`LeituraResposta`): no SQLite é `TEXT`; no Postgres
   vira `numeric`. Melhoria de precisão, sem ação.

5. **GUID → `uuid`**: todas as PKs são `Guid`. No Postgres mapeiam nativamente
   para `uuid` (melhor que o `TEXT` do SQLite). Sem ação.

6. **Datas em UTC**: `Helpers/DataHora.cs` já padroniza tudo em UTC — exatamente
   o que o Npgsql espera (`timestamp with time zone`). O Npgsql é **rígido**:
   `DateTime` gravado deve ter `Kind=Utc`. O código usa `DateTime.UtcNow`, o que
   está correto. Se surgir erro de "Cannot write DateTime with Kind=Unspecified",
   verifique conversões em `DataHora`.

7. **Latência de rede**: o banco deixa de ser in-process. O endpoint
   `SalvarResposta` faz uma escrita por marcação. Com banco remoto, considere:
   - **Connection pooling** (pooler do Neon + pool do Npgsql) — essencial.
   - Avaliar **salvar em lote** (debounce no front) para reduzir round-trips.

---

## 6. Checklist

- [ ] Projeto Neon criado em `sa-east-1`, connection string com SSL obtida.
- [ ] Pacote `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.x adicionado.
- [ ] `DatabaseConfiguration.Configure` habilitado para `UseNpgsql`.
- [ ] Migrations antigas removidas e `InitialCreatePostgres` gerada.
- [ ] Dados migrados (pgloader/ETL) e tipos validados — ou início limpo.
- [ ] `DATABASE__PROVIDER=Postgres` e `SISTEMA_AVALIACAO_CONNECTION_STRING`
      configurados (secret no Fly.io).
- [ ] Deploy ajustado (volume, entrypoint, Dockerfile; chaves de Data Protection).
- [ ] Testes do Passo 8 OK em um branch do Neon.
- [ ] Plano de rollback documentado e arquivo SQLite preservado.

---

## Referências de código

| O quê | Onde |
|---|---|
| Configuração do provedor (único ponto a mudar) | `SistemaAvaliacao.Web/Data/DatabaseConfiguration.cs` |
| Startup Web | `SistemaAvaliacao.Web/Program.cs` |
| Startup Worker | `SistemaAvaliacao.CorrecaoWorker/Program.cs` |
| Migrations + PRAGMAs (gate SQLite) | `SistemaAvaliacao.Web/Data/DbInitializer.cs` |
| Mapeamentos EF | `SistemaAvaliacao.Web/Data/ApplicationDbContext.cs` |
| Datas/UTC | `SistemaAvaliacao.Web/Helpers/DataHora.cs` |
| Deploy | `fly.toml`, `Dockerfile`, `deploy/entrypoint.sh` |

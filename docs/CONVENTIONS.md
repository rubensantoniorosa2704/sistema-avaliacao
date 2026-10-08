# Convenções do Projeto — Sistema de Avaliação

Guia curto e prático. Documenta os padrões que o projeto **já segue**, para
mantê-los consistentes ao longo do tempo. A filosofia é deliberada: **monólito
ASP.NET Core MVC, simples e coeso**, adequado ao estágio atual. Não adotamos
DDD/Clean Arch/microsserviços — adicionariam cerimônia sem ganho nesta escala.

---

## 1. Arquitetura e camadas

Fluxo de uma requisição:

```
Controller  →  Service (quando há lógica)  →  ApplicationDbContext (EF Core)
   (fino)        (lógica de negócio)              (acesso a dados)
```

- **Controller**: fino. Valida entrada, orquestra, devolve View/JSON. Não contém
  regra de negócio pesada nem acesso a dados complexo.
- **Service**: lógica de verdade (OMR, exportação, importação, PDF, tentativas).
  Nomeados por responsabilidade: `TentativaExameService`, `ExportacaoXlsxService`.
- **ApplicationDbContext**: único ponto de acesso a dados. Não envelopamos em
  Repository — o `DbContext` já é Unit of Work + repositório.
- **ViewModel ≠ Entidade**: controllers nunca entregam entidades à view. Sempre
  um ViewModel/DTO em `Models/ViewModels/`. O gabarito (`AlternativaCorreta`)
  **nunca** vai para a tela do aluno.

## 2. Estrutura de pastas (mantida como está)

```
SistemaAvaliacao.Web/
  Controllers/        # 1 controller por área (Home, Account, Admin, Correcao, Exame)
  Services/           # lógica de negócio; subpasta Omr/ para o pipeline de leitura óptica
  Data/               # ApplicationDbContext, DbInitializer, DatabaseConfiguration
  Models/
    Entities/         # entidades EF (persistidas)
    Enums/            # 1 enum por arquivo
    ViewModels/       # DTOs de view (ver §5)
  Migrations/         # geradas pelo EF — não editar à mão
  Helpers/            # utilitários puros (ex: DataHora)
  Views/              # ver §6
  wwwroot/            # css/, js/, lib/ (ver §7)
SistemaAvaliacao.CorrecaoWorker/   # worker de OMR (compartilha o DbContext da Web)
```

## 3. Nomenclatura

- **Domínio em português** (consistente): `Prova`, `Questao`, `Resposta`,
  `ProvaAluno`, `TentativaExameService`. Mantenha assim.
- **Classes/métodos/propriedades**: `PascalCase`.
- **Campos privados**: `_camelCase` (ex: `_context`, `_logger`).
- **Variáveis locais/parâmetros**: `camelCase`.
- **Async**: sufixo `Async` em métodos assíncronos (`FinalizarAsync`).
- **Arquivos**: um por tipo principal; agrupamentos de DTOs relacionados usam
  plural (`ExameViewModels.cs`, `CorrecaoViewModels.cs`). O nome do arquivo
  **deve** refletir o conteúdo.

## 4. Controllers

- Autorização por atributo: `[Authorize(Roles = "Admin")]` na classe.
- `[ValidateAntiForgeryToken]` em todos os POST.
- **Feedback ao usuário**, padrão por tipo de ação:
  - **Full-page / redirect**: `TempData["Sucesso"]` / `TempData["Erro"]`.
  - **AJAX**: retornar JSON (`Json(new { sucesso, erro })`) ou `Ok/BadRequest`.
- Lógica repetida entre actions vira método privado (ex: `AplicarEdicaoProvaAsync`).

## 5. ViewModels

- Vivem em `Models/ViewModels/`. São DTOs: propriedades + lógica de apresentação
  simples (ex: `StatusCss`, `PodeIniciar`). Sem acesso a dados.
- Validação de entrada via DataAnnotations (`[Required]`, `[StringLength]`).
  Regras cruzadas simples (ex: `DataFim > DataInicio`) podem ficar no controller.
- Mapeamento entidade→ViewModel é **manual e explícito** (sem AutoMapper).

## 6. Views (.cshtml)

- **Markup apenas.** Comportamento vai para `.js`; estilo para `.css` (ver §7).
- **Nada de `<script>`/`<style>` grandes inline.** Um bloco `<script>` mínimo só
  para passar dados do servidor ao JS externo (ex: `const token = '@Model.Token'`)
  é aceitável; a lógica fica no arquivo `.js`.
- **Partials para reuso e tamanho**: bloco repetido (modais) ou view acima de
  ~200 linhas → quebrar em `_Nome.cshtml`. Exemplos existentes: `_PaginaEstado`,
  `_Questao`.
- Telas de estado (confirmação/erro/aviso) usam o componente `_PaginaEstado`.

## 7. Assets (CSS/JS)

Regra (já declarada no topo de `wwwroot/css/site.css`):

> Estilo/comportamento específico de uma tela **não** fica inline na view.

- **CSS global e tokens**: `wwwroot/css/site.css` (`:root` com variáveis `--app-*`).
  Use os tokens; não repita cores/raios/sombras soltos.
- **CSS de uma tela**: arquivo próprio `wwwroot/css/<tela>.css`
  (ex: `exame-responder.css`), carregado via `@section Styles`.
- **JS de uma tela**: arquivo próprio `wwwroot/js/<tela>.js`, carregado via
  `@section Scripts`. Dados do servidor passam por `data-*` ou um `<script>`
  mínimo de variáveis.
- **Bibliotecas**: em `wwwroot/lib/` (jQuery, Bootstrap já locais).

## 8. Datas e fuso horário

- **Tudo em UTC** no banco e nas comparações. Conversão para o fuso da aplicação
  (Brasília) só nas bordas: entrada de formulário e exibição.
- Use sempre o helper `DataHora` (`ParaUtc`, `ParaLocal`) — nunca `DateTime.Now`.
- `DateTime.UtcNow` para timestamps. (Isso já deixa o código pronto para o
  PostgreSQL, que é rígido com `Kind=Utc`.)

## 9. Banco de dados

- EF Core + migrations. **Não editar migrations à mão**; gerar com
  `dotnet ef migrations add <Nome>`.
- O provedor é resolvido num único ponto: `Data/DatabaseConfiguration.cs`
  (hoje SQLite; preparado para PostgreSQL — ver `docs/migracao-sqlite-para-postgres.md`).
- Código específico de provedor (ex: PRAGMAs do SQLite) fica atrás de
  `provider.EhSqlite()`.
- Arquivos `.db` **não** são versionados (ver `.gitignore`). O banco nasce das
  migrations; o admin inicial, das variáveis `INITIAL_ADMIN_*`.

## 10. Segurança (lembretes)

- Senhas com BCrypt (`PasswordService`). Nunca logar/expor hash.
- Emails normalizados para lowercase na gravação e na busca.
- Uploads validados por extensão, MIME **e** conteúdo real (magic bytes).
- Não versionar segredos (`bootstrap.env`, `data/keys/`, `.db`).

## 11. Verificação antes de concluir

- `dotnet build` sem erros (idealmente sem warnings novos).
- Testar o fluxo afetado — com atenção especial ao **fluxo do aluno**
  (`ExameController` + `Views/Exame/`), que é crítico e não deve regredir.

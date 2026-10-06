# Integração OMR - Sistema de Avaliação

## Estrutura Implementada

A lógica de detecção OMR (Optical Mark Recognition) foi extraída como um serviço independente no projeto `SistemaAvaliacao.Web`:

**Localização:** `SistemaAvaliacao.Web/Services/Omr/`

### Componentes Principais

| Arquivo | Responsabilidade |
|---------|------------------|
| `SheetLayout.cs` | Geometria da folha (100 questões, 4 colunas, ArUco markers) |
| `OmrReader.cs` | Núcleo de leitura OMR (alinhamento, QR, medição de bolhas) |
| `AnswerClassifier.cs` | Classificação das respostas (Válida, Dupla, EmBranco, RasuraDuvidosa) |
| `PdfPageLoader.cs` | Carregamento de páginas PDF → Mat |
| `CardGenerator.cs` | Geração de cartão-resposta (para testes) |
| `DebugRenderer.cs` | Anotações visuais para debug |
| `OmrProcessingService.cs` | Orquestrador (PDF → Processamento → Resultados) |
| `OmrServiceFactory.cs` | Factory para criar instâncias configuradas |

---

## Como Usar

### Uso Básico (Uma Única Página)

```csharp
using SistemaAvaliacao.Web.Services.Omr;
using OpenCvSharp;

// Criar o serviço com configuração padrão
var omrService = OmrServiceFactory.CreateDefault(optionCount: 5, dpi: 200);

// Carregar uma página do PDF
var pageMat = PdfPageLoader.LoadPage("caminho/para/arquivo.pdf", pageNumber: 1);

// Processar a página
var resultado = omrService.ProcessSinglePage(
    pageMat, 
    pageNumber: 1, 
    debugDir: "wwwroot/uploads/debug"
);

// Acessar os resultados
if (resultado.Aligned)
{
    Console.WriteLine($"ExamId: {resultado.ExamId}");
    Console.WriteLine($"StudentId: {resultado.StudentId}");
    foreach (var q in resultado.Questions)
    {
        Console.WriteLine($"Q{q.Number}: {q.Status} => {q.DetectedOption}");
    }
}

pageMat.Dispose();
```

### Processamento de PDF Completo

```csharp
// Processar todas as páginas de um PDF
var pdfPath = "resposta-scan.pdf";
var debugDir = "wwwroot/uploads/correcoes/debug/lote1";

foreach (var (pageNum, result) in omrService.ProcessPdf(pdfPath, debugDir))
{
    if (result.Aligned)
    {
        // Persistir resultado no banco
        // ...
        Console.WriteLine($"Página {pageNum}: Alinhada, estudante {result.StudentId}");
    }
    else
    {
        Console.WriteLine($"Página {pageNum}: Não alinhada - {result.Warnings}");
    }
}
```

---

## Integração com CorrecaoWorker (Existente)

O `CorrecaoWorker.cs` atual continua funcionando com a lógica antiga. 

**Para usar a nova lógica OMR no worker:**

Crie um novo método em `CorrecaoWorker.cs` que chame a `OmrProcessingService`:

```csharp
private async Task<int> ProcessarComNovaLógicaOmrAsync(
    ApplicationDbContext context,
    LoteCorrecao lote,
    CancellationToken cancellationToken)
{
    var omrService = OmrServiceFactory.CreateDefault(
        optionCount: lote.Prova.NumeroAlternativas,
        dpi: 200
    );

    var debugDir = Path.Combine(
        _environment.WebRootPath,
        "uploads/correcoes/debug",
        lote.Id.ToString()
    );

    var processadas = 0;
    
    foreach (var (pageNum, resultado) in omrService.ProcessPdf(lote.CaminhoArquivo, debugDir))
    {
        if (!resultado.Aligned)
            continue;

        // Localizar a folha pelo QR
        var folha = await context.FolhasResposta
            .FirstOrDefaultAsync(f => f.TokenIdentificacao == resultado.QrPayload);
        
        if (folha == null)
            continue;

        // Persistir as leituras
        foreach (var q in resultado.Questions)
        {
            context.LeiturasResposta.Add(new LeiturasResposta
            {
                FolhaRespostaId = folha.Id,
                QuestaoId = q.Number,
                Status = q.Status.ToString(),
                DetectedOption = q.DetectedOption?.ToString(),
                Confidence = q.Confidence
                // ... outros campos
            });
        }

        folha.LoteCorrecaoId = lote.Id;
        folha.Processada = true;
        folha.ProcessadaEm = DateTime.UtcNow;
        folha.NumeroPagina = pageNum;

        processadas++;
    }

    await context.SaveChangesAsync(cancellationToken);
    return processadas;
}
```

---

## Configuração Customizada

Se precisar de valores diferentes dos padrões:

```csharp
// Opção 1: Usar factory com parâmetros
var omr = OmrServiceFactory.CreateDefault(
    optionCount: 4,  // 4 alternativas (A-D)
    dpi: 300         // Resolução mais alta
);

// Opção 2: Configuração manual (full control)
var layout = new SheetLayout();
var classifier = new AnswerClassifier(
    layout,
    new ClassifierThresholds
    {
        EmptyCeiling = 0.25,           // Bolha vazia se < 25%
        StrongFill = 0.75,             // Bolha forte se >= 75%
        SecondRelativeToTop = 0.5      // Dupla se 2ª >= 50% da 1ª
    }
);
var omr = OmrServiceFactory.Create(layout, classifier, optionCount: 5);
```

---

## Enums e Status

### QuestionStatus
```csharp
public enum QuestionStatus
{
    Valida,           // Uma alternativa marcada claramente
    EmBranco,         // Nenhuma marcada
    Dupla,            // Duas ou mais marcadas
    RasuraDuvidosa    // Marcação ambígua, requer revisão
}
```

### Resultado de Página
```csharp
public class PageProcessingResult
{
    public bool Aligned { get; set; }              // Alinhada pelos ArUco?
    public int? ExamId { get; set; }               // ID da prova (do QR)
    public int? StudentId { get; set; }            // ID do estudante (do QR)
    public string? QrPayload { get; set; }         // Conteúdo bruto do QR
    public IReadOnlyList<QuestionResult> Questions { get; set; }  // Respostas
    public string? Warnings { get; set; }          // Mensagens de diagnóstico
    public string? DebugImagePath { get; set; }    // Caminho da imagem de debug
}
```

---

## Debugging

As imagens de debug são salvas com anotações:
- ✅ **Verde**: Questão válida (OK)
- ❌ **Vermelho**: Questão dupla (DUP)
- ⚪ **Cinza**: Questão em branco (---)
- 🟠 **Laranja**: Questão com rasura/dúvida (RAS)

Cada círculo representa a região amostrada de uma bolha.

---

## Notas Importantes

1. **Geometria Fixa**: O `SheetLayout` define a geometria canônica (1000×1414 px) que é usada tanto na geração quanto na leitura.

2. **ArUco Markers**: 4 marcadores ArUco nos cantos (ids 0-3) definem o alinhamento da folha.

3. **QR Code**: Localizado no canto superior direito; contém `exam=<id>;student=<id>;v=1`.

4. **Limiares de Classificação**: Ajustáveis via `ClassifierThresholds` - modificar conforme necessário para o scanner específico.

5. **Sem Alterações ao Código Original**: Toda a lógica está em um serviço separado, totalmente independente do resto da aplicação.

---

## Exemplo: Controller com OMR

```csharp
[HttpPost]
public async Task<IActionResult> ProcessarComOmr(Guid loteId)
{
    var lote = await _context.LotesCorrecao.FindAsync(loteId);
    if (lote == null) return NotFound();

    var omrService = OmrServiceFactory.CreateDefault(
        optionCount: lote.Prova.NumeroAlternativas
    );

    var debugDir = Path.Combine(
        _environment.WebRootPath,
        "uploads/debug",
        lote.Id.ToString()
    );

    var resultados = new List<PageProcessingResult>();
    
    foreach (var (_, resultado) in omrService.ProcessPdf(lote.CaminhoArquivo, debugDir))
    {
        resultados.Add(resultado);
    }

    return View(resultados);
}
```

---

## Referência: Estrutura do Projeto

```
SistemaAvaliacao.Web/
├── Services/
│   └── Omr/
│       ├── SheetLayout.cs
│       ├── SheetReadResult.cs
│       ├── QuestionResult.cs (enum + class)
│       ├── AnswerClassifier.cs
│       ├── ClassifierThresholds.cs
│       ├── OmrReader.cs
│       ├── PdfPageLoader.cs
│       ├── CardGenerator.cs
│       ├── StudentCardInfo.cs
│       ├── DebugRenderer.cs
│       ├── OmrProcessingService.cs
│       ├── PageProcessingResult.cs
│       └── OmrServiceFactory.cs
└── ...
```

---

## Integração Passo a Passo

### 1. Adicionar Referência à OmrProcessingService em seu Controller/Service

```csharp
using SistemaAvaliacao.Web.Services.Omr;
```

### 2. Criar a instância do serviço

```csharp
var omrService = OmrServiceFactory.CreateDefault(optionCount: 5);
```

### 3. Processar PDF e capturar resultados

```csharp
var pdfPath = "caminho/para/prova-scan.pdf";
var debugDir = "wwwroot/uploads/debug/lote123";

foreach (var (pageNum, resultado) in omrService.ProcessPdf(pdfPath, debugDir))
{
    // Verificar alinhamento
    if (!resultado.Aligned)
    {
        // Página não alinhada - marcar para revisão
        continue;
    }

    // Processar questões
    foreach (var questao in resultado.Questions)
    {
        // Salvar no banco de dados...
    }
}
```

### 4. Persistir no Banco de Dados

Use a estrutura de entidades do projeto para salvar:
- `FolhaResposta` (identificada pelo QR)
- `LeiturasResposta` (uma por questão)

---

**Criado em:** 2026-09-26
**Baseado em:** `SistemaAvaliacao.CorrecaoWorker/novo/` (código funcionando com 100 questões)

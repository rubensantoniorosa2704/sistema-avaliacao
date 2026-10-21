# Hardening do pipeline OMR

Acompanhamento das correções de 3 riscos identificados na revisão do pipeline de
correção de provas impressas (OMR). A ordem de ataque segue a severidade, mas a
ordem de **entrega** começa pelo teste de ouro — ele é a rede que protege as
outras duas mudanças de regressão.

O pipeline decide **nota de aluno**, então a régua aqui é mais alta: cada correção
que toca o fluxo crítico vem acompanhada de teste, e o comportamento só é dado como
correto quando a suíte roda verde.

## Itens

### 🟡 Item 2 — Teste de ouro do classificador (entregue primeiro, como rede)
- **Risco**: os limiares do `AnswerClassifier` (`EmptyCeiling`, `StrongFill`,
  `SecondRelativeToTop`) são o coração da correção e não tinham teste de regressão.
  Mexer num limiar poderia quebrar a detecção sem ninguém perceber até uma prova
  real sair errada.
- **Correção**: teste que gera um cartão com padrões conhecidos via
  `CardGenerator.GenerateTestCard` e roda o `OmrReader` em memória (sem PDF/PDFium),
  verificando a classificação esperada.
- **Teste de fluxo crítico**: é o próprio item — teste de ouro geração→leitura
  (`OmrPipelineGoldenTests`, 5 casos): alinhamento+QR, Válida (Q1–10), Dupla (Q11–20),
  decisão por área (Q21–30) e EmBranco (Q31–100).
- **Achado registrado**: o `MarkStyle.Scribble` (rabisco) cobre ~56% da bolha, acima
  do `StrongFill` (0.51), então é classificado como **Válida**. O classificador decide
  por **área preenchida**, não por textura/formato — logo não distingue "risco" de
  "preenchimento sólido". Isso é coerente com a regra do produto e NÃO é bug. Mitigado
  na prática pela **revisão humana obrigatória** antes de gravar nota. O teste fixa esse
  comportamento como âncora para uma futura evolução do classificador, se desejada.
- **Status**: ✅ concluído

### 🔴 Item 1 (reforçado) — Identidade da folha por `TokenIdentificacao`
- **Risco (duplo)**: a identidade da folha vinha inteiramente de um inteiro de 32 bits
  derivado do Guid (`GuidToStableInt`), não-secreto:
  1. **Colisão acidental**: dois alunos do mesmo lote com colisão nos 4 bytes usados
     receberiam o mesmo `student` no QR; o worker (`FirstOrDefault` por id) atribuiria
     a folha ao aluno errado — corrupção silenciosa de nota.
  2. **Forja maliciosa**: como o `student` não é segredo, uma folha com o QR de outro
     aluno seria aceita, desviando a nota — e a revisão humana mostraria o nome errado
     com confiança. O campo `FolhaResposta.TokenIdentificacao` (16 bytes aleatórios,
     único) existia para isso, mas o worker nunca o verificava.
- **Correção**: o QR passa a carregar o `TokenIdentificacao` (segredo único por folha);
  o worker casa a folha por esse token, não pelo id derivado. Resolve colisão E forja
  de uma vez. Sem compatibilidade retroativa — **nenhuma prova real foi gerada ainda**.
  Payload do QR migrado para v2: `exam=<id>;token=<hex>;v=2` (o `exam` fica para
  diagnóstico; a identidade é o `token`). `GuidToStableInt` sobra apenas no cabeçalho
  legível por humanos, não mais como chave de identidade.
- **Teste de fluxo crítico**:
  - `QrPayloadTests` (4 casos): round-trip build/parse do token; payload vazio → nulos;
    formato v1 legado (sem token) NÃO produz token (worker rejeita em vez de casar errado);
    parse tolerante a partes malformadas.
  - `OmrPipelineGoldenTests`: o caso de alinhamento+QR agora valida o round-trip REAL do
    token pelo QR (geração → warp → ZXing).
- **Status**: ✅ concluído

### 🟡 Item 3 — Destravar lote preso em `Processando`
- **Risco**: se o worker morre no meio do processamento (reinício do container, OOM),
  o lote fica em `Processando` para sempre — o worker só pega lotes `Recebido`.
- **Correção**: recuperar lotes presos em `Processando` além de um limite de tempo,
  devolvendo-os ao fluxo.
- **Teste de fluxo crítico**: avaliar viabilidade (worker é `BackgroundService`);
  testar a regra de recuperação isoladamente se possível.
- **Status**: ⬜ pendente

## Verificação final
- ⬜ `dotnet test` verde
- ⬜ 3 commits no padrão do repositório
- ⬜ Este documento atualizado com os status

# CP3 — Relatórios de reembolsos em segundo plano

Evolução do ExpenseHub do CP2 para gerar relatórios por período com RabbitMQ e um worker C#.
Especificação comum: issues CP3 do repositório original
([Racass/checkpoint-csharpracass-expensehub](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues?q=is%3Aissue+CP3)).

| Referência | Valor |
| --- | --- |
| Commit entregue no CP2 | `8b75617` (merge do PR #6, `i10-code-quality`) |
| Commit final do CP3 (imagens) | `832f4354e08fd40888a2bf86081743186c43eabb` (merge do PR #4) |
| Imagem web | `ghcr.io/lorransantosdev/expensehub-web:832f4354e08fd40888a2bf86081743186c43eabb` |
| Imagem worker | `ghcr.io/lorransantosdev/expensehub-worker:832f4354e08fd40888a2bf86081743186c43eabb` |
| Acesso às imagens | pacotes públicos no GHCR: `docker compose pull` sem login |

## Arquitetura

```text
Usuário ──HTTP──> Web (ExpenseHub.Api) ──publica {"reportId"}──> RabbitMQ (expensehub.reports)
                     │                                                   │
                     │ registra / consulta                         consome│ (ack manual)
                     ▼                                                   ▼
                 SQLite (/data/expensehub.db) <──── lê / salva ──── Worker (ExpenseHub.Worker)
```

- **Web** autentica, valida, persiste a solicitação, publica e serve consulta e Excel.
- **Worker** (`sources/ExpenseHub.Worker`) consome a fila, carrega período, escopo e solicitante do
  registro persistido, calcula e salva o resultado.
- Banco compartilhado: o mesmo arquivo SQLite num volume Docker (`app-data`) montado nos dois contêineres.
  `Default Timeout=30` faz o SQLite aguardar o lock em vez de falhar quando os dois gravam ao mesmo tempo.
- O worker referencia o projeto web para reaproveitar domínio, `AppDbContext` e mensageria; não há
  duplicação de modelo.

## Contrato HTTP

| Operação | Resposta |
| --- | --- |
| `POST /api/reports` | `202` + `Location: /api/reports/{id}` após persistir **e** receber a confirmação do broker. `503` com o envelope (id persistido + `error.code = PublishNotConfirmed`) quando não há confirmação. |
| `GET /api/reports` | `200` com as solicitações criadas pelo usuário autenticado. |
| `GET /api/reports/{id}` | `200` com o envelope; `404` para inexistente ou alheia. |
| `GET /api/reports/{id}/excel` | `200` com `.xlsx` (`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `attachment; filename=report-{id}.xlsx`); `404` inexistente/alheia; `409` própria não `Completed`. |

Entrada: `{"fromDate": "yyyy-MM-dd", "toDate": "yyyy-MM-dd", "scope": "Mine" | "Global"}`.
`400` para datas ausentes/inválidas, `fromDate > toDate`, escopo diferente de `Mine`/`Global` ou campo extra
(ex.: `ownerId`). O solicitante vem sempre do token (`ClaimTypes.NameIdentifier`).

Envelope: `id`, `scope`, `fromDate`, `toDate`, `status` (`Pending`, `Processing`, `Completed`, `Failed`),
`createdAt`, `startedAt`, `completedAt`, `result`, `error` (instantes em UTC).
`result` traz `generatedAt`, `totalCount`, `totalAmount` e `byStatus` com os **cinco** estados do reembolso,
inclusive zerados. O resultado é salvo em JSON na tabela `ReportRequests`; consulta e Excel leem esse JSON e
nunca recalculam.

### Matriz de acesso

| Perfil | Mine | Global | Listagem, detalhe e Excel |
| --- | --- | --- | --- |
| Employee | Sim | Não (403) | Somente as próprias |
| Auditor | Não (403), salvo se também Employee | Sim | Somente as próprias |
| Employee + Auditor | Sim | Sim | Somente as próprias |
| Admin / Approver / Finance sem os perfis acima | Não (403) | Não (403) | Somente as próprias (nenhuma) |

No escopo `Mine` o worker filtra pelo `RequesterId` gravado pelo servidor (`ReportExpenseQuery.For`), nunca por
dado vindo da mensagem ou do cliente. Acesso anônimo recebe `401` (controller com `[Authorize]`).

## Mensageria

| Item | Configuração |
| --- | --- |
| Fila | `expensehub.reports` (`RabbitMq:QueueName`), exchange padrão, routing key = nome da fila |
| Durabilidade | `durable: true`, `exclusive: false`, `autoDelete: false`, declarada igual na web e no worker |
| Mensagem | `{"reportId": "<guid>"}`, `Persistent = true` (delivery mode 2), `MessageId`/`CorrelationId` = reportId |
| Publicação | canal com *publisher confirms* e rastreamento; `mandatory: true`; timeout `RabbitMq:PublishTimeoutSeconds` (5 s) |
| Consumo | `prefetch 1`, `autoAck: false`; `BasicAck` só depois de `SaveChanges` do resultado (ou da falha definitiva) |
| Armazenamento | volume `rabbitmq-data` em `/var/lib/rabbitmq`, `hostname` fixo (`expensehub-rabbitmq`) para o nó reutilizar os dados |

### Processamento e idempotência (`ReportProcessor`)

1. Carrega a solicitação pelo `reportId`. Inexistente → ack sem efeito (`NotFound`).
2. Já `Completed` ou `Failed` → ack sem gravar nem recalcular (`Duplicate`). A proteção é o **estado salvo no
   banco**, então vale após reiniciar o worker.
3. `Attempts >= 5` → grava `Failed` (`MaxAttemptsExceeded`) e confirma: falha definitiva sem reencaminhamento
   infinito.
4. Grava `Processing` (`startedAt`, `Attempts + 1`). `Pending` e `Processing` são retomáveis: uma tentativa
   interrompida não bloqueia a solicitação.
5. Lê estado e valor atuais dos reembolsos (filtro por `ExpenseDate` inclusivo e escopo), calcula em `decimal`.
6. Grava `Completed` + resultado + `completedAt` num único `SaveChanges` (conclusão consistente) e só então
   envia o ack.

Exceções transitórias (banco indisponível, conflito de concorrência via token `Version`) não geram ack: o
consumidor espera `RetryDelaySeconds` e devolve a mensagem com `BasicNack(requeue: true)`. Mensagem sem
`reportId` válido é rejeitada sem reentrega (`BasicReject(requeue: false)`).

### Falhas e a janela sem Transactional Outbox

- **Banco indisponível antes de registrar**: a exceção vira `500` (ProblemDetails); não há `202` nem id.
- **Broker sem confirmação depois de registrar**: a solicitação fica `Pending` no banco e a resposta é `503` com
  esse id. Nenhuma mensagem foi confirmada, então reiniciar o worker **não** recupera esse pedido; o usuário pode
  fazer um novo `POST` (que gera outro id). Não há republicação automática.
- **Queda entre salvar e publicar** (processo morre depois do `SaveChanges` e antes do confirm): mesma situação,
  `Pending` sem mensagem. É a limitação conhecida de não usar Transactional Outbox.
- **Worker parado** é diferente de **broker indisponível**: com o worker parado a mensagem foi confirmada e fica
  `Ready` na fila; ao voltar, o worker processa o mesmo id. Com o broker fora, não há confirmação e a web
  responde `503`.
- **Worker cai depois de salvar e antes do ack**: a mensagem volta (`Unacked` → `Ready`) e é reentregue; o passo 2
  reconhece a duplicata e confirma sem novo efeito.

## Executar

Pré-requisitos: Docker com Compose. Não é preciso SDK para executar.

```bash
cp .env.example .env    # preencha IMAGE_TAG (SHA publicado) e as senhas
docker compose pull
docker compose up -d
docker compose ps
curl http://localhost:5245/health   # {"status":"ok","version":"<sha>"}
```

- A web aplica as migrations no início (o worker também chama `MigrateAsync`; o EF Core usa trava de migração).
- O Admin inicial é criado com `ADMIN_EMAIL`/`ADMIN_PASSWORD` do `.env`.
- Chaves de Data Protection ficam em `/data/keys` (mesmo volume), então tokens continuam válidos após recriar a web.
- Painel do RabbitMQ: <http://localhost:15672> (usuário/senha do `.env`).
- Dados de exemplo dos experimentos: `scripts/cp3/Invoke-Cp3Seed.ps1` (usa só a API).
- Chamadas: `sources/ExpenseHub.Api/Reports.http` (tokens obtidos por login, nada persistido).

Para rodar sem Docker (desenvolvimento): RabbitMQ local em `localhost:5672`, `dotnet run --project
sources/ExpenseHub.Api` e, para o worker, defina o mesmo banco antes de `dotnet run --project
sources/ExpenseHub.Worker` (ex.: `ConnectionStrings__DefaultConnection="Data Source=../ExpenseHub.Api/expensehub.db;Default Timeout=30"`
e `RabbitMq__UserName`/`RabbitMq__Password`). O worker não tem `appsettings.json` próprio: herda o do projeto
web e, no contêiner, tudo vem de variáveis de ambiente.

### Imagens

O workflow `.github/workflows/images.yml` roda em cada push na `main`: primeiro `dotnet test` (job `test`),
depois, somente se os testes passarem, constrói e publica as duas imagens no GHCR com a tag igual ao SHA completo
do commit (`needs: test`). Não usamos `latest`. A versão em execução aparece em `GET /health` e na primeira
linha de log de cada serviço (`ExpenseHub web/worker iniciado na versão <sha>`).

## Testes

```bash
dotnet test sources/ExpenseHub.slnx
```

Os testes do CP3 ficam em `sources/ExpenseHub.UnitTests/Reports` e usam repositório e publicador em memória
(`Fakes/InMemoryReportRepository`, `Fakes/FakeReportPublisher`): não dependem de banco, rede ou RabbitMQ.
O filtro em memória compila a mesma expressão (`ReportExpenseQuery.ToExpression`) enviada ao EF Core.

| Cenário | Testes |
| --- | --- |
| Dados conhecidos, limites inclusivos, vazio, decimal | `ReportCalculationTests` |
| Período invertido, escopo inválido, perfis negados, 503, banco fora | `ReportServiceTests` |
| Detalhe/Excel alheio (404 inclusive Auditor), listagem própria, 409 | `ReportServiceTests` |
| Mesma mensagem de novo, Processing retomado, falha ao salvar, limite de tentativas | `ReportProcessorIdempotencyTests` |
| Consulta e Excel após alterar reembolso (sem recálculo) | `ReportServiceTests` |
| Planilha válida, células numéricas, cabeçalhos e valores do resultado salvo | `ReportExcelWriterTests`, `ReportsControllerTests` |

## Entrega

- Dossiê E01–E05 com ids, comandos, resultados e logs sanitizados: [`docs/cp3/EXPERIMENTOS.md`](cp3/EXPERIMENTOS.md).
- Scripts reproduzíveis: `scripts/cp3/Invoke-Cp3Seed.ps1` e `scripts/cp3/Invoke-Cp3Experiments.ps1`.
- Pull requests do CP3: #1 (I01/I02), #2 (I03), #3 (I04), #4 (I05) e o PR de documentação/experimentos (I06/I07).
- Qualidade (I07): `dotnet build sources/ExpenseHub.slnx` com 0 warnings; analisadores, `.editorconfig` e o
  workflow `code-quality` do CP2 foram mantidos sem reduzir severidades.

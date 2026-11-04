# CP3 — Dossiê dos experimentos E01–E05

Ambiente executado em 2026-11-04 (UTC) com `docker compose` e as **imagens publicadas** no GHCR para o commit
`832f4354e08fd40888a2bf86081743186c43eabb`:

| Serviço | Imagem | Digest local |
| --- | --- | --- |
| web | `ghcr.io/lorransantosdev/expensehub-web:832f4354e08fd40888a2bf86081743186c43eabb` | `sha256:ce3a922b3d77…` |
| worker | `ghcr.io/lorransantosdev/expensehub-worker:832f4354e08fd40888a2bf86081743186c43eabb` | `sha256:01a527afe3b7…` |

As saídas completas e sanitizadas estão em [`evidencias/`](evidencias). Tokens, senhas e cabeçalhos de
autenticação nunca foram gravados (as requisições aparecem como `Bearer <token omitido>`).

## Como reproduzir

```bash
cp .env.example .env          # IMAGE_TAG=832f4354e08fd40888a2bf86081743186c43eabb e senhas
docker compose pull
docker compose up -d
```

```powershell
# Senhas só em variáveis de ambiente (o script de experimentos também lê o .env)
$env:EXPENSEHUB_ADMIN_PASSWORD = '<ADMIN_PASSWORD do .env>'
$env:EXPENSEHUB_USER_PASSWORD = '<senha para os usuários de teste>'
./scripts/cp3/Invoke-Cp3Seed.ps1            # usuários, roles e reembolsos conhecidos, só pela API
./scripts/cp3/Invoke-Cp3Experiments.ps1     # E01–E05, grava docs/cp3/evidencias
```

Dados conhecidos (criados pela API, passando por envio, aprovação e pagamento):

| Reembolso | Dono | Data | Estado | Valor |
| --- | --- | --- | --- | --- |
| 1 | employee-a | 2026-09-10 | Approved | 100,00 |
| 2 | employee-a | 2026-09-15 | Approved | 50,00 |
| 3 | employee-a | 2026-09-20 | Paid | 25,00 |
| 4 | employee-b | 2026-09-12 | Approved | 200,00 |
| 5 | employee-a | 2026-08-31 | Paid | 500,00 (fora do período) |

Esperado para 2026-09-01..2026-09-30: **Mine (employee-a) = 3 / 175,00** e **Global = 4 / 375,00**.

## Versão em execução — [`00-versao.txt`](evidencias/00-versao.txt)

`docker compose ps` mostra as duas imagens com a tag do commit; `GET /health` responde
`{"status":"ok","version":"832f4354…"}`; o label `org.opencontainers.image.revision` dos contêineres e a primeira
linha de log (`ExpenseHub web/worker iniciado na versão 832f4354…`) confirmam a mesma versão.

## E01 — Fluxo normal — [`E01-fluxo-normal.txt`](evidencias/E01-fluxo-normal.txt)

- reportId: `fcda4901-e324-4c16-bb4c-2bba6e5df193` (employee-a, `Mine`).
- `POST /api/reports` → **202**, `Location: http://localhost:5245/api/reports/fcda4901-…`, envelope `Pending`.
- Acompanhamento: `Pending` → `Completed` em cerca de 1 s.
- Resultado: `totalCount 3`, `totalAmount 175`, Approved 2 / 150, Paid 1 / 25, Draft/Submitted/Rejected com 0.
- `GET …/excel` → **200**, `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`,
  `Content-Disposition: attachment; filename=report-fcda4901-….xlsx`. A planilha foi aberta como pacote Open XML
  e transcrita no arquivo: id, escopo, período, instante de geração e a tabela com os cinco estados e o total, com
  quantidades e valores em **células numéricas** iguais ao resultado salvo.
- Global (auditor) `857f4dd2-26c9-47ee-bc4f-4b5e6d47edac`: **4 / 375**.
- Logs correlacionados pelo id: web `registrado como Pending` → `publicado com confirmação do broker`; worker
  `recebida` → `iniciado (tentativa 1)` → `concluído com 3 reembolsos` → `confirmada (ack) com desfecho Completed`.

## E02 — Worker parado — [`E02-worker-parado.txt`](evidencias/E02-worker-parado.txt)

1. `docker compose stop worker`.
2. `POST` (employee-a, Mine) → **202**, reportId `e348a7f3-f9af-4b80-ad8e-71272eb0316a`.
3. Fila: `durable=True ready=1 unacked=0 consumers=0` — a mensagem confirmada está disponível e ninguém consome.
4. `GET` → `Pending`; `GET …/excel` → **409** (`O relatório está Pending…`).
5. `docker compose up -d worker` → o mesmo id passa por `Pending` → `Processing` → `Completed` (3 / 175).
6. Fila depois: `ready=0 unacked=0 consumers=1`. Logs do worker com o mesmo id: recebida, iniciado, concluído, ack.

`Ready` e `Unacked` são métricas do RabbitMQ, não os estados `Pending`/`Processing` da aplicação; a correlação
foi feita pelo reportId nos logs e no envelope.

## E03 — Duplicata — [`E03-duplicata.txt`](evidencias/E03-duplicata.txt)

1. Antes: `fcda4901-…` `Completed`, `completedAt 2026-11-04T23:25:05.2660434Z`, `generatedAt 2026-11-04T23:25:05.2565719Z`, total 175.
2. `docker compose restart worker` (descarta qualquer estado em memória).
3. Republicação do payload `{"reportId":"fcda4901-…"}` na fila pelo painel de gerenciamento
   (`POST /api/exchanges/%2F/amq.default/publish`, `delivery_mode 2`) → `routed=True`.
4. Worker: `Relatório fcda4901-… já está Completed; duplicata ignorada sem novo efeito` e ack com desfecho
   `Duplicate`. Fila volta a `ready=0 unacked=0`.
5. Depois: envelope **idêntico** (mesmos `completedAt`, `generatedAt` e resultado); a listagem de employee-a
   não ganhou solicitação nova.

A proteção é o estado `Completed` salvo no banco (`ReportProcessor`), por isso vale após o reinício. Reentregar a
mesma mensagem é diferente de um novo `POST`, que sempre cria outro id.

## E04 — Isolamento — [`E04-isolamento.txt`](evidencias/E04-isolamento.txt)

Requisições na ordem do arquivo:

| Cenário | Resposta |
| --- | --- |
| Anônimo lista / solicita | 401 / 401 |
| employee-a pede `Global` | 403 |
| auditor (sem Employee) pede `Mine` | 403 |
| employee-b consulta / baixa Excel do relatório de employee-a | 404 / 404 |
| auditor consulta / baixa Excel do relatório de employee-a | 404 / 404 |
| employee-a consulta o relatório Global do auditor | 404 |
| employee-b lista | 200 com somente as próprias solicitações (lista vazia) |
| auditor consulta o próprio Global | 200 |
| Período invertido / escopo `Everyone` / campo `ownerId` | 400 / 400 / 400 |
| Id inexistente | 404 |

## E05 — Persistência e versão — [`E05-persistencia-versao.txt`](evidencias/E05-persistencia-versao.txt)

1. `docker compose up -d --force-recreate web worker` (sem `down -v`; volumes preservados).
2. `docker compose ps`: contêineres novos (`Up 3 seconds`) com as mesmas imagens publicadas `…:832f4354…`;
   `/health` e os logs de inicialização confirmam a versão.
3. O token emitido antes da recriação continuou válido (chaves de Data Protection em `/data/keys` no volume).
4. `GET /api/reports/fcda4901-…` depois da recriação → resultado **igual** ao anterior; o Excel também foi baixado
   de novo (**200**).

## Diagnóstico

- **Aceitação ≠ conclusão.** O `202` significa que a solicitação foi salva e o broker confirmou a mensagem
  persistente numa fila durável. A conclusão é assíncrona e aparece no envelope (`status`, `completedAt`, `result`).
- **Worker parado ≠ broker indisponível.** Worker parado (E02): a mensagem foi confirmada e espera em `Ready`;
  ao voltar, o mesmo id é processado. Broker fora: não há confirmação, a web responde **503** com o id já
  persistido e `error.code = PublishNotConfirmed` (coberto por `ReportsControllerTests` e `ReportServiceTests`);
  esse pedido fica `Pending` sem mensagem.
- **Duplicata ≠ novo POST.** A reentrega da mesma mensagem é reconhecida pelo estado salvo e não gera efeito (E03);
  um novo `POST` cria outro id.
- **Janela sem Transactional Outbox.** Salvar e publicar são operações separadas. Se o processo cair entre o
  `SaveChanges` e a confirmação, ou se a confirmação se perder, a solicitação fica `Pending` sem mensagem.
  Reiniciar o worker não recupera uma mensagem que nunca foi publicada; não há republicação automática. O usuário
  recebe o id (503) e pode fazer um novo pedido.
- **Ack depois de salvar.** O worker só envia ack depois de gravar `Completed` + resultado (ou `Failed`). Se cair
  antes do ack, a mensagem volta e cai no caso de duplicata. Falha transitória (banco indisponível) volta à fila
  após 5 s; depois de 5 tentativas registradas, a solicitação vira `Failed` (`MaxAttemptsExceeded`), sem
  reencaminhamento infinito.

## Testes unitários (separados dos experimentos)

```bash
dotnet test sources/ExpenseHub.slnx
```

220 testes aprovados localmente; o job `test` do workflow `images` executa a mesma suíte antes de publicar as imagens.

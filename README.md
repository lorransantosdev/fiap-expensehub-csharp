# ExpenseHub — Checkpoints 2 e 3 (C#)

API REST corporativa de reembolsos: funcionários registram despesas, aprovadores analisam, o financeiro registra
o pagamento e auditores consultam todo o histórico.

**CP3:** relatórios de reembolsos por período processados em segundo plano (RabbitMQ + worker C#), com
acompanhamento do processamento e download do resultado em Excel. Contrato, mensageria, falhas, execução com as
imagens publicadas e testes: [docs/CP3.md](docs/CP3.md). Dossiê dos experimentos:
[docs/cp3/EXPERIMENTOS.md](docs/cp3/EXPERIMENTOS.md).

O backlog e os critérios de aceite estão nas issues do repositório original
[Racass/checkpoint-csharpracass-expensehub](https://github.com/Racass/checkpoint-csharpracass-expensehub).

## Stack

- .NET 10 e ASP.NET Core
- Entity Framework Core 10 com SQLite
- ASP.NET Core Identity com bearer token (`/login`, `/register`)
- MSTest para testes unitários
- RabbitMQ.Client 7 (fila durável, publisher confirms, ack manual) e worker .NET (`ExpenseHub.Worker`)
- ClosedXML para a planilha `.xlsx`
- Docker Compose com imagens publicadas no GHCR

## Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Configuração (senha do Admin inicial)

Na primeira execução a API cria as roles (`Admin`, `Employee`, `Approver`, `Finance`, `Auditor`) e **uma** conta Admin.
O e-mail vem de `Seed:Admin:Email` em `appsettings.json` (`admin@expensehub.local`). A senha **nunca** fica no Git: defina
com user-secrets (o `UserSecretsId` já está no `.csproj`):

```shell
dotnet user-secrets set "Seed:Admin:Password" "<senha forte>" --project ./sources/ExpenseHub.Api
```

Também vale a variável de ambiente `Seed__Admin__Password`. A senha precisa seguir a política do Identity (maiúscula,
minúscula, dígito, símbolo e pelo menos 6 caracteres). Sem senha configurada, só as roles são criadas.

## Como executar

```shell
dotnet restore ./sources/ExpenseHub.slnx
dotnet build ./sources/ExpenseHub.slnx
dotnet run --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
```

A API sobe em `http://localhost:5245`. O endpoint `GET /health` confirma que a aplicação iniciou.

## Banco de dados

| Item | Valor |
|---|---|
| Provider | SQLite |
| Pacotes | `Microsoft.EntityFrameworkCore.Sqlite` e `Microsoft.EntityFrameworkCore.Design` (10.0.12) |
| Configuração | `ConnectionStrings:DefaultConnection` em `sources/ExpenseHub.Api/appsettings.json` |
| Valor padrão | `Data Source=expensehub.db`, arquivo criado no diretório de execução da API |
| Migrations | `sources/ExpenseHub.Api/Data/Migrations` |

O banco é criado ou atualizado automaticamente na inicialização da API, que aplica as migrations pendentes.
Para aplicar manualmente:

```shell
dotnet tool restore
dotnet ef database update --project ./sources/ExpenseHub.Api
```

Para criar uma nova migration depois de alterar o modelo:

```shell
dotnet ef migrations add <Nome> --project ./sources/ExpenseHub.Api --output-dir Data/Migrations
```

A connection string pode ser sobrescrita pela variável de ambiente `ConnectionStrings__DefaultConnection`.
Para recriar o banco do zero, pare a API e apague os arquivos `expensehub.db*`. Os arquivos do banco não são
versionados.

## Fluxo de usuários

1. **Admin faz login** com o e-mail do seed e a senha do user-secrets (`POST /login`).
2. **Usuário se registra** com e-mail e senha (`POST /register`). O cadastro nunca aceita roles: enviar `roles` gera 400.
3. **Admin atribui roles** com `PUT /api/admin/users/{id}/roles` (lista de usuários em `GET /api/admin/users`).
4. **O usuário faz novo login.** A troca de roles renova o security stamp, e o token antigo passa a receber 401.

Um usuário pode acumular roles; a leitura é a união delas, mas o dono nunca aprova, reprova ou paga o próprio reembolso.

## Endpoints de reembolso

Todos exigem `Authorization: Bearer <accessToken>`. Admin sem outra role não acessa nenhum deles (403).

| Método e rota | Role | Regra |
|---|---|---|
| `POST /api/expenses` | Employee | cria rascunho; dono, estado e datas vêm do servidor |
| `PUT /api/expenses/{id}` | Employee | só o dono e só em `Draft` |
| `POST /api/expenses/{id}/submit` | Employee | só o dono, `Draft → Submitted` |
| `POST /api/expenses/{id}/approve` | Approver | não dono, `Submitted → Approved` |
| `POST /api/expenses/{id}/reject` | Approver | não dono, `Submitted → Rejected`, `justification` de 10 a 500 caracteres |
| `POST /api/expenses/{id}/pay` | Finance | não dono, `Approved → Paid`, cria o registro de pagamento |
| `GET /api/expenses` | Employee, Approver, Finance, Auditor | Employee vê os próprios, Approver os `Submitted`, Finance os `Approved`/`Paid`, Auditor todos |
| `GET /api/expenses/{id}` | idem | fora do escopo de leitura gera 404 |
| `GET /api/expenses/{id}/history` | idem | mesma visibilidade do detalhe |

Erros seguem ProblemDetails: 400 (validação ou campo desconhecido no JSON), 401 (sem token ou token antigo), 403 (role ou
dono), 404 (fora do escopo), 409 (estado incompatível ou alteração concorrente). Ator e horário de toda ação vêm do servidor.

## Validação pelo `.http`

O arquivo `sources/ExpenseHub.Api/ExpenseHub.Api.http` percorre o fluxo completo: login do Admin, cadastro e roles de
Employee, Approver e Finance, novo login de cada um, criar, editar, enviar, aprovar, reprovar, pagar, histórico e os
casos negativos (400, 401, 403, 404, 409). As senhas vêm de variáveis de ambiente, nunca do arquivo:

```shell
# PowerShell
$env:EXPENSEHUB_ADMIN_PASSWORD = "<senha do Admin definida no user-secrets>"
$env:EXPENSEHUB_USER_PASSWORD = "<senha dos usuários de teste>"
```

Com a API rodando, abra o arquivo no Visual Studio, Rider ou VS Code (extensão REST Client) e execute as requisições em
ordem. Para repetir do zero, apague `expensehub.db*` antes de subir a API.

## Testes

```shell
dotnet test ./sources/ExpenseHub.slnx
```

Os testes unitários não dependem de banco, rede ou serviço externo: usam os fakes de `sources/ExpenseHub.UnitTests/Fakes`.
Cobrem a tabela completa de transições, ownership e a ordem das checagens, limites de validação, decisões, pagamento e
histórico. A suíte foi verificada contra regressões quebrando regras de propósito (registro em
[docs/ARQUITETURA.md](docs/ARQUITETURA.md#testes-organização-e-regressão)).

## Qualidade

```shell
dotnet build ./sources/ExpenseHub.slnx
dotnet format ./sources/ExpenseHub.slnx --verify-no-changes --severity warn
```

O workflow `code-quality` roda a cada push e publica o artefato `code-quality-report` (resumo em Actions → Summary).
Localmente, com PowerShell 7: `pwsh ./scripts/Invoke-CodeQuality.ps1`.

## Documentação

- [CP3 — relatórios em segundo plano](docs/CP3.md)
- [CP3 — dossiê dos experimentos E01–E05](docs/cp3/EXPERIMENTOS.md)
- [Arquitetura e convenções do grupo](docs/ARQUITETURA.md)
- [Enunciado](docs/ENUNCIADO.md)
- [Requisitos e contratos](docs/REQUISITOS.md)
- [Matriz de autorização](docs/MATRIZ-AUTORIZACAO.md)
- [Rubrica](docs/RUBRICA.md)
- [Processo no GitHub](docs/PROCESSO-GITHUB.md)
- [Uso de Inteligência Artificial](docs/USO-DE-IA.md)
- [Regras do pipeline de qualidade](docs/code-quality-rules.md)

## Integrantes

- Lorran dos Santos Sarmento - RM 558982
- Vinícius - RM 555200
- Maria - RM 557478

A base do CP2 (commit `8b75617`) veio de
[fabianoZagDev/csharp-cp2-2sem](https://github.com/fabianoZagDev/csharp-cp2-2sem), onde Lorran participou do
desenvolvimento; o CP3 foi desenvolvido neste repositório.

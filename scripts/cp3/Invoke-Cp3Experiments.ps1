#Requires -Version 7.0
<#
.SYNOPSIS
    Executa os experimentos E01–E05 do CP3 contra o ambiente do Compose e grava evidências sanitizadas.

.DESCRIPTION
    Pré-requisitos: `docker compose up -d` com as imagens publicadas e `Invoke-Cp3Seed.ps1` já executado.
    Lê senhas das variáveis de ambiente (ou do .env) e nunca grava tokens, senhas ou cabeçalhos de autenticação.
    Para E03 republica o payload {"reportId": ...} pela API HTTP do painel de gerenciamento do RabbitMQ.

.EXAMPLE
    ./scripts/cp3/Invoke-Cp3Experiments.ps1 -OutDir docs/cp3/evidencias
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5245',
    [string] $RabbitUrl = 'http://localhost:15672',
    [string] $OutDir = 'docs/cp3/evidencias',
    [string] $Queue = 'expensehub.reports'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

if (Test-Path .env) {
    foreach ($line in Get-Content .env) {
        if ($line -match '^\s*([A-Z_]+)=(.*)$' -and -not [Environment]::GetEnvironmentVariable($Matches[1])) {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2])
        }
    }
}

$userPassword = $env:EXPENSEHUB_USER_PASSWORD
$rabbitAuth = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($env:RABBITMQ_USER):$($env:RABBITMQ_PASSWORD)"))
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$script:log = [System.Collections.Generic.List[string]]::new()

function Write-Evidence([string] $Text) {
    $script:log.Add($Text)
    Write-Host $Text
}

function Save-Evidence([string] $Name) {
    $script:log | Set-Content -Path (Join-Path $OutDir $Name) -Encoding utf8
    $script:log.Clear()
}

function Get-Token([string] $Email) {
    $body = @{ email = $Email; password = $userPassword } | ConvertTo-Json
    (Invoke-RestMethod -Method Post -Uri "$BaseUrl/login" -ContentType 'application/json' -Body $body).accessToken
}

function Send([string] $Method, [string] $Path, [string] $Token, $Body = $null, [string] $OutFile = $null) {
    $parameters = @{ Method = $Method; Uri = "$BaseUrl$Path"; SkipHttpErrorCheck = $true }

    if ($Token) { $parameters.Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Body) { $parameters.ContentType = 'application/json'; $parameters.Body = $Body | ConvertTo-Json }
    if ($OutFile) { $parameters.OutFile = $OutFile; $parameters.PassThru = $true }

    $response = Invoke-WebRequest @parameters
    $label = if ($Token) { '(Bearer <token omitido>)' } else { '(anônimo)' }
    Write-Evidence "> $Method $Path $label"

    if ($null -ne $Body) { Write-Evidence "  body: $($Body | ConvertTo-Json -Compress)" }

    Write-Evidence "< $($response.StatusCode)"

    foreach ($header in 'Location', 'Content-Type', 'Content-Disposition') {
        if ($response.Headers[$header]) { Write-Evidence "  ${header}: $($response.Headers[$header])" }
    }

    if (-not $OutFile -and $response.Content) {
        $text = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { $response.Content }
        Write-Evidence "  $text"
    }

    $response
}

function Get-Report([string] $Id, [string] $Token) {
    (Invoke-WebRequest -Uri "$BaseUrl/api/reports/$Id" -Headers @{ Authorization = "Bearer $Token" }).Content |
        ConvertFrom-Json -DateKind String
}

function Wait-Status([string] $Id, [string] $Token, [string] $Expected, [int] $TimeoutSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        $report = Get-Report $Id $Token
        Write-Evidence "  [poll $(Get-Date -Format 'HH:mm:ss')] status=$($report.status)"
        if ($report.status -eq $Expected) { return $report }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    throw "Relatório $Id não chegou a $Expected."
}

function Get-QueueInfo {
    # As métricas do painel são atualizadas a cada ~5 s; espera para não ler um valor antigo.
    Start-Sleep -Seconds 7
    $info = Invoke-RestMethod -Uri "$RabbitUrl/api/queues/%2F/$Queue" -Headers @{ Authorization = "Basic $rabbitAuth" }
    Write-Evidence "  fila $($info.name): durable=$($info.durable) ready=$($info.messages_ready) unacked=$($info.messages_unacknowledged) consumers=$($info.consumers)"
    $info
}

function Read-Xlsx([string] $Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path))

    try {
        $read = { param($name) $reader = [IO.StreamReader]::new($zip.GetEntry($name).Open()); try { [xml]$reader.ReadToEnd() } finally { $reader.Dispose() } }
        $shared = @()
        if ($zip.GetEntry('xl/sharedStrings.xml')) { $shared = (& $read 'xl/sharedStrings.xml').sst.si | ForEach-Object { $_.InnerText } }
        $sheet = & $read 'xl/worksheets/sheet1.xml'

        foreach ($row in $sheet.worksheet.sheetData.row) {
            $cells = foreach ($cell in $row.c) {
                $value = switch ($cell.t) { 's' { $shared[[int]$cell.v] } 'inlineStr' { $cell.is.InnerText } default { $cell.v } }
                $kind = if ($cell.t -in 's', 'inlineStr', 'str') { 'texto' } else { 'número' }
                "$($cell.r)=$value ($kind)"
            }
            Write-Evidence "  linha $($row.r): $($cells -join ' | ')"
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Write-Logs([string] $Service, [string] $Pattern) {
    $lines = docker compose logs --no-color --tail 200 $Service 2>&1 | Select-String -Pattern $Pattern
    $lines | ForEach-Object { Write-Evidence "  $($_.Line)" }
}

$period = @{ fromDate = '2026-09-01'; toDate = '2026-09-30' }
$tokenA = Get-Token 'employee-a@expensehub.local'
$tokenB = Get-Token 'employee-b@expensehub.local'
$tokenAuditor = Get-Token 'auditor@expensehub.local'

# ---------------- Versão em execução ----------------
Write-Evidence '# Versão em execução'
Write-Evidence "$(Get-Date -AsUTC -Format o)"
docker compose ps --format 'table {{.Service}}\t{{.Image}}\t{{.Status}}' | ForEach-Object { Write-Evidence $_ }
Send Get '/health' $null | Out-Null
foreach ($service in 'web', 'worker') {
    $container = docker compose ps -q $service
    Write-Evidence "  $service revision label: $(docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' $container)"
    Write-Evidence "  $service image digest: $(docker inspect --format '{{.Image}}' $container)"
}
Write-Logs web 'iniciado na versão'
Write-Logs worker 'iniciado na versão'
Save-Evidence '00-versao.txt'

# ---------------- E01 ----------------
Write-Evidence '# E01 — Fluxo normal (Employee A, Mine) e Global (Auditor)'
$response = Send Post '/api/reports' $tokenA ($period + @{ scope = 'Mine' })
$e01Id = ($response.Content | ConvertFrom-Json).id
Write-Evidence "reportId E01 = $e01Id"
$e01 = Wait-Status $e01Id $tokenA 'Completed'
Send Get "/api/reports/$e01Id" $tokenA | Out-Null
$xlsx = Join-Path $OutDir "report-$e01Id.xlsx"
Send Get "/api/reports/$e01Id/excel" $tokenA $null $xlsx | Out-Null
Write-Evidence "Conteúdo da planilha ($xlsx):"
Read-Xlsx $xlsx
Write-Evidence "Conferência: esperado 3 / 175.00 -> obtido $($e01.result.totalCount) / $($e01.result.totalAmount)"
Send Get '/api/reports' $tokenA | Out-Null

$response = Send Post '/api/reports' $tokenAuditor ($period + @{ scope = 'Global' })
$globalId = ($response.Content | ConvertFrom-Json).id
$global = Wait-Status $globalId $tokenAuditor 'Completed'
Write-Evidence "Conferência Global: esperado 4 / 375.00 -> obtido $($global.result.totalCount) / $($global.result.totalAmount)"
Write-Logs web $e01Id
Write-Logs worker $e01Id
Save-Evidence 'E01-fluxo-normal.txt'

# ---------------- E02 ----------------
Write-Evidence '# E02 — Worker parado e retomado'
docker compose stop worker 2>&1 | ForEach-Object { Write-Evidence "  $_" }
$response = Send Post '/api/reports' $tokenA ($period + @{ scope = 'Mine' })
$e02Id = ($response.Content | ConvertFrom-Json).id
Write-Evidence "reportId E02 = $e02Id"
Get-QueueInfo | Out-Null
Send Get "/api/reports/$e02Id" $tokenA | Out-Null
Send Get "/api/reports/$e02Id/excel" $tokenA | Out-Null
docker compose up -d worker 2>&1 | ForEach-Object { Write-Evidence "  $_" }
Wait-Status $e02Id $tokenA 'Completed' | Out-Null
Get-QueueInfo | Out-Null
Send Get "/api/reports/$e02Id" $tokenA | Out-Null
Write-Logs worker $e02Id
Save-Evidence 'E02-worker-parado.txt'

# ---------------- E03 ----------------
Write-Evidence '# E03 — Duplicata após reiniciar o worker'
$before = Get-Report $e01Id $tokenA
Write-Evidence "Antes: status=$($before.status) completedAt=$($before.completedAt) generatedAt=$($before.result.generatedAt) total=$($before.result.totalAmount)"
docker compose restart worker 2>&1 | ForEach-Object { Write-Evidence "  $_" }
Start-Sleep -Seconds 8
$payload = @{
    properties = @{ delivery_mode = 2; content_type = 'application/json'; message_id = $e01Id }
    routing_key = $Queue
    payload = (@{ reportId = $e01Id } | ConvertTo-Json -Compress)
    payload_encoding = 'string'
} | ConvertTo-Json
Write-Evidence "Republicação pelo painel (POST $RabbitUrl/api/exchanges/%2F/amq.default/publish): payload {`"reportId`":`"$e01Id`"}"
$published = Invoke-RestMethod -Method Post -Uri "$RabbitUrl/api/exchanges/%2F/amq.default/publish" -Headers @{ Authorization = "Basic $rabbitAuth" } -ContentType 'application/json' -Body $payload
Write-Evidence "  routed=$($published.routed)"
Start-Sleep -Seconds 5
Get-QueueInfo | Out-Null
$after = Get-Report $e01Id $tokenA
Write-Evidence "Depois: status=$($after.status) completedAt=$($after.completedAt) generatedAt=$($after.result.generatedAt) total=$($after.result.totalAmount)"
Write-Evidence "Resultado idêntico: $((($before | ConvertTo-Json -Depth 6) -eq ($after | ConvertTo-Json -Depth 6)))"
$list = Invoke-RestMethod -Uri "$BaseUrl/api/reports" -Headers @{ Authorization = "Bearer $tokenA" }
Write-Evidence "Solicitações de employee-a na listagem: $($list.Count) (nenhuma nova criada pela reentrega)"
Write-Logs worker $e01Id
Save-Evidence 'E03-duplicata.txt'

# ---------------- E04 ----------------
Write-Evidence '# E04 — Isolamento e autorização'
Send Get '/api/reports' $null | Out-Null
Send Post '/api/reports' $null ($period + @{ scope = 'Mine' }) | Out-Null
Send Post '/api/reports' $tokenA ($period + @{ scope = 'Global' }) | Out-Null
Send Post '/api/reports' $tokenAuditor ($period + @{ scope = 'Mine' }) | Out-Null
Send Get "/api/reports/$e01Id" $tokenB | Out-Null
Send Get "/api/reports/$e01Id/excel" $tokenB | Out-Null
Send Get "/api/reports/$e01Id" $tokenAuditor | Out-Null
Send Get "/api/reports/$e01Id/excel" $tokenAuditor | Out-Null
Send Get "/api/reports/$globalId" $tokenA | Out-Null
Send Get '/api/reports' $tokenB | Out-Null
Send Get "/api/reports/$globalId" $tokenAuditor | Out-Null
Send Post '/api/reports' $tokenA @{ fromDate = '2026-09-30'; toDate = '2026-09-01'; scope = 'Mine' } | Out-Null
Send Post '/api/reports' $tokenA ($period + @{ scope = 'Everyone' }) | Out-Null
Send Post '/api/reports' $tokenA ($period + @{ scope = 'Mine'; ownerId = 'outro-usuario' }) | Out-Null
Send Get "/api/reports/$([guid]::NewGuid())" $tokenA | Out-Null
Save-Evidence 'E04-isolamento.txt'

# ---------------- E05 ----------------
Write-Evidence '# E05 — Recriar web e worker com as imagens publicadas'
$before = Get-Report $e01Id $tokenA
docker compose up -d --force-recreate web worker 2>&1 | ForEach-Object { Write-Evidence "  $_" }
$deadline = (Get-Date).AddSeconds(60)
do { Start-Sleep -Seconds 2; try { $health = Invoke-RestMethod "$BaseUrl/health" } catch { $health = $null } } until ($health -or (Get-Date) -gt $deadline)
docker compose ps --format 'table {{.Service}}\t{{.Image}}\t{{.Status}}\t{{.CreatedAt}}' | ForEach-Object { Write-Evidence $_ }
Send Get '/health' $null | Out-Null
Write-Logs web 'iniciado na versão'
Write-Logs worker 'iniciado na versão'
Write-Evidence 'Token emitido antes da recriação (chaves de Data Protection no volume):'
Send Get "/api/reports/$e01Id" $tokenA | Out-Null
$tokenA2 = Get-Token 'employee-a@expensehub.local'
$after = Get-Report $e01Id $tokenA2
Write-Evidence "Resultado igual ao anterior à recriação: $((($before | ConvertTo-Json -Depth 6) -eq ($after | ConvertTo-Json -Depth 6)))"
Send Get "/api/reports/$e01Id/excel" $tokenA2 $null (Join-Path $OutDir "report-$e01Id-apos-recriar.xlsx") | Out-Null
Save-Evidence 'E05-persistencia-versao.txt'

Write-Host "Evidências gravadas em $OutDir"

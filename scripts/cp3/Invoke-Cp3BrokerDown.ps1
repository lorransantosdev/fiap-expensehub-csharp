#Requires -Version 7.0
<#
.SYNOPSIS
    Experimento complementar: broker indisponível. A web deve responder 503 com o id persistido e sem 202 falso.

.DESCRIPTION
    Para somente o RabbitMQ, solicita um relatório, consulta o id devolvido, religa o broker e mostra que um novo
    POST volta a ser aceito e concluído, enquanto o pedido sem confirmação continua Pending (sem mensagem na fila).
    Não grava tokens nem senhas. Pré-requisitos: ambiente do Compose e Invoke-Cp3Seed.ps1 já executados.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5245',
    [string] $OutFile = 'docs/cp3/evidencias/E06-broker-indisponivel.txt'
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

$out = [System.Collections.Generic.List[string]]::new()

function Write-Evidence([string] $Text) {
    $out.Add($Text)
    Write-Host $Text
}

$loginBody = @{ email = 'employee-a@expensehub.local'; password = $env:EXPENSEHUB_USER_PASSWORD } | ConvertTo-Json
$token = (Invoke-RestMethod -Method Post -Uri "$BaseUrl/login" -ContentType 'application/json' -Body $loginBody).accessToken
$headers = @{ Authorization = "Bearer $token" }
$body = @{ fromDate = '2026-09-01'; toDate = '2026-09-30'; scope = 'Mine' } | ConvertTo-Json -Compress

Write-Evidence '# E06 (complementar) — Broker indisponível: publicação sem confirmação'
Write-Evidence (Get-Date -AsUTC -Format o)
docker compose stop rabbitmq 2>&1 | ForEach-Object { Write-Evidence "  $_" }
Start-Sleep -Seconds 3

$response = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/reports" -Headers $headers -ContentType 'application/json' -Body $body -SkipHttpErrorCheck
Write-Evidence "> POST /api/reports (Bearer <token omitido>) body: $body"
Write-Evidence "< $($response.StatusCode)"
Write-Evidence "  $($response.Content)"
$failedId = ($response.Content | ConvertFrom-Json).id

$detail = Invoke-WebRequest -Uri "$BaseUrl/api/reports/$failedId" -Headers $headers -SkipHttpErrorCheck
Write-Evidence "> GET /api/reports/$failedId (Bearer <token omitido>)"
Write-Evidence "< $($detail.StatusCode)"
Write-Evidence "  $($detail.Content)"
docker compose logs --no-color --tail 60 web 2>&1 | Select-String -SimpleMatch $failedId | ForEach-Object { Write-Evidence "  $($_.Line)" }

docker compose start rabbitmq 2>&1 | ForEach-Object { Write-Evidence "  $_" }
$deadline = (Get-Date).AddSeconds(120)
do {
    Start-Sleep -Seconds 3
    $health = docker inspect --format '{{.State.Health.Status}}' (docker compose ps -q rabbitmq)
} until ($health -eq 'healthy' -or (Get-Date) -gt $deadline)
Write-Evidence "  rabbitmq: $health"
Start-Sleep -Seconds 10

$retry = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/reports" -Headers $headers -ContentType 'application/json' -Body $body -SkipHttpErrorCheck
$newId = ($retry.Content | ConvertFrom-Json).id
Write-Evidence "Broker de volta: novo POST -> $($retry.StatusCode), id $newId (novo id; o pedido anterior não é republicado)"

$deadline = (Get-Date).AddSeconds(90)
do {
    Start-Sleep -Seconds 2
    $newStatus = (Invoke-RestMethod -Uri "$BaseUrl/api/reports/$newId" -Headers $headers).status
} until ($newStatus -eq 'Completed' -or (Get-Date) -gt $deadline)
Write-Evidence "  status do novo pedido: $newStatus"

$oldStatus = (Invoke-RestMethod -Uri "$BaseUrl/api/reports/$failedId" -Headers $headers).status
Write-Evidence "  status do pedido sem confirmação ($failedId): $oldStatus (sem mensagem na fila, o worker não tem o que consumir)"
docker compose logs --no-color --tail 120 worker 2>&1 |
    Select-String -Pattern 'indisponível|consumindo|Connection|recovery' , $newId |
    ForEach-Object { Write-Evidence "  $($_.Line)" }

$out | Set-Content -Path $OutFile -Encoding utf8

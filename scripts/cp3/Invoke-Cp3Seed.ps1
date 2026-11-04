#Requires -Version 7.0
<#
.SYNOPSIS
    Prepara os dados conhecidos dos experimentos do CP3 pela própria API (sem tocar no banco).

.DESCRIPTION
    Cadastra employee-a, employee-b, auditor, approver e finance, atribui as roles pelo Admin e cria
    os reembolsos do exemplo do enunciado:
      employee-a: Approved 100,00 e 50,00, Paid 25,00 (setembro) e Paid 500,00 (31/08, fora do período)
      employee-b: Approved 200,00 (setembro)
    Resultado esperado para 2026-09-01..2026-09-30: Mine (employee-a) = 3 / 175,00; Global = 4 / 375,00.
    As senhas vêm de variáveis de ambiente e nunca são impressas.

.EXAMPLE
    $env:EXPENSEHUB_ADMIN_PASSWORD = '...'; $env:EXPENSEHUB_USER_PASSWORD = '...'
    ./scripts/cp3/Invoke-Cp3Seed.ps1 -BaseUrl http://localhost:5245
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5245',
    [string] $AdminEmail = 'admin@expensehub.local'
)

$ErrorActionPreference = 'Stop'

$adminPassword = $env:EXPENSEHUB_ADMIN_PASSWORD
$userPassword = $env:EXPENSEHUB_USER_PASSWORD

if ([string]::IsNullOrWhiteSpace($adminPassword) -or [string]::IsNullOrWhiteSpace($userPassword)) {
    throw 'Defina EXPENSEHUB_ADMIN_PASSWORD e EXPENSEHUB_USER_PASSWORD antes de executar.'
}

function Get-Token([string] $Email, [string] $Password) {
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    (Invoke-RestMethod -Method Post -Uri "$BaseUrl/login" -ContentType 'application/json' -Body $body).accessToken
}

function Invoke-Api([string] $Method, [string] $Path, [string] $Token, $Body = $null) {
    $parameters = @{
        Method = $Method
        Uri = "$BaseUrl$Path"
        Headers = @{ Authorization = "Bearer $Token" }
        ContentType = 'application/json'
    }

    if ($null -ne $Body) {
        $parameters.Body = $Body | ConvertTo-Json -Depth 5
    }

    Invoke-RestMethod @parameters
}

$users = [ordered]@{
    'employee-a@expensehub.local' = @('Employee')
    'employee-b@expensehub.local' = @('Employee')
    'auditor@expensehub.local' = @('Auditor')
    'approver@expensehub.local' = @('Approver')
    'finance@expensehub.local' = @('Finance')
}

$adminToken = Get-Token $AdminEmail $adminPassword

foreach ($email in $users.Keys) {
    try {
        $body = @{ email = $email; password = $userPassword } | ConvertTo-Json
        Invoke-RestMethod -Method Post -Uri "$BaseUrl/register" -ContentType 'application/json' -Body $body | Out-Null
        Write-Host "Cadastrado: $email"
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 409) { throw }
        Write-Host "Já existia: $email"
    }
}

$allUsers = Invoke-Api Get '/api/admin/users' $adminToken

foreach ($email in $users.Keys) {
    $user = $allUsers | Where-Object { $_.email -eq $email }
    Invoke-Api Put "/api/admin/users/$($user.id)/roles" $adminToken @{ roles = $users[$email] } | Out-Null
    Write-Host "Roles de $email -> $($users[$email] -join ', ')"
}

$tokenA = Get-Token 'employee-a@expensehub.local' $userPassword
$tokenB = Get-Token 'employee-b@expensehub.local' $userPassword
$tokenApprover = Get-Token 'approver@expensehub.local' $userPassword
$tokenFinance = Get-Token 'finance@expensehub.local' $userPassword

function New-Expense([string] $Token, [decimal] $Amount, [string] $Date, [string] $Target) {
    $expense = Invoke-Api Post '/api/expenses' $Token @{
        description = "Despesa CP3 $Target $Amount"
        amount = $Amount
        expenseDate = $Date
        categoryId = 1
    }

    Invoke-Api Post "/api/expenses/$($expense.id)/submit" $Token | Out-Null
    Invoke-Api Post "/api/expenses/$($expense.id)/approve" $tokenApprover | Out-Null

    if ($Target -eq 'Paid') {
        Invoke-Api Post "/api/expenses/$($expense.id)/pay" $tokenFinance | Out-Null
    }

    [pscustomobject]@{ Id = $expense.id; Amount = $Amount; Date = $Date; Status = $Target }
}

$created = @(
    New-Expense $tokenA 100.00 '2026-09-10' 'Approved'
    New-Expense $tokenA 50.00 '2026-09-15' 'Approved'
    New-Expense $tokenA 25.00 '2026-09-20' 'Paid'
    New-Expense $tokenB 200.00 '2026-09-12' 'Approved'
    New-Expense $tokenA 500.00 '2026-08-31' 'Paid'
)

$created | Format-Table -AutoSize
Write-Host 'Esperado (2026-09-01..2026-09-30): Mine(employee-a) = 3 / 175,00; Global = 4 / 375,00.'

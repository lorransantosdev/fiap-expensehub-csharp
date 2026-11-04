using System;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpenseHub.UnitTests.Fakes;

/// <summary>
/// Dados conhecidos dos testes de relatório (exemplo do enunciado do CP3).
/// </summary>
public static class ReportFixtures
{
    /// <summary>
    /// Funcionário que solicita o relatório.
    /// </summary>
    public const string RequesterId = "employee-a";

    /// <summary>
    /// Outro funcionário.
    /// </summary>
    public const string OtherEmployeeId = "employee-b";

    /// <summary>
    /// Primeiro dia do período de exemplo.
    /// </summary>
    public static readonly DateOnly From = new(2026, 9, 1);

    /// <summary>
    /// Último dia do período de exemplo.
    /// </summary>
    public static readonly DateOnly To = new(2026, 9, 30);

    /// <summary>
    /// Instante fixo dos testes.
    /// </summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Cria um reembolso com valor, data e estado definidos, passando pelas transições de domínio.
    /// </summary>
    /// <param name="ownerId">Proprietário.</param>
    /// <param name="status">Estado desejado.</param>
    /// <param name="amount">Valor.</param>
    /// <param name="expenseDate">Data da despesa.</param>
    /// <returns>O reembolso.</returns>
    public static Expense Expense(string ownerId, ExpenseStatus status, decimal amount, DateOnly expenseDate)
    {
        DateTime now = Now.UtcDateTime;
        Expense expense = ExpenseHub.Api.Domain.Expense.CreateDraft(ownerId, "Despesa de teste do relatório", amount, expenseDate, 1, now);

        if (status == ExpenseStatus.Draft)
        {
            return expense;
        }

        expense.Submit(ownerId, now);

        if (status is ExpenseStatus.Approved or ExpenseStatus.Paid)
        {
            expense.Approve("approver", now);
        }

        if (status == ExpenseStatus.Rejected)
        {
            expense.Reject("approver", "Falta o comprovante fiscal.", now);
        }

        if (status == ExpenseStatus.Paid)
        {
            expense.MarkAsPaid("finance", now);
        }

        return expense;
    }

    /// <summary>
    /// Preenche o repositório com a tabela de exemplo do enunciado.
    /// </summary>
    /// <param name="repository">Repositório em memória.</param>
    public static void SeedSpecExample(InMemoryReportRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        repository.Expenses.Add(Expense(RequesterId, ExpenseStatus.Approved, 100.00m, new DateOnly(2026, 9, 10)));
        repository.Expenses.Add(Expense(RequesterId, ExpenseStatus.Approved, 50.00m, new DateOnly(2026, 9, 15)));
        repository.Expenses.Add(Expense(RequesterId, ExpenseStatus.Paid, 25.00m, new DateOnly(2026, 9, 20)));
        repository.Expenses.Add(Expense(OtherEmployeeId, ExpenseStatus.Approved, 200.00m, new DateOnly(2026, 9, 12)));
        repository.Expenses.Add(Expense(RequesterId, ExpenseStatus.Paid, 500.00m, new DateOnly(2026, 8, 31)));
    }

    /// <summary>
    /// Cria uma solicitação pendente já persistida.
    /// </summary>
    /// <param name="repository">Repositório em memória.</param>
    /// <param name="scope">Escopo.</param>
    /// <param name="requesterId">Solicitante.</param>
    /// <returns>A solicitação.</returns>
    public static ReportRequest SeedReport(
        InMemoryReportRepository repository,
        ReportScope scope,
        string requesterId = RequesterId)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var report = ReportRequest.Create(requesterId, scope, From, To, Now.UtcDateTime);
        repository.Seed(report);

        return report;
    }

    /// <summary>
    /// Cria o processador do worker com fonte de tempo fixa.
    /// </summary>
    /// <param name="repository">Repositório em memória.</param>
    /// <returns>O processador.</returns>
    public static ReportProcessor Processor(InMemoryReportRepository repository)
    {
        return new ReportProcessor(repository, new FixedTimeProvider(Now), NullLogger<ReportProcessor>.Instance);
    }

    /// <summary>
    /// Cria o serviço web com fonte de tempo fixa.
    /// </summary>
    /// <param name="repository">Repositório em memória.</param>
    /// <param name="publisher">Publicador falso.</param>
    /// <returns>O serviço.</returns>
    public static ReportService Service(InMemoryReportRepository repository, FakeReportPublisher publisher)
    {
        return new ReportService(repository, publisher, new FixedTimeProvider(Now), NullLogger<ReportService>.Instance);
    }

    /// <summary>
    /// Cria o usuário autenticado.
    /// </summary>
    /// <param name="roles">Roles separadas por vírgula.</param>
    /// <param name="id">Identificador.</param>
    /// <returns>O usuário.</returns>
    public static CurrentUser User(string roles, string id = RequesterId)
    {
        return new CurrentUser(id, roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

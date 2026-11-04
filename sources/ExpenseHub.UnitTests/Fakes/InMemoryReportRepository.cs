using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;

namespace ExpenseHub.UnitTests.Fakes;

/// <summary>
/// Repositório de relatórios em memória. O filtro de reembolsos usa a mesma expressão enviada ao banco.
/// </summary>
public sealed class InMemoryReportRepository : IReportRepository
{
    private readonly Dictionary<Guid, ReportRequest> _reports = [];
    private readonly List<ReportRequest> _pending = [];
    private int _saveAttempts;

    /// <summary>
    /// Obtém os reembolsos disponíveis para o cálculo.
    /// </summary>
    public List<Expense> Expenses { get; } = [];

    /// <summary>
    /// Obtém quantas gravações aconteceram com sucesso.
    /// </summary>
    public int SaveChangesCalls { get; private set; }

    /// <summary>
    /// Obtém quantas vezes os reembolsos foram lidos para cálculo.
    /// </summary>
    public int ExpenseQueries { get; private set; }

    /// <summary>
    /// Obtém o último filtro recebido.
    /// </summary>
    public ReportExpenseQuery? LastQuery { get; private set; }

    /// <summary>
    /// Obtém ou define o número (a partir de 1) da tentativa de gravação que deve falhar, simulando banco indisponível.
    /// </summary>
    public int? FailAtSaveAttempt { get; set; }

    /// <summary>
    /// Obtém as solicitações persistidas.
    /// </summary>
    public IReadOnlyCollection<ReportRequest> Reports => _reports.Values;

    /// <summary>
    /// Insere uma solicitação como já persistida.
    /// </summary>
    /// <param name="report">Solicitação.</param>
    public void Seed(ReportRequest report)
    {
        _reports[report.Id] = report;
    }

    /// <inheritdoc />
    public void Add(ReportRequest report)
    {
        _pending.Add(report);
    }

    /// <inheritdoc />
    public Task<ReportRequest?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return Task.FromResult(_reports.GetValueOrDefault(id));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportRequest>> ListByRequesterAsync(string requesterId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportRequest> reports = _reports.Values
            .Where(report => report.RequesterId == requesterId)
            .OrderByDescending(report => report.CreatedAtUtc)
            .ToList();

        return Task.FromResult(reports);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportExpenseRow>> ListExpensesAsync(ReportExpenseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        ExpenseQueries++;
        LastQuery = query;
        Func<Expense, bool> filter = query.ToExpression().Compile();

        IReadOnlyList<ReportExpenseRow> rows = Expenses
            .Where(filter)
            .Select(expense => new ReportExpenseRow(expense.Status, expense.Amount))
            .ToList();

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        _saveAttempts++;

        if (_saveAttempts == FailAtSaveAttempt)
        {
            throw new InvalidOperationException("Banco indisponível (simulado).");
        }

        foreach (ReportRequest report in _pending)
        {
            _reports[report.Id] = report;
        }

        _pending.Clear();
        SaveChangesCalls++;

        return Task.CompletedTask;
    }
}

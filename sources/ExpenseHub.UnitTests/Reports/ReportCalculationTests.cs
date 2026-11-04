using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>
/// Cálculo por estado, totais, conjunto vazio e limites inclusivos do período, executados pelo processador do worker.
/// </summary>
[TestClass]
public sealed class ReportCalculationTests
{
    /// <summary>
    /// Exemplo do enunciado no escopo Mine: três reembolsos, R$ 175,00 (dois Approved = 150, um Paid = 25).
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_SpecExampleMine_SumsOnlyRequesterInsidePeriod()
    {
        var repository = new InMemoryReportRepository();
        ReportFixtures.SeedSpecExample(repository);
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);

        ReportProcessingOutcome outcome =
            await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);

        ReportResult result = report.GetResult()!;
        Assert.AreEqual(ReportProcessingOutcome.Completed, outcome);
        Assert.AreEqual(ReportStatus.Completed, report.Status);
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(175.00m, result.TotalAmount);
        AssertStatus(result, ExpenseStatus.Approved, 2, 150.00m);
        AssertStatus(result, ExpenseStatus.Paid, 1, 25.00m);
        AssertStatus(result, ExpenseStatus.Draft, 0, 0m);
        AssertStatus(result, ExpenseStatus.Submitted, 0, 0m);
        AssertStatus(result, ExpenseStatus.Rejected, 0, 0m);
    }

    /// <summary>
    /// Exemplo do enunciado no escopo Global: quatro reembolsos, R$ 375,00, mantendo o registro fora do período excluído.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_SpecExampleGlobal_SumsEveryoneInsidePeriod()
    {
        var repository = new InMemoryReportRepository();
        ReportFixtures.SeedSpecExample(repository);
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Global, "auditor");

        await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);

        ReportResult result = report.GetResult()!;
        Assert.AreEqual(4, result.TotalCount);
        Assert.AreEqual(375.00m, result.TotalAmount);
        AssertStatus(result, ExpenseStatus.Approved, 3, 350.00m);
        AssertStatus(result, ExpenseStatus.Paid, 1, 25.00m);
    }

    /// <summary>
    /// Mine filtra pelo solicitante registrado no servidor; Global não filtra proprietário.
    /// </summary>
    /// <param name="scope">Escopo da solicitação.</param>
    /// <param name="expectedOwner">Filtro de proprietário esperado.</param>
    [TestMethod]
    [DataRow(ReportScope.Mine, ReportFixtures.RequesterId)]
    [DataRow(ReportScope.Global, null)]
    public void ReportExpenseQuery_For_UsesPersistedRequesterOnlyForMine(ReportScope scope, string? expectedOwner)
    {
        var report = ReportRequest.Create(ReportFixtures.RequesterId, scope, ReportFixtures.From, ReportFixtures.To, DateTime.UtcNow);

        ReportExpenseQuery query = ReportExpenseQuery.For(report);

        Assert.AreEqual(expectedOwner, query.OwnerId);
        Assert.AreEqual(ReportFixtures.From, query.FromDate);
        Assert.AreEqual(ReportFixtures.To, query.ToDate);
    }

    /// <summary>
    /// Os dias inicial e final entram; o dia anterior e o posterior não.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_PeriodBoundaries_AreInclusive()
    {
        var repository = new InMemoryReportRepository();
        string owner = ReportFixtures.RequesterId;
        repository.Expenses.Add(ReportFixtures.Expense(owner, ExpenseStatus.Submitted, 1.00m, new DateOnly(2026, 8, 31)));
        repository.Expenses.Add(ReportFixtures.Expense(owner, ExpenseStatus.Submitted, 10.00m, new DateOnly(2026, 9, 1)));
        repository.Expenses.Add(ReportFixtures.Expense(owner, ExpenseStatus.Draft, 20.00m, new DateOnly(2026, 9, 30)));
        repository.Expenses.Add(ReportFixtures.Expense(owner, ExpenseStatus.Draft, 2.00m, new DateOnly(2026, 10, 1)));
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);

        await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);

        ReportResult result = report.GetResult()!;
        Assert.AreEqual(2, result.TotalCount);
        Assert.AreEqual(30.00m, result.TotalAmount);
        AssertStatus(result, ExpenseStatus.Submitted, 1, 10.00m);
        AssertStatus(result, ExpenseStatus.Draft, 1, 20.00m);
    }

    /// <summary>
    /// Sem reembolsos elegíveis: totais zero e os cinco estados presentes com zero.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_NoEligibleExpense_ReturnsFiveZeroedStatuses()
    {
        var repository = new InMemoryReportRepository();
        repository.Expenses.Add(ReportFixtures.Expense(ReportFixtures.OtherEmployeeId, ExpenseStatus.Paid, 80m, new DateOnly(2026, 9, 5)));
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);

        await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);

        ReportResult result = report.GetResult()!;
        Assert.AreEqual(ReportStatus.Completed, report.Status);
        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(0m, result.TotalAmount);
        CollectionAssert.AreEquivalent(Enum.GetValues<ExpenseStatus>(), result.ByStatus.Select(item => item.Status).ToArray());
        Assert.IsTrue(result.ByStatus.All(item => item.Count == 0 && item.Amount == 0m));
    }

    /// <summary>
    /// Valores decimais não sofrem erro de ponto flutuante (0,10 + 0,20 = 0,30).
    /// </summary>
    [TestMethod]
    public void Calculate_DecimalValues_HaveNoFloatingPointError()
    {
        ReportExpenseRow[] rows =
        [
            new(ExpenseStatus.Approved, 0.10m),
            new(ExpenseStatus.Approved, 0.20m),
        ];

        ReportResult result = ReportCalculator.Calculate(rows, ReportFixtures.Now.UtcDateTime);

        Assert.AreEqual(0.30m, result.TotalAmount);
        Assert.AreEqual(ReportFixtures.Now.UtcDateTime, result.GeneratedAt);
    }

    private static void AssertStatus(ReportResult result, ExpenseStatus status, int count, decimal amount)
    {
        ReportStatusSummary summary = result.ByStatus.Single(item => item.Status == status);

        Assert.AreEqual(count, summary.Count, $"Quantidade de {status}");
        Assert.AreEqual(amount, summary.Amount, $"Valor de {status}");
    }
}

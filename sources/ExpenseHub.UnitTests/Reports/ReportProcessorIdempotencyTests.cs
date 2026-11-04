using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>
/// Reentrega da mesma mensagem, retomada de Processing, falha de persistência e falha definitiva no worker.
/// </summary>
[TestClass]
public sealed class ReportProcessorIdempotencyTests
{
    /// <summary>
    /// A mesma mensagem processada de novo (inclusive por um processador novo, como após reiniciar o worker)
    /// não recalcula, não grava e mantém o resultado e o generatedAt.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_SameMessageAfterCompleted_HasNoNewEffect()
    {
        var repository = new InMemoryReportRepository();
        ReportFixtures.SeedSpecExample(repository);
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);
        string? savedJson = report.ResultJson;
        DateTime? completedAt = report.CompletedAtUtc;
        int saves = repository.SaveChangesCalls;
        int queries = repository.ExpenseQueries;

        repository.Expenses.Add(ReportFixtures.Expense(ReportFixtures.RequesterId, ExpenseStatus.Paid, 999m, new DateOnly(2026, 9, 9)));
        ReportProcessingOutcome outcome =
            await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);

        Assert.AreEqual(ReportProcessingOutcome.Duplicate, outcome);
        Assert.AreEqual(savedJson, report.ResultJson);
        Assert.AreEqual(completedAt, report.CompletedAtUtc);
        Assert.AreEqual(saves, repository.SaveChangesCalls);
        Assert.AreEqual(queries, repository.ExpenseQueries);
        Assert.AreEqual(1, report.Attempts);
    }

    /// <summary>
    /// Uma solicitação Failed também é final: a reentrega não a reabre.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_SameMessageAfterFailed_HasNoNewEffect()
    {
        var repository = new InMemoryReportRepository();
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        report.Fail("Teste", "Falha definitiva.", ReportFixtures.Now.UtcDateTime);

        ReportProcessingOutcome outcome =
            await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);

        Assert.AreEqual(ReportProcessingOutcome.Duplicate, outcome);
        Assert.AreEqual(ReportStatus.Failed, report.Status);
        Assert.IsNull(report.ResultJson);
        Assert.AreEqual(0, repository.SaveChangesCalls);
    }

    /// <summary>
    /// Processing não é descartado: uma tentativa anterior interrompida é retomada e concluída.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_ReportLeftInProcessing_IsResumed()
    {
        var repository = new InMemoryReportRepository();
        ReportFixtures.SeedSpecExample(repository);
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        report.StartProcessing(ReportFixtures.Now.UtcDateTime);

        ReportProcessingOutcome outcome =
            await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);

        Assert.AreEqual(ReportProcessingOutcome.Completed, outcome);
        Assert.AreEqual(ReportStatus.Completed, report.Status);
        Assert.AreEqual(2, report.Attempts);
    }

    /// <summary>
    /// Falha ao salvar o resultado propaga a exceção: o consumidor não envia ack e a mensagem volta à fila.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_SaveOfResultFails_PropagatesWithoutOutcome()
    {
        var repository = new InMemoryReportRepository();
        ReportFixtures.SeedSpecExample(repository);
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        repository.FailAtSaveAttempt = 2;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None));

        Assert.AreEqual(1, repository.SaveChangesCalls, "Somente o início (Processing) foi gravado.");
    }

    /// <summary>
    /// Banco indisponível já no início: nada é gravado e a exceção chega ao consumidor.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_DatabaseUnavailableAtStart_PropagatesWithoutCalculating()
    {
        var repository = new InMemoryReportRepository();
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        repository.FailAtSaveAttempt = 1;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None));

        Assert.AreEqual(0, repository.SaveChangesCalls);
        Assert.AreEqual(0, repository.ExpenseQueries);
    }

    /// <summary>
    /// Depois do limite de tentativas a solicitação vira Failed com código estável, encerrando o reencaminhamento.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_MaxAttemptsReached_RecordsDefinitiveFailure()
    {
        var repository = new InMemoryReportRepository();
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);

        for (int attempt = 0; attempt < ReportRequest.MaxAttempts; attempt++)
        {
            report.StartProcessing(ReportFixtures.Now.UtcDateTime);
        }

        ReportProcessingOutcome outcome =
            await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);

        Assert.AreEqual(ReportProcessingOutcome.Failed, outcome);
        Assert.AreEqual(ReportStatus.Failed, report.Status);
        Assert.AreEqual("MaxAttemptsExceeded", report.ErrorCode);
        Assert.IsNotNull(report.CompletedAtUtc);
        Assert.IsNull(report.GetResult());
        Assert.AreEqual(0, repository.ExpenseQueries);
    }

    /// <summary>
    /// Mensagem com id inexistente não cria nada.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ProcessAsync_UnknownReport_ReturnsNotFoundWithoutEffect()
    {
        var repository = new InMemoryReportRepository();

        ReportProcessingOutcome outcome =
            await ReportFixtures.Processor(repository).ProcessAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.AreEqual(ReportProcessingOutcome.NotFound, outcome);
        Assert.AreEqual(0, repository.SaveChangesCalls);
    }

    /// <summary>
    /// Uma solicitação finalizada não aceita novo resultado nem nova falha.
    /// </summary>
    [TestMethod]
    public void ReportRequest_Finished_RejectsNewTransitions()
    {
        var report = ReportRequest.Create("u", ReportScope.Mine, ReportFixtures.From, ReportFixtures.To, ReportFixtures.Now.UtcDateTime);
        report.Complete(ReportCalculator.Calculate([], ReportFixtures.Now.UtcDateTime), ReportFixtures.Now.UtcDateTime);

        Assert.ThrowsExactly<ExpenseHub.Api.Errors.StateConflictException>(
            () => report.Complete(ReportCalculator.Calculate([], DateTime.UtcNow), DateTime.UtcNow));
        Assert.ThrowsExactly<ExpenseHub.Api.Errors.StateConflictException>(
            () => report.Fail("X", "Y", DateTime.UtcNow));
        Assert.ThrowsExactly<ExpenseHub.Api.Errors.StateConflictException>(
            () => report.StartProcessing(DateTime.UtcNow));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Errors;
using ExpenseHub.Api.Reports;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>
/// Solicitação, matriz de perfis, isolamento por solicitante, publicação e consulta do resultado salvo.
/// </summary>
[TestClass]
public sealed class ReportServiceTests
{
    /// <summary>
    /// Data inicial posterior à final: 400 sem persistir e sem publicar.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task RequestAsync_FromAfterTo_RejectsWithoutPersistingOrPublishing()
    {
        (ReportService service, InMemoryReportRepository repository, FakeReportPublisher publisher) = Arrange();

        await Assert.ThrowsExactlyAsync<DomainValidationException>(() => service.RequestAsync(
            ReportFixtures.User(Roles.Employee),
            NewRequest("Mine", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1)),
            CancellationToken.None));

        Assert.IsEmpty(repository.Reports);
        Assert.IsEmpty(publisher.Published);
    }

    /// <summary>
    /// Escopo fora de Mine/Global (inclusive número ou caixa diferente) e datas ausentes: 400 sem efeito.
    /// </summary>
    /// <param name="scope">Escopo enviado.</param>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    [DataRow("All")]
    [DataRow("mine")]
    [DataRow("0")]
    [DataRow("")]
    [DataRow(null)]
    public async Task RequestAsync_InvalidScope_RejectsWithoutEffect(string? scope)
    {
        (ReportService service, InMemoryReportRepository repository, FakeReportPublisher publisher) = Arrange();

        await Assert.ThrowsExactlyAsync<DomainValidationException>(() => service.RequestAsync(
            ReportFixtures.User(Roles.Employee + "," + Roles.Auditor),
            NewRequest(scope),
            CancellationToken.None));

        Assert.IsEmpty(repository.Reports);
        Assert.IsEmpty(publisher.Published);
    }

    /// <summary>
    /// Perfis sem permissão para o escopo recebem 403 e nada é criado nem publicado.
    /// </summary>
    /// <param name="roles">Roles do usuário.</param>
    /// <param name="scope">Escopo pedido.</param>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    [DataRow(Roles.Employee, "Global")]
    [DataRow(Roles.Auditor, "Mine")]
    [DataRow(Roles.Admin, "Mine")]
    [DataRow(Roles.Admin, "Global")]
    [DataRow(Roles.Approver + "," + Roles.Finance, "Mine")]
    [DataRow(Roles.Approver + "," + Roles.Finance, "Global")]
    public async Task RequestAsync_RoleWithoutScopePermission_ThrowsForbiddenWithoutEffect(string roles, string scope)
    {
        (ReportService service, InMemoryReportRepository repository, FakeReportPublisher publisher) = Arrange();

        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(
            () => service.RequestAsync(ReportFixtures.User(roles), NewRequest(scope), CancellationToken.None));

        Assert.IsEmpty(repository.Reports);
        Assert.IsEmpty(publisher.Published);
    }

    /// <summary>
    /// Perfis permitidos: o solicitante vem do token, o registro fica Pending e o id publicado é o persistido.
    /// </summary>
    /// <param name="roles">Roles do usuário.</param>
    /// <param name="scope">Escopo pedido.</param>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    [DataRow(Roles.Employee, "Mine")]
    [DataRow(Roles.Auditor, "Global")]
    [DataRow(Roles.Employee + "," + Roles.Auditor, "Mine")]
    [DataRow(Roles.Employee + "," + Roles.Auditor, "Global")]
    public async Task RequestAsync_AllowedScope_PersistsPendingAndPublishesSameId(string roles, string scope)
    {
        (ReportService service, InMemoryReportRepository repository, FakeReportPublisher publisher) = Arrange();

        ReportSubmission submission = await service.RequestAsync(
            ReportFixtures.User(roles, "user-from-token"),
            NewRequest(scope),
            CancellationToken.None);

        ReportRequest persisted = repository.Reports.Single();
        Assert.IsTrue(submission.Published);
        Assert.AreSame(persisted, submission.Report);
        Assert.AreEqual("user-from-token", persisted.RequesterId);
        Assert.AreEqual(Enum.Parse<ReportScope>(scope), persisted.Scope);
        Assert.AreEqual(ReportStatus.Pending, persisted.Status);
        Assert.AreEqual(ReportFixtures.Now.UtcDateTime, persisted.CreatedAtUtc);
        CollectionAssert.AreEqual(new[] { persisted.Id }, publisher.Published);
    }

    /// <summary>
    /// Sem confirmação do broker o registro continua salvo (para o 503 com id) e não é marcado como publicado.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task RequestAsync_PublishNotConfirmed_KeepsPersistedReportAndSignalsFailure()
    {
        (ReportService service, InMemoryReportRepository repository, FakeReportPublisher publisher) = Arrange();
        publisher.FailConfirmation = true;

        ReportSubmission submission = await service.RequestAsync(
            ReportFixtures.User(Roles.Employee),
            NewRequest("Mine"),
            CancellationToken.None);

        Assert.IsFalse(submission.Published);
        Assert.AreEqual(ReportStatus.Pending, repository.Reports.Single().Status);
        Assert.AreEqual(submission.Report.Id, repository.Reports.Single().Id);
    }

    /// <summary>
    /// Banco indisponível antes de registrar: erro explícito e nada publicado (sem id fictício).
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task RequestAsync_DatabaseUnavailable_ThrowsWithoutPublishing()
    {
        (ReportService service, InMemoryReportRepository repository, FakeReportPublisher publisher) = Arrange();
        repository.FailAtSaveAttempt = 1;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.RequestAsync(ReportFixtures.User(Roles.Employee), NewRequest("Mine"), CancellationToken.None));

        Assert.IsEmpty(repository.Reports);
        Assert.IsEmpty(publisher.Published);
    }

    /// <summary>
    /// Detalhe e Excel de solicitação alheia retornam 404, mesmo para Auditor, Admin ou o outro funcionário.
    /// </summary>
    /// <param name="roles">Roles de quem tenta acessar.</param>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    [DataRow(Roles.Employee)]
    [DataRow(Roles.Auditor)]
    [DataRow(Roles.Admin)]
    [DataRow(Roles.Employee + "," + Roles.Auditor)]
    public async Task GetAndExcel_OthersReport_ThrowNotFound(string roles)
    {
        (ReportService service, InMemoryReportRepository repository, _) = Arrange();
        ReportFixtures.SeedSpecExample(repository);
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);
        CurrentUser intruder = ReportFixtures.User(roles, "intruder");

        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => service.GetAsync(intruder, report.Id, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => service.ExportExcelAsync(intruder, report.Id, CancellationToken.None));
    }

    /// <summary>
    /// Id inexistente: 404 no detalhe e no Excel.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task GetAndExcel_UnknownId_ThrowNotFound()
    {
        (ReportService service, _, _) = Arrange();
        CurrentUser user = ReportFixtures.User(Roles.Employee);

        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => service.GetAsync(user, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => service.ExportExcelAsync(user, Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>
    /// A listagem traz só as solicitações criadas pelo usuário, inclusive para Auditor.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task ListAsync_ReturnsOnlyOwnRequests()
    {
        (ReportService service, InMemoryReportRepository repository, _) = Arrange();
        ReportRequest own = ReportFixtures.SeedReport(repository, ReportScope.Global, "auditor-a");
        ReportFixtures.SeedReport(repository, ReportScope.Global, "auditor-b");
        ReportFixtures.SeedReport(repository, ReportScope.Mine, ReportFixtures.RequesterId);

        IReadOnlyList<ReportRequest> listed =
            await service.ListAsync(ReportFixtures.User(Roles.Auditor, "auditor-a"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { own.Id }, listed.Select(report => report.Id).ToArray());
    }

    /// <summary>
    /// Excel de solicitação própria ainda não concluída (Pending, Processing) ou Failed: 409.
    /// </summary>
    /// <param name="status">Estado da solicitação.</param>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    [DataRow(ReportStatus.Pending)]
    [DataRow(ReportStatus.Processing)]
    [DataRow(ReportStatus.Failed)]
    public async Task ExportExcelAsync_OwnNotCompleted_ThrowsConflict(ReportStatus status)
    {
        (ReportService service, InMemoryReportRepository repository, _) = Arrange();
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);

        if (status == ReportStatus.Processing)
        {
            report.StartProcessing(ReportFixtures.Now.UtcDateTime);
        }
        else if (status == ReportStatus.Failed)
        {
            report.Fail("Teste", "Falha definitiva.", ReportFixtures.Now.UtcDateTime);
        }

        await Assert.ThrowsExactlyAsync<StateConflictException>(() => service.ExportExcelAsync(
            ReportFixtures.User(Roles.Employee),
            report.Id,
            CancellationToken.None));
    }

    /// <summary>
    /// Depois de concluído, alterar um reembolso não muda a consulta nem o Excel: ambos usam o resultado salvo.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task GetAsync_AfterExpenseChanges_ReturnsPersistedResultWithoutRecalculation()
    {
        (ReportService service, InMemoryReportRepository repository, _) = Arrange();
        ReportFixtures.SeedSpecExample(repository);
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);
        int queriesAfterProcessing = repository.ExpenseQueries;

        Expense approved = repository.Expenses.First(expense =>
            expense.OwnerId == ReportFixtures.RequesterId && expense.Status == ExpenseStatus.Approved);
        approved.MarkAsPaid("finance", ReportFixtures.Now.UtcDateTime);
        CurrentUser owner = ReportFixtures.User(Roles.Employee);

        ReportResult result = (await service.GetAsync(owner, report.Id, CancellationToken.None)).GetResult()!;
        byte[] excel = await service.ExportExcelAsync(owner, report.Id, CancellationToken.None);

        Assert.AreEqual(150.00m, result.ByStatus.Single(item => item.Status == ExpenseStatus.Approved).Amount);
        Assert.AreEqual(25.00m, result.ByStatus.Single(item => item.Status == ExpenseStatus.Paid).Amount);
        Assert.IsNotEmpty(excel);
        Assert.AreEqual(queriesAfterProcessing, repository.ExpenseQueries);
    }

    private static (ReportService Service, InMemoryReportRepository Repository, FakeReportPublisher Publisher) Arrange()
    {
        var repository = new InMemoryReportRepository();
        var publisher = new FakeReportPublisher();

        return (ReportFixtures.Service(repository, publisher), repository, publisher);
    }

    private static CreateReportRequest NewRequest(string? scope, DateOnly? from = null, DateOnly? to = null)
    {
        return new CreateReportRequest
        {
            Scope = scope,
            FromDate = from ?? ReportFixtures.From,
            ToDate = to ?? ReportFixtures.To,
        };
    }
}

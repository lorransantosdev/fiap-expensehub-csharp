using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Contracts;
using ExpenseHub.Api.Controllers;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Messaging;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>
/// Códigos HTTP e contrato das respostas do <see cref="ReportsController"/>, sem rede nem banco.
/// </summary>
[TestClass]
public sealed class ReportsControllerTests
{
    /// <summary>
    /// Todas as rotas exigem autenticação (401 para anônimo) e nenhuma é liberada com AllowAnonymous.
    /// </summary>
    [TestMethod]
    public void Controller_RequiresAuthenticationOnEveryRoute()
    {
        Assert.IsNotNull(typeof(ReportsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.IsFalse(typeof(ReportsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null));
    }

    /// <summary>
    /// Publicação confirmada: 202, Location para o detalhe e envelope Pending sem resultado.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task Create_PublishConfirmed_Returns202WithLocation()
    {
        var repository = new InMemoryReportRepository();
        ReportsController controller = Controller(repository, new FakeReportPublisher(), Roles.Employee);

        IActionResult result = await controller.Create(Request("Mine"), CancellationToken.None);

        var accepted = (AcceptedAtActionResult)result;
        var body = (ReportResponse)accepted.Value!;
        Assert.AreEqual(StatusCodes.Status202Accepted, accepted.StatusCode);
        Assert.AreEqual(nameof(ReportsController.GetById), accepted.ActionName);
        Assert.AreEqual(body.Id, accepted.RouteValues!["id"]);
        Assert.AreEqual(ReportStatus.Pending, body.Status);
        Assert.IsNull(body.Result);
        Assert.IsNull(body.Error);
        Assert.IsNull(body.StartedAt);
    }

    /// <summary>
    /// Sem confirmação: 503 com o id persistido e erro sanitizado com código estável.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task Create_PublishNotConfirmed_Returns503WithPersistedId()
    {
        var repository = new InMemoryReportRepository();
        ReportsController controller = Controller(repository, new FakeReportPublisher { FailConfirmation = true }, Roles.Employee);

        IActionResult result = await controller.Create(Request("Mine"), CancellationToken.None);

        var objectResult = (ObjectResult)result;
        var body = (ReportResponse)objectResult.Value!;
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.AreEqual(repository.Reports.Single().Id, body.Id);
        Assert.AreEqual(ReportResponse.PublishNotConfirmedCode, body.Error!.Code);
        Assert.DoesNotContain("Exception", body.Error.Message);
    }

    /// <summary>
    /// Download: arquivo com Content-Type Open XML e nome report-{id}.xlsx.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task Excel_CompletedOwnReport_ReturnsXlsxFile()
    {
        var repository = new InMemoryReportRepository();
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);
        ReportsController controller = Controller(repository, new FakeReportPublisher(), Roles.Employee);

        var file = (FileContentResult)await controller.Excel(report.Id, CancellationToken.None);

        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.AreEqual($"report-{report.Id}.xlsx", file.FileDownloadName);
        Assert.IsNotEmpty(file.FileContents);
    }

    /// <summary>
    /// O envelope do detalhe repete o resultado salvo e os instantes do processamento.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task GetById_CompletedReport_ReturnsEnvelopeWithResult()
    {
        var repository = new InMemoryReportRepository();
        ReportFixtures.SeedSpecExample(repository);
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);
        ReportsController controller = Controller(repository, new FakeReportPublisher(), Roles.Employee);

        ReportResponse body = (await controller.GetById(report.Id, CancellationToken.None)).Value!;

        Assert.AreEqual(ReportStatus.Completed, body.Status);
        Assert.IsNotNull(body.StartedAt);
        Assert.IsNotNull(body.CompletedAt);
        Assert.IsNull(body.Error);
        Assert.AreEqual(175.00m, body.Result!.TotalAmount);
        Assert.HasCount(5, body.Result.ByStatus);
    }

    /// <summary>
    /// A mensagem publicada carrega somente o reportId e é lida de volta pelo worker.
    /// </summary>
    [TestMethod]
    public void ReportMessage_RoundTrip_CarriesOnlyReportId()
    {
        var id = Guid.NewGuid();
        byte[] bytes = new ReportMessage(id).ToBytes();

        Assert.AreEqual($"{{\"reportId\":\"{id}\"}}", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.IsTrue(ReportMessage.TryParse(bytes, out ReportMessage? parsed));
        Assert.AreEqual(id, parsed!.ReportId);
    }

    /// <summary>
    /// Corpo inválido não vira processamento.
    /// </summary>
    /// <param name="body">Corpo recebido.</param>
    [TestMethod]
    [DataRow("não é json")]
    [DataRow("{}")]
    [DataRow("{\"reportId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public void ReportMessage_InvalidBody_IsRejected(string body)
    {
        Assert.IsFalse(ReportMessage.TryParse(System.Text.Encoding.UTF8.GetBytes(body), out _));
    }

    private static ReportsController Controller(
        InMemoryReportRepository repository,
        FakeReportPublisher publisher,
        string roles)
    {
        IEnumerable<Claim> claims = new[] { new Claim(ClaimTypes.NameIdentifier, ReportFixtures.RequesterId) }
            .Concat(roles.Split(',').Select(role => new Claim(ClaimTypes.Role, role)));

        return new ReportsController(ReportFixtures.Service(repository, publisher))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }

    private static ExpenseHub.Api.Dtos.CreateReportRequest Request(string scope)
    {
        return new ExpenseHub.Api.Dtos.CreateReportRequest
        {
            Scope = scope,
            FromDate = ReportFixtures.From,
            ToDate = ReportFixtures.To,
        };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Contracts;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseHub.Api.Controllers;

/// <summary>
/// Endpoints dos relatórios de reembolsos processados em segundo plano.
/// </summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportService _service;

    /// <summary>
    /// Inicializa o controller.
    /// </summary>
    /// <param name="service">Serviço de relatórios.</param>
    public ReportsController(IReportService service)
    {
        _service = service;
    }

    /// <summary>
    /// Solicita um relatório. Responde 202 somente depois de persistir e ter a publicação confirmada.
    /// </summary>
    /// <param name="request">Período e escopo.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>202 com Location, ou 503 com o id persistido quando o broker não confirma.</returns>
    [HttpPost]
    [ProducesResponseType<ReportResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ReportResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(CreateReportRequest request, CancellationToken cancellationToken)
    {
        ReportSubmission submission =
            await _service.RequestAsync(CurrentUser.FromPrincipal(User), request, cancellationToken);

        if (!submission.Published)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ReportResponse.PublishNotConfirmed(submission.Report));
        }

        return AcceptedAtAction(
            nameof(GetById),
            new { id = submission.Report.Id },
            ReportResponse.FromEntity(submission.Report));
    }

    /// <summary>
    /// Lista somente as solicitações criadas pelo usuário autenticado.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>As solicitações do usuário.</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReportResponse>>> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportRequest> reports = await _service.ListAsync(CurrentUser.FromPrincipal(User), cancellationToken);

        return reports.Select(ReportResponse.FromEntity).ToList();
    }

    /// <summary>
    /// Consulta andamento e resultado de uma solicitação própria.
    /// </summary>
    /// <param name="id">Identificador da solicitação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>O envelope atualizado, ou 404 para inexistente ou alheia.</returns>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReportResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        ReportRequest report = await _service.GetAsync(CurrentUser.FromPrincipal(User), id, cancellationToken);

        return ReportResponse.FromEntity(report);
    }

    /// <summary>
    /// Baixa o resultado salvo de uma solicitação própria concluída em formato .xlsx.
    /// </summary>
    /// <param name="id">Identificador da solicitação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A planilha; 404 para inexistente ou alheia; 409 quando ainda não está Completed.</returns>
    [HttpGet("{id:guid}/excel")]
    public async Task<IActionResult> Excel(Guid id, CancellationToken cancellationToken)
    {
        byte[] content = await _service.ExportExcelAsync(CurrentUser.FromPrincipal(User), id, cancellationToken);

        return File(content, ReportExcelWriter.ContentType, ReportExcelWriter.FileName(id));
    }
}

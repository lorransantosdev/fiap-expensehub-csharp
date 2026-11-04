using System;
using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Dtos;

/// <summary>
/// Dados de entrada para solicitar um relatório. O solicitante vem sempre do token.
/// </summary>
public sealed class CreateReportRequest
{
    /// <summary>
    /// Obtém ou define o primeiro dia do período (yyyy-MM-dd), inclusive.
    /// </summary>
    [Required]
    public DateOnly? FromDate { get; set; }

    /// <summary>
    /// Obtém ou define o último dia do período (yyyy-MM-dd), inclusive.
    /// </summary>
    [Required]
    public DateOnly? ToDate { get; set; }

    /// <summary>
    /// Obtém ou define o escopo: Mine ou Global.
    /// </summary>
    [Required]
    public string? Scope { get; set; }
}

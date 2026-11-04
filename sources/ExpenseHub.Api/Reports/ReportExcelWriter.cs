using System;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Monta a planilha .xlsx somente a partir do resultado salvo, sem consultar os reembolsos atuais.
/// </summary>
public static class ReportExcelWriter
{
    /// <summary>
    /// Content-Type de planilhas Open XML.
    /// </summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Nome da aba com o relatório.
    /// </summary>
    public const string SheetName = "Relatorio";

    /// <summary>
    /// Primeira linha da tabela por estado (cabeçalho).
    /// </summary>
    public const int TableHeaderRow = 7;

    /// <summary>
    /// Nome do arquivo baixado.
    /// </summary>
    /// <param name="reportId">Identificador da solicitação.</param>
    /// <returns>O nome do arquivo.</returns>
    public static string FileName(Guid reportId)
    {
        return $"report-{reportId}.xlsx";
    }

    /// <summary>
    /// Gera a planilha de uma solicitação concluída.
    /// </summary>
    /// <param name="report">Solicitação em <see cref="ReportStatus.Completed"/>.</param>
    /// <returns>O conteúdo do arquivo .xlsx.</returns>
    public static byte[] Write(ReportRequest report)
    {
        ArgumentNullException.ThrowIfNull(report);

        ReportResult result = report.GetResult()
            ?? throw new InvalidOperationException("A solicitação não possui resultado salvo.");

        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.Worksheets.Add(SheetName);

        sheet.Cell(1, 1).Value = "Id";
        sheet.Cell(1, 2).Value = report.Id.ToString();
        sheet.Cell(2, 1).Value = "Escopo";
        sheet.Cell(2, 2).Value = report.Scope.ToString();
        sheet.Cell(3, 1).Value = "De";
        sheet.Cell(3, 2).Value = report.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        sheet.Cell(4, 1).Value = "Até";
        sheet.Cell(4, 2).Value = report.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        sheet.Cell(5, 1).Value = "Gerado em (UTC)";
        sheet.Cell(5, 2).Value = result.GeneratedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        sheet.Cell(TableHeaderRow, 1).Value = "Estado";
        sheet.Cell(TableHeaderRow, 2).Value = "Quantidade";
        sheet.Cell(TableHeaderRow, 3).Value = "Valor";
        sheet.Row(TableHeaderRow).Style.Font.Bold = true;

        int row = TableHeaderRow + 1;

        foreach (ReportStatusSummary summary in result.ByStatus)
        {
            sheet.Cell(row, 1).Value = summary.Status.ToString();
            sheet.Cell(row, 2).Value = summary.Count;
            sheet.Cell(row, 3).Value = summary.Amount;
            row++;
        }

        sheet.Cell(row, 1).Value = "Total";
        sheet.Cell(row, 2).Value = result.TotalCount;
        sheet.Cell(row, 3).Value = result.TotalAmount;
        sheet.Row(row).Style.Font.Bold = true;

        sheet.Column(3).Style.NumberFormat.Format = "#,##0.00";
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }
}

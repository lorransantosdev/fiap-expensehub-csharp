using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>
/// Contrato do download: planilha .xlsx real, células numéricas e dados iguais ao resultado salvo.
/// </summary>
[TestClass]
public sealed class ReportExcelWriterTests
{
    /// <summary>
    /// A planilha abre como .xlsx e repete identificação, período, geração, os cinco estados e o total.
    /// </summary>
    /// <returns>Tarefa do teste.</returns>
    [TestMethod]
    public async Task Write_CompletedReport_ProducesWorkbookMatchingSavedResult()
    {
        var repository = new InMemoryReportRepository();
        ReportFixtures.SeedSpecExample(repository);
        ReportRequest report = ReportFixtures.SeedReport(repository, ReportScope.Mine);
        await ReportFixtures.Processor(repository).ProcessAsync(report.Id, CancellationToken.None);
        ReportResult saved = report.GetResult()!;

        using XLWorkbook workbook = Open(ReportExcelWriter.Write(report));
        IXLWorksheet sheet = workbook.Worksheet(ReportExcelWriter.SheetName);

        Assert.AreEqual(report.Id.ToString(), sheet.Cell(1, 2).GetString());
        Assert.AreEqual("Mine", sheet.Cell(2, 2).GetString());
        Assert.AreEqual("2026-09-01", sheet.Cell(3, 2).GetString());
        Assert.AreEqual("2026-09-30", sheet.Cell(4, 2).GetString());
        Assert.AreEqual("2026-10-01T12:00:00Z", sheet.Cell(5, 2).GetString());
        Assert.AreEqual("Estado", sheet.Cell(ReportExcelWriter.TableHeaderRow, 1).GetString());
        Assert.AreEqual("Quantidade", sheet.Cell(ReportExcelWriter.TableHeaderRow, 2).GetString());
        Assert.AreEqual("Valor", sheet.Cell(ReportExcelWriter.TableHeaderRow, 3).GetString());

        int row = ReportExcelWriter.TableHeaderRow + 1;

        foreach (ReportStatusSummary summary in saved.ByStatus)
        {
            Assert.AreEqual(summary.Status.ToString(), sheet.Cell(row, 1).GetString());
            Assert.AreEqual(XLDataType.Number, sheet.Cell(row, 2).DataType);
            Assert.AreEqual(XLDataType.Number, sheet.Cell(row, 3).DataType);
            Assert.AreEqual(summary.Count, sheet.Cell(row, 2).GetValue<int>());
            Assert.AreEqual(summary.Amount, sheet.Cell(row, 3).GetValue<decimal>());
            row++;
        }

        Assert.AreEqual("Total", sheet.Cell(row, 1).GetString());
        Assert.AreEqual(3, sheet.Cell(row, 2).GetValue<int>());
        Assert.AreEqual(175.00m, sheet.Cell(row, 3).GetValue<decimal>());
    }

    /// <summary>
    /// Conjunto vazio: os cinco estados aparecem com zero numérico e o total também.
    /// </summary>
    [TestMethod]
    public void Write_EmptyResult_KeepsFiveZeroedRows()
    {
        var report = ReportRequest.Create("u", ReportScope.Global, ReportFixtures.From, ReportFixtures.To, ReportFixtures.Now.UtcDateTime);
        report.Complete(ReportCalculator.Calculate([], ReportFixtures.Now.UtcDateTime), ReportFixtures.Now.UtcDateTime);

        using XLWorkbook workbook = Open(ReportExcelWriter.Write(report));
        IXLWorksheet sheet = workbook.Worksheet(ReportExcelWriter.SheetName);
        int firstRow = ReportExcelWriter.TableHeaderRow + 1;
        int[] rows = Enumerable.Range(firstRow, Enum.GetValues<ExpenseStatus>().Length + 1).ToArray();

        foreach (int row in rows)
        {
            Assert.AreEqual(0, sheet.Cell(row, 2).GetValue<int>());
            Assert.AreEqual(0m, sheet.Cell(row, 3).GetValue<decimal>());
            Assert.AreEqual(XLDataType.Number, sheet.Cell(row, 3).DataType);
        }

        Assert.AreEqual("Total", sheet.Cell(rows[^1], 1).GetString());
    }

    /// <summary>
    /// Nome de arquivo report-{id}.xlsx (o Content-Type é verificado no teste do controller).
    /// </summary>
    [TestMethod]
    public void FileName_FollowsDownloadContract()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.AreEqual("report-11111111-2222-3333-4444-555555555555.xlsx", ReportExcelWriter.FileName(id));
    }

    /// <summary>
    /// Sem resultado salvo não há planilha.
    /// </summary>
    [TestMethod]
    public void Write_PendingReport_Throws()
    {
        var report = ReportRequest.Create("u", ReportScope.Mine, ReportFixtures.From, ReportFixtures.To, ReportFixtures.Now.UtcDateTime);

        Assert.ThrowsExactly<InvalidOperationException>(() => ReportExcelWriter.Write(report));
    }

    private static XLWorkbook Open(byte[] content)
    {
        // Um .xlsx é um pacote ZIP: começa com "PK". CSV ou texto renomeado não passariam aqui nem na abertura.
        Assert.AreEqual((byte)'P', content[0]);
        Assert.AreEqual((byte)'K', content[1]);

        return new XLWorkbook(new MemoryStream(content));
    }
}

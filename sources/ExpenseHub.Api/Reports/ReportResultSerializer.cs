using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Serialização do resultado salvo no banco.
/// </summary>
public static class ReportResultSerializer
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Serializa o resultado.
    /// </summary>
    /// <param name="result">Resultado do relatório.</param>
    /// <returns>O JSON do resultado.</returns>
    public static string Serialize(ReportResult result)
    {
        return JsonSerializer.Serialize(result, _options);
    }

    /// <summary>
    /// Desserializa o resultado salvo.
    /// </summary>
    /// <param name="json">JSON salvo.</param>
    /// <returns>O resultado.</returns>
    public static ReportResult Deserialize(string json)
    {
        return JsonSerializer.Deserialize<ReportResult>(json, _options)
            ?? throw new InvalidOperationException("Resultado de relatório salvo é inválido.");
    }
}

using System;
using System.Text.Json;

namespace ExpenseHub.Api.Messaging;

/// <summary>
/// Contrato mínimo da mensagem: somente o identificador. Período, escopo e solicitante vêm do banco.
/// </summary>
/// <param name="ReportId">Identificador da solicitação persistida.</param>
public sealed record ReportMessage(Guid ReportId)
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializa a mensagem como <c>{"reportId":"..."}</c>.
    /// </summary>
    /// <returns>Os bytes UTF-8 do JSON.</returns>
    public byte[] ToBytes()
    {
        return JsonSerializer.SerializeToUtf8Bytes(this, _options);
    }

    /// <summary>
    /// Tenta ler a mensagem recebida da fila.
    /// </summary>
    /// <param name="body">Corpo da mensagem.</param>
    /// <param name="message">Mensagem lida, quando válida.</param>
    /// <returns><see langword="true"/> quando o corpo contém um reportId válido.</returns>
    public static bool TryParse(ReadOnlySpan<byte> body, out ReportMessage? message)
    {
        message = null;

        try
        {
            ReportMessage? parsed = JsonSerializer.Deserialize<ReportMessage>(body, _options);

            if (parsed is null || parsed.ReportId == Guid.Empty)
            {
                return false;
            }

            message = parsed;

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

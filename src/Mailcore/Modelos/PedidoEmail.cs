using System.Text.Json.Serialization;

namespace Mailcore.Modelos;

public sealed class PedidoEmail
{
    [JsonPropertyName("remetente"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Remetente { get; init; }
    [JsonPropertyName("destinatarios"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Destinatarios { get; init; }
    [JsonPropertyName("assunto"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Assunto { get; init; }
    [JsonPropertyName("html"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Html { get; init; }
    [JsonPropertyName("texto"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Texto { get; init; }
    [JsonPropertyName("responder_para"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponderPara { get; init; }
    [JsonPropertyName("agendada_em"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgendadaEm { get; init; }
    [JsonPropertyName("cc"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Cc { get; init; }
    [JsonPropertyName("cco"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Cco { get; init; }
    [JsonPropertyName("cabecalhos"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string,string>? Cabecalhos { get; init; }
    [JsonPropertyName("tags"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string,string>? Tags { get; init; }
    [JsonPropertyName("anexos"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AnexoEmail>? Anexos { get; init; }
    [JsonPropertyName("modelo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModeloEmail? Modelo { get; init; }
    public override string ToString() => "PedidoEmail { Conteudo = protegido }";
}

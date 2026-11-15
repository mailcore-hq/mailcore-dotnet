using System.Text.Json.Serialization;

namespace Mailcore.Modelos;

public sealed class AnexoEmail
{
    [JsonPropertyName("nome"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Nome { get; init; }
    [JsonPropertyName("tipo_conteudo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TipoConteudo { get; init; }
    [JsonPropertyName("conteudo_base64"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConteudoBase64 { get; init; }
    [JsonPropertyName("id_conteudo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdConteudo { get; init; }
    public override string ToString() => "AnexoEmail { Conteudo = protegido }";
}

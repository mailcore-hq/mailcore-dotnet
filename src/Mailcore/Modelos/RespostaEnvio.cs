using System.Text.Json.Serialization;

namespace Mailcore.Modelos;

public sealed class RespostaEnvio
{
    [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    [JsonPropertyName("estado"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Estado { get; init; }
    public override string ToString() => "RespostaEnvio { Conteudo = protegido }";
}

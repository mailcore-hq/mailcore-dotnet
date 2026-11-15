using System.Text.Json.Serialization;

namespace Mailcore.Modelos;

public sealed class ModeloEmail
{
    [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    [JsonPropertyName("variaveis"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string,object?>? Variaveis { get; init; }
    public override string ToString() => "ModeloEmail { Conteudo = protegido }";
}

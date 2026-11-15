using System.Text.Json;

namespace Mailcore;

public sealed class EventoWebhook
{
    public string Tipo { get; }
    public string CriadoEm { get; }
    public JsonElement Dados { get; }
    internal EventoWebhook(string tipo, string criadoEm, JsonElement dados)
    { Tipo = tipo; CriadoEm = criadoEm; Dados = dados.Clone(); }
    public override string ToString() => "EventoWebhook { Conteudo = protegido }";
}

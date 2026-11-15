using System.Text.Json;

namespace Mailcore;

/// <summary>Resposta imutável com bytes originais, metadados e decodificação explícita.</summary>
public sealed class Resposta
{
    private readonly byte[] _dados;
    public byte[] Dados => _dados.ToArray();
    public int Status { get; }
    public long TotalBytes { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Cabecalhos { get; }
    public ErroMailcore? Erro { get; }
    public bool Sucesso => Erro is null && Status is >= 200 and < 300;

    internal Resposta(byte[] dados, int status, IReadOnlyDictionary<string, IReadOnlyList<string>> cabecalhos,
        long totalBytes, ErroMailcore? erro = null)
    {
        _dados = dados.ToArray(); Status = status; TotalBytes = totalBytes;
        Cabecalhos = Interno.Cabecalhos.Copiar(cabecalhos); Erro = erro;
    }

    public JsonElement Json() => Interno.Json.Ler(_dados);
    public T? Decodificar<T>() => Interno.Json.Decodificar<T>(_dados);
    internal static Resposta DeErro(ErroMailcore erro) => new([], erro.Status ?? 0, erro.Cabecalhos, 0, erro);
    public override string ToString() => $"Resposta {{ Status = {Status}, TotalBytes = {TotalBytes}, Sucesso = {Sucesso} }}";
}

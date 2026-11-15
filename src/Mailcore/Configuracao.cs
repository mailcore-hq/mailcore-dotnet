namespace Mailcore;

/// <summary>Opções do cliente; uma cópia é obtida na construção do Cliente.</summary>
public sealed class Configuracao
{
    public string? ChaveApi { get; set; }
    public Func<CancellationToken, ValueTask<string>>? FornecedorChave { get; set; }
    public string UrlBase { get; set; } = "https://mailcore.com.br/v1";
    public TimeSpan TempoLimite { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan TempoConexao { get; set; } = TimeSpan.FromSeconds(10);
    public long LimiteRespostaBytes { get; set; } = 16 * 1024 * 1024;
    public byte[]? CertificadoCa { get; set; }
    public bool PermitirHttpLocal { get; set; }
    public bool LancarExcecoes { get; set; } = true;

    internal Configuracao Copiar() => new()
    {
        ChaveApi = ChaveApi, FornecedorChave = FornecedorChave, UrlBase = UrlBase,
        TempoLimite = TempoLimite, TempoConexao = TempoConexao, LimiteRespostaBytes = LimiteRespostaBytes,
        CertificadoCa = CertificadoCa?.ToArray(), PermitirHttpLocal = PermitirHttpLocal,
        LancarExcecoes = LancarExcecoes
    };

    public override string ToString() => "Configuracao { Conteudo = protegido }";
}

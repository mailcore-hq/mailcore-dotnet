using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Mailcore.Interno;

internal static class ConfiguracaoValidada
{
    internal static void Validar(Configuracao c)
    {
        Json.ValidarTexto(c.UrlBase);
        if (!Uri.TryCreate(c.UrlBase, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            c.UrlBase.Any(x => x <= 32 || x == 127 || x == '\\') ||
            !(uri.Scheme == "https" || (uri.Scheme == "http" && c.PermitirHttpLocal &&
                uri.Host.Trim('[', ']') is "localhost" or "127.0.0.1" or "::1")))
            throw ErroMailcore.Configuracao("Use URL HTTPS válida; HTTP de teste se limita a loopback.");
        Prazo(c.TempoLimite); Prazo(c.TempoConexao); Limite(c.LimiteRespostaBytes);
        if (c.ChaveApi is not null) Chave(c.ChaveApi);
        if (c.ChaveApi is not null && c.FornecedorChave is not null)
            throw ErroMailcore.Configuracao("Escolha chave explícita ou fornecedor, sem ambiguidade.");
        if (c.CertificadoCa is not null) { using var _ = new Certificados(c.CertificadoCa); }
    }

    internal static TimeSpan Prazo(TimeSpan valor) => valor > TimeSpan.Zero && valor <= TimeSpan.FromHours(24)
        ? valor : throw ErroMailcore.Configuracao("Use prazo positivo de até 24 horas.");
    internal static long Limite(long valor) => valor > 0 ? valor : throw ErroMailcore.Configuracao("O limite de resposta deve ser positivo.");
    internal static string Chave(string? valor) => !string.IsNullOrEmpty(valor) && valor.All(x => x is >= (char)33 and <= (char)126)
        ? valor : throw ErroMailcore.Configuracao("Informe uma chave válida ou MAILCORE_API_KEY.");

    internal static SocketsHttpHandler CriarHandler(Configuracao c)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false, UseProxy = false, AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = c.TempoConexao, MaxResponseHeadersLength = 64,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
            MaxConnectionsPerServer = 100, EnableMultipleHttp2Connections = true,
            ActivityHeadersPropagator = null,
            SslOptions = new SslClientAuthenticationOptions { EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }
        };
        if (c.CertificadoCa is not null)
        {
            var pem = c.CertificadoCa.ToArray();
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificado, cadeia, falhas) =>
            {
                if (falhas == SslPolicyErrors.None) return true;
                if (certificado is null || (falhas & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
                try
                {
                    using var raizes = new Certificados(pem);
                    using var verificar = new X509Chain();
                    verificar.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    verificar.ChainPolicy.CustomTrustStore.AddRange(raizes.Valores);
                    verificar.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
                    verificar.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    if (cadeia is not null)
                        foreach (var item in cadeia.ChainElements.Cast<X509ChainElement>().Skip(1))
                            verificar.ChainPolicy.ExtraStore.Add(item.Certificate);
                    using var folha = new X509Certificate2(certificado);
                    return verificar.Build(folha);
                }
                catch (Exception) { return false; }
            };
        }
        return handler;
    }

    private sealed class Certificados : IDisposable
    {
        internal X509Certificate2Collection Valores { get; } = [];
        internal Certificados(byte[] pem)
        {
            try
            {
                Valores.ImportFromPem(System.Text.Encoding.ASCII.GetString(pem));
                if (Valores.Count == 0) throw ErroMailcore.Configuracao("Autoridade certificadora inválida.");
            }
            catch (Exception)
            { Dispose(); throw ErroMailcore.Configuracao("Informe certificados PEM válidos."); }
        }
        public void Dispose() { foreach (var certificado in Valores) certificado.Dispose(); }
    }
}

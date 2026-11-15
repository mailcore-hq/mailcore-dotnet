using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Mailcore.Tests;

public sealed class TlsUsoChaveTestes
{
    [Fact]
    public async Task CaAdicionalExigeServerAuthERecusaClientAuthAntesDoPedido()
    {
        await using var laboratorio = await Laboratorio.IniciarAsync();
        using (var cliente = new Cliente("mc_sintetica_tls", new Configuracao
        {
            UrlBase = laboratorio.ClientAuth,
            CertificadoCa = laboratorio.Ca,
            TempoLimite = TimeSpan.FromSeconds(5)
        }))
        {
            var erro = await Assert.ThrowsAsync<ErroMailcore>(() => cliente.Emails.ObterAsync("id"));
            Assert.Equal("falha_de_rede", erro.Codigo);
            Assert.Null(erro.InnerException);
        }

        using (var cliente = new Cliente("mc_sintetica_tls", new Configuracao
        {
            UrlBase = laboratorio.ServerAuth,
            CertificadoCa = laboratorio.Ca,
            TempoLimite = TimeSpan.FromSeconds(5)
        }))
        {
            var resposta = await cliente.Emails.ObterAsync("id");
            Assert.Equal(200, resposta.Status);
            Assert.Equal("mensagem-sintetica", resposta.Json().GetProperty("id").GetString());
        }

        var registros = await laboratorio.ConsultarAsync();
        Assert.Equal(0, registros.GetProperty("client_auth").GetInt32());
        Assert.Equal(1, registros.GetProperty("server_auth").GetInt32());
    }

    private sealed class Laboratorio : IAsyncDisposable
    {
        private readonly Process _processo;
        internal string ClientAuth { get; }
        internal string ServerAuth { get; }
        internal byte[] Ca { get; }

        private Laboratorio(Process processo, JsonElement inicial)
        {
            _processo = processo;
            ClientAuth = inicial.GetProperty("client_auth").GetString()!;
            ServerAuth = inicial.GetProperty("server_auth").GetString()!;
            Ca = Convert.FromBase64String(inicial.GetProperty("ca").GetString()!);
        }

        internal static async Task<Laboratorio> IniciarAsync()
        {
            var inicio = new ProcessStartInfo("python3")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            inicio.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Suporte", "servidor_tls_uso_chave.py"));
            var processo = Process.Start(inicio) ?? throw new InvalidOperationException("Não foi possível iniciar o laboratório TLS.");
            try
            {
                var dados = await LerAsync(processo);
                return new Laboratorio(processo, dados);
            }
            catch
            {
                if (!processo.HasExited) processo.Kill(entireProcessTree: true);
                processo.Dispose();
                throw;
            }
        }

        internal async Task<JsonElement> ConsultarAsync()
        {
            await _processo.StandardInput.WriteLineAsync("{\"acao\":\"registros\"}");
            await _processo.StandardInput.FlushAsync();
            return await LerAsync(_processo);
        }

        private static async Task<JsonElement> LerAsync(Process processo)
        {
            var linha = await processo.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (linha is null) throw new InvalidOperationException("O laboratório TLS encerrou prematuramente.");
            using var json = JsonDocument.Parse(linha);
            return json.RootElement.Clone();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_processo.HasExited)
                {
                    await _processo.StandardInput.WriteLineAsync("{\"acao\":\"encerrar\"}");
                    await _processo.StandardInput.FlushAsync();
                    await LerAsync(_processo);
                    await _processo.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            finally
            {
                if (!_processo.HasExited) _processo.Kill(entireProcessTree: true);
                _processo.Dispose();
            }
        }
    }
}

"""Laboratório TLS privado para verificar Extended Key Usage, sem serviços externos."""

import base64
import json
import ssl
import subprocess
import sys
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class Servidor(ThreadingHTTPServer):
    daemon_threads = True

    def handle_error(self, _pedido, _endereco):
        pass


class Atendimento(BaseHTTPRequestHandler):
    def log_message(self, _formato, *_argumentos):
        pass

    def do_GET(self):
        with self.server.mutex:
            self.server.contagem += 1
        corpo = b'{"id":"mensagem-sintetica","estado":"aceita"}'
        self.send_response(200)
        self.send_header("Content-Length", str(len(corpo)))
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(corpo)


def executar(argumentos):
    subprocess.run(list(map(str, argumentos)), check=True, stdout=subprocess.DEVNULL,
                   stderr=subprocess.DEVNULL, timeout=30)


def identidade(pasta, nome, uso):
    certificado, chave, pedido = (pasta / (nome + extensao) for extensao in (".pem", ".key", ".csr"))
    extensoes = pasta / (nome + ".cnf")
    extensoes.write_text("basicConstraints=critical,CA:FALSE\n"
                        "keyUsage=critical,digitalSignature,keyEncipherment\n"
                        "extendedKeyUsage=critical," + uso + "\n"
                        "subjectAltName=IP:127.0.0.1\n")
    executar(["openssl", "req", "-new", "-newkey", "rsa:2048", "-nodes", "-subj", "/CN=API sintetica Mailcore",
              "-keyout", chave, "-out", pedido])
    executar(["openssl", "x509", "-req", "-in", pedido, "-CA", pasta / "ca.pem", "-CAkey", pasta / "ca.key",
              "-CAcreateserial", "-days", "1", "-extfile", extensoes, "-out", certificado])
    chave.chmod(0o600)
    return certificado, chave


def servidor(certificado, chave):
    instancia = Servidor(("127.0.0.1", 0), Atendimento)
    instancia.mutex = threading.Lock()
    instancia.contagem = 0
    contexto = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    contexto.minimum_version = ssl.TLSVersion.TLSv1_2
    contexto.load_cert_chain(certificado, chave)
    instancia.socket = contexto.wrap_socket(instancia.socket, server_side=True)
    thread = threading.Thread(target=instancia.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True)
    thread.start()
    return instancia, thread


def publicar(valor):
    print(json.dumps(valor), flush=True)


def main():
    servidores = []
    with tempfile.TemporaryDirectory(prefix="mailcore-tls-uso-") as temporario:
        pasta = Path(temporario)
        executar(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1",
                  "-subj", "/CN=CA sintetica Mailcore", "-addext", "basicConstraints=critical,CA:TRUE",
                  "-keyout", pasta / "ca.key", "-out", pasta / "ca.pem"])
        (pasta / "ca.key").chmod(0o600)
        try:
            for nome, uso in (("client_auth", "clientAuth"), ("server_auth", "serverAuth")):
                certificado, chave = identidade(pasta, nome, uso)
                instancia, thread = servidor(certificado, chave)
                servidores.append((nome, instancia, thread))
            publicar({"ca": base64.b64encode((pasta / "ca.pem").read_bytes()).decode(),
                      **{nome: f"https://127.0.0.1:{instancia.server_port}/v1" for nome, instancia, _ in servidores}})
            for linha in sys.stdin:
                comando = json.loads(linha)
                if comando["acao"] == "registros":
                    contagens = {}
                    for nome, instancia, _ in servidores:
                        with instancia.mutex:
                            contagens[nome] = instancia.contagem
                    publicar(contagens)
                elif comando["acao"] == "encerrar":
                    publicar({"ok": True})
                    break
                else:
                    publicar({"erro": "Comando de laboratório inválido."})
        finally:
            for _, instancia, thread in servidores:
                instancia.shutdown()
                instancia.server_close()
                thread.join(2)


if __name__ == "__main__":
    main()

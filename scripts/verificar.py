#!/usr/bin/env python3
"""Compila uma vez com SDK 11 e verifica os runtimes/SDKs .NET da matriz em Docker."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

SDK = Path(__file__).resolve().parents[1]
IMAGENS = json.loads((SDK / "testes/imagens.json").read_text())


def executar(comando, registro=None):
    retorno = subprocess.run(comando, cwd=SDK, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    texto = retorno.stdout.decode("utf-8", errors="replace")
    if registro:
        registro.write_text(texto)
    if retorno.returncode:
        print(texto[-18000:], file=sys.stderr)
        raise RuntimeError("Verificação falhou; consulte o registro.")
    return texto


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--perfis", nargs="+", choices=list(IMAGENS), default=list(IMAGENS))
    parser.add_argument("--sem-build", action="store_true")
    argumentos = parser.parse_args()
    pasta = SDK / "dist/verificacao"
    pasta.mkdir(parents=True, exist_ok=True)
    executar([sys.executable, "scripts/gerar_recursos.py", "--verificar"])
    arquivo = pasta / "matriz.json"
    anteriores = json.loads(arquivo.read_text()) if arquivo.exists() else []
    resultados = {item["perfil"]: item for item in anteriores}
    for perfil in argumentos.perfis:
        imagem = "mailcore/dotnet-testes:" + perfil
        framework = "net" + perfil + ".0"
        if not argumentos.sem_build:
            print("Compilando imagem .NET " + perfil + "...", flush=True)
            executar(["docker", "build", "--build-arg", "IMAGEM_DOTNET=" + IMAGENS[perfil]["sdk"]["imagem"],
                      "--build-arg", "FRAMEWORK=" + framework, "-t", imagem, "."], pasta / ("build-" + perfil + ".log"))
        print("Verificando .NET " + perfil + " sem rede externa...", flush=True)
        texto = executar(["docker", "run", "--rm", "--network", "none", "--read-only", "--cap-drop", "ALL",
                          "--security-opt", "no-new-privileges", "--memory", "1536m", "--pids-limit", "512",
                          "--tmpfs", "/tmp:rw,exec,nosuid,size=512m", imagem], pasta / ("dotnet-" + perfil + ".log"))
        linhas = [linha for linha in texto.splitlines() if linha.startswith('{"resultado":')]
        resultado = json.loads(linhas[-1]) if linhas else {}
        if resultado.get("resultado") != "aprovado" or resultado.get("framework") != framework:
            raise RuntimeError("O VSTest não confirmou o perfil esperado.")
        info = executar(["docker", "run", "--rm", "--network", "none", imagem, "dotnet", "--info"])
        resultado.update({"perfil": perfil, "imagem": IMAGENS[perfil]["sdk"]["imagem"],
                          "dotnet_info": info.strip(), "rede": "none", "suporte_microsoft": IMAGENS[perfil]["suportado"]})
        resultados[perfil] = resultado
        arquivo.write_text(json.dumps(sorted(resultados.values(), key=lambda r: int(r["perfil"])), ensure_ascii=False, indent=2) + "\n")
        print(".NET " + perfil + ": " + str(resultado["testes"]) + " testes aprovados.", flush=True)


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, ValueError) as erro:
        print(str(erro), file=sys.stderr)
        sys.exit(1)

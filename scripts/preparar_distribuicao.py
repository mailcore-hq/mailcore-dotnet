#!/usr/bin/env python3
"""Prepara os pacotes NuGet locais e um feed público offline, sem publicação."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

SDK = Path(__file__).resolve().parents[1]
PACOTE = "Mailcore"
VERSAO = "0.1.0-alpha.1"
ETAPA = "configuracao"


def exigir(condicao):
    if not condicao:
        raise ValueError("O contrato de distribuição não foi satisfeito.")


def sha256(conteudo):
    return hashlib.sha256(conteudo).hexdigest()


def executar(comando, pasta=SDK):
    ambiente = os.environ.copy()
    ambiente.update({"DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1",
                     "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1"})
    retorno = subprocess.run(comando, cwd=pasta, env=ambiente, stdout=subprocess.PIPE,
                             stderr=subprocess.PIPE, check=False, timeout=300)
    exigir(retorno.returncode == 0)
    return retorno.stdout


def inventariar(pasta):
    digest = hashlib.sha256()
    arquivos = {}
    for caminho in sorted(pasta.rglob("*")):
        exigir(not caminho.is_symlink())
        if not caminho.is_file():
            continue
        nome = caminho.relative_to(pasta).as_posix()
        conteudo = caminho.read_bytes()
        arquivos[nome] = sha256(conteudo)
        digest.update(nome.encode() + b"\0" + conteudo)
    return digest.hexdigest(), arquivos


def metadados_nuget(arquivo, identidade=PACOTE, simbolos=False):
    descritores = [nome for nome in arquivo.namelist() if nome.endswith(".nuspec")]
    exigir(len(descritores) == 1)
    raiz = ET.fromstring(arquivo.read(descritores[0]))
    nome = lambda elemento: elemento.tag.rsplit("}", 1)[-1]
    metadata = next(item for item in raiz if nome(item) == "metadata")
    campos = {nome(item): item for item in metadata}
    exigir(campos["id"].text == identidade and campos["version"].text == VERSAO)
    if not simbolos:
        exigir(campos["license"].get("type") == "expression" and campos["license"].text == "MIT")
    else:
        # O formato snupkg oficial retém somente metadados de símbolos e dependências.
        exigir(any(item.get("name") == "SymbolsPackage" for item in campos.get("packageTypes", [])))
    frameworks = []
    dependencias = []
    for item in campos.get("dependencies", []):
        frameworks.append(item.get("targetFramework"))
        for dep in item:
            dependencias.append({"id": dep.get("id"), "version": dep.get("version"),
                                 "targetFramework": item.get("targetFramework")})
    return frameworks, dependencias


def validar_pacote(caminho, simbolos=False, identidade=PACOTE):
    arquivos = {}
    with zipfile.ZipFile(caminho) as pacote:
        exigir(pacote.testzip() is None)
        exigir(len(pacote.namelist()) == len(set(pacote.namelist())))
        frameworks, dependencias = metadados_nuget(pacote, identidade, simbolos)
        for item in pacote.infolist():
            nome = item.filename
            exigir(not nome.startswith("/") and "\\" not in nome and ".." not in nome.split("/"))
            exigir(not any(parte in ("testes", ".git", "dist", "obj", "bin") for parte in nome.split("/")))
            exigir(not nome.endswith((".key", ".pem")) and not nome.startswith(".env"))
            if item.is_dir():
                continue
            exigir(item.file_size < 32 * 1024 * 1024)
            conteudo = pacote.read(nome)
            arquivos[nome] = sha256(conteudo)
            if nome.endswith(".dll"):
                exigir(not simbolos and nome.endswith("/" + identidade + ".dll") and conteudo[:2] == b"MZ")
        if simbolos:
            exigir(any(nome.endswith("/" + identidade + ".pdb") for nome in arquivos))
            exigir(not any(nome.endswith(".dll") for nome in arquivos))
        else:
            exigir(any(nome.endswith("/" + identidade + ".dll") for nome in arquivos))
            exigir(any(nome.endswith("/" + identidade + ".xml") for nome in arquivos))
            licenca = (SDK / "LICENSE").read_bytes()
            exigir(any(pacote.read(nome) == licenca for nome in arquivos if nome.rsplit("/", 1)[-1].upper().startswith("LICENSE")))
            exigir("README.md" in arquivos)
    return {"arquivos": arquivos, "frameworks": frameworks, "dependencias": dependencias}


def preparar(destino, cache, projeto):
    global ETAPA
    destino.mkdir(parents=True, exist_ok=True)
    ETAPA = "empacotar"
    projetos = [(PACOTE, projeto)]
    for identidade in ("Mailcore.FluentEmail", "Mailcore.AspNetCore"):
        addon = SDK / "src" / identidade / (identidade + ".csproj")
        if addon.is_file():
            projetos.append((identidade, addon))
    pacotes = {}
    with tempfile.TemporaryDirectory(prefix="mailcore-dotnet-pacotes-") as temporario:
        pasta = Path(temporario)
        for identidade, caminho_projeto in projetos:
            ETAPA = "empacotar_" + identidade
            executar(["dotnet", "pack", str(caminho_projeto), "-c", "Release", "--no-restore",
                      "--output", str(pasta), "-p:ContinuousIntegrationBuild=true"])
            esperado = pasta / (identidade + "." + VERSAO + ".nupkg")
            simbolos = pasta / (identidade + "." + VERSAO + ".snupkg")
            exigir(esperado.is_file() and simbolos.is_file())
            ETAPA = "inspecionar_" + identidade
            dados = validar_pacote(esperado, identidade=identidade)
            dados_simbolos = validar_pacote(simbolos, simbolos=True, identidade=identidade)
            artefatos = {}
            for tipo, origem in (("pacote", esperado), ("simbolos", simbolos)):
                conteudo = origem.read_bytes()
                shutil.copyfile(origem, destino / origem.name)
                soma = sha256(conteudo)
                (destino / (origem.name + ".sha256")).write_text(soma + "\n")
                artefatos[tipo] = {"arquivo": origem.name, "sha256": soma, "bytes": len(conteudo)}
            pacotes[identidade] = {"artefatos": artefatos, "arquivos_sdk": dados["arquivos"],
                "arquivos_simbolos": dados_simbolos["arquivos"], "frameworks": dados["frameworks"],
                "dependencias": dados["dependencias"]}
    principal = pacotes[PACOTE]
    artefatos = principal["artefatos"]
    ETAPA = "preparar_feed_publico"
    feed = destino / "feed"
    feed.mkdir(parents=True, exist_ok=True)
    exigir(cache.is_dir())
    for arquivo in sorted(cache.rglob("*.nupkg")):
        exigir(not arquivo.is_symlink())
        alvo = feed / arquivo.name
        if alvo.exists():
            exigir(sha256(alvo.read_bytes()) == sha256(arquivo.read_bytes()))
        else:
            shutil.copyfile(arquivo, alvo)
    for dados_pacote in pacotes.values():
        item = dados_pacote["artefatos"]["pacote"]
        shutil.copyfile(destino / item["arquivo"], feed / item["arquivo"])
    exigir(len(list(feed.glob("*.nupkg"))) > 1)
    digest_feed, arquivos_feed = inventariar(feed)
    manifesto = {"resultado": "preparado", "data_utc": datetime.now(timezone.utc).isoformat(),
                 "pacote": PACOTE, "versao": VERSAO, "artefatos": artefatos,
                 "arquivos_sdk": principal["arquivos_sdk"], "arquivos_simbolos": principal["arquivos_simbolos"],
                 "frameworks": principal["frameworks"], "dependencias": principal["dependencias"], "pacotes": pacotes,
                 "sha256_feed": digest_feed, "arquivos_feed": arquivos_feed,
                 "dependencias_fora_dll_sdk": True, "licenca_sdk_preservada": True,
                 "cache_sem_configuracoes_credenciais_host": True, "publicado": False}
    (destino / "manifesto.json").write_text(json.dumps(manifesto, ensure_ascii=False, indent=2) + "\n")
    for nome in ("LICENSE", "README.md", "CHANGELOG.md", "SECURITY.md"):
        shutil.copyfile(SDK / nome, destino / nome)
    return {"resultado": "preparado", "pacote": PACOTE, "versao": VERSAO,
            "sha256_pacote": artefatos["pacote"]["sha256"], "arquivos_sdk": len(principal["arquivos_sdk"]),
            "pacotes_preparados": list(pacotes), "arquivos_feed": len(arquivos_feed), "publicado": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destino", type=Path, default=Path("/pacotes"))
    parser.add_argument("--cache", type=Path, default=Path(os.environ.get("NUGET_PACKAGES", "/opt/nuget")))
    parser.add_argument("--projeto", type=Path, default=SDK / "src/Mailcore/Mailcore.csproj")
    opcoes = parser.parse_args()
    try:
        resultado = preparar(opcoes.destino.resolve(), opcoes.cache.resolve(), opcoes.projeto.resolve())
    except Exception:
        resultado = {"resultado": "falhou", "etapa": ETAPA,
                     "falha": "Não foi possível preparar o pacote local; detalhes privados omitidos."}
    print(json.dumps(resultado, ensure_ascii=False))
    return 0 if resultado["resultado"] == "preparado" else 1


if __name__ == "__main__":
    sys.exit(main())

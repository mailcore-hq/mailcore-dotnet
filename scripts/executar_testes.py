#!/usr/bin/env python3
"""Executa VSTest com resultados TRX e confirma os totais antes de emitir o resumo."""
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

framework = os.environ.get("MAILCORE_FRAMEWORK", "net11.0")
if framework not in {"net8.0", "net9.0", "net10.0", "net11.0"}:
    sys.exit("Framework de verificação inválido.")
saida = Path("/tmp/mailcore-resultados")
saida.mkdir(parents=True, exist_ok=True)
retorno = subprocess.run(["dotnet", "vstest", f"tests/Mailcore.Tests/bin/Release/{framework}/Mailcore.Tests.dll",
                          "--logger:trx;LogFileName=resultado.trx", f"--ResultsDirectory:{saida}"])
if retorno.returncode:
    sys.exit(retorno.returncode)
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
raiz = ET.parse(saida / "resultado.trx").getroot()
totais = raiz.find("t:ResultSummary/t:Counters", ns)
if totais is None or int(totais.get("total", "0")) == 0 or int(totais.get("passed", "0")) != int(totais.get("total", "0")):
    sys.exit("VSTest não confirmou a aprovação de todos os casos.")
print(json.dumps({"resultado": "aprovado", "framework": framework,
                  "testes": int(totais.get("total")), "falhas": 0, "ignorados": 0}, ensure_ascii=False))

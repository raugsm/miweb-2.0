from __future__ import annotations

from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def main() -> int:
    doc = read_text(REPO / "docs/ARIADGSM_PRODUCT_SHELL_FINAL_DESIGN.md")
    form = read_text(REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/MainForm.cs")

    assert "Product Shell visual final" in doc
    assert "Microsoft Learn, Progress controls" in doc
    assert "Microsoft Research, Human-AI interaction guidelines" in doc
    assert "Google PAIR Guidebook" in doc
    assert "errores deben ser accionables" in doc.lower()
    assert "la app solo se minimiza cuando no hay errores ni avisos visibles" in doc.lower()

    assert "private readonly Label _safetyStatusLabel" in form
    assert 'BuildMetricCard("Seguridad"' in form
    assert '"Area de la IA"' in form
    assert '"Que significa para operar"' in form
    assert '"Lo que la IA hizo"' in form
    assert '"Caja negra de la IA"' not in form

    assert "BuildVisibleHealthItems" in form
    assert '"Cabina WhatsApp"' in form
    assert '"Ojos y lectura"' in form
    assert '"Cerebro y memoria"' in form
    assert '"Contabilidad"' in form
    assert '"Seguridad"' in form
    assert '"Manos"' in form
    assert '"Nube y panel"' in form

    assert "Math.Max(_cabinSetupMaxProgress, value)" in form
    assert "var hasVisibleProblems = HasVisibleProblems();" in form
    assert "if (autonomous && !hasVisibleProblems)" in form
    assert "WindowState = FormWindowState.Minimized;" in form
    assert "Para detalle completo usa Reporte o Historial." in form
    assert "HumanSafetyMetric" in form

    print("product shell visual final OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

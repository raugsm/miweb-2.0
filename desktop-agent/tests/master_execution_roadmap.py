from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.architecture import MASTER_STAGES, describe_master_stages


EXPECTED_STAGES = [
    "Execution Lock",
    "Domain Event Contracts",
    "Autonomous Cycle Orchestrator",
    "Case Manager",
    "Channel Routing Brain",
    "Accounting Core evidence-first",
    "Product Shell",
    "Cabin Authority",
    "Safe Eyes / Reader Core",
    "Living Memory",
    "Business Brain",
    "Trust & Safety + Input Arbiter",
    "Hands & Verification",
    "Tool Registry",
    "Cloud Sync / ariadgsm.com",
    "Evaluation + Release",
]


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def main() -> int:
    roadmap = read_text(REPO / "docs/ARIADGSM_MASTER_EXECUTION_ROADMAP.md")
    lock = read_text(REPO / "docs/ARIADGSM_EXECUTION_LOCK.md")
    stage_zero = read_text(REPO / "docs/ARIADGSM_STAGE_0_PRODUCT_FOUNDATION.md")

    assert [stage.name for stage in MASTER_STAGES] == EXPECTED_STAGES
    assert [stage["name"] for stage in describe_master_stages()] == EXPECTED_STAGES
    assert MASTER_STAGES[8].status == "closed_reader_core_base"
    assert MASTER_STAGES[9].status == "closed_living_memory_base"
    assert MASTER_STAGES[10].status == "closed_business_brain_base"
    assert MASTER_STAGES[11].status == "next_pending"

    for index, name in enumerate(EXPECTED_STAGES):
        expected_line = f"{index}. {name}"
        assert expected_line in roadmap, expected_line
        assert expected_line in lock, expected_line
        assert expected_line in stage_zero, expected_line

    assert "docs/ARIADGSM_MASTER_EXECUTION_ROADMAP.md" in lock
    assert "### 6.8 Safe Eyes / Reader Core" in lock
    assert "docs/ARIADGSM_SAFE_EYES_READER_CORE_DESIGN.md" in lock
    assert "### 6.9 Living Memory" in lock
    assert "docs/ARIADGSM_LIVING_MEMORY_DESIGN.md" in lock
    assert "### 6.10 Business Brain" in lock
    assert "docs/ARIADGSM_BUSINESS_BRAIN_DESIGN.md" in lock
    assert "Etapa 11: Trust & Safety + Input Arbiter" in lock
    assert "Updater final" not in lock[lock.index("## 10. Siguiente bloque activo") :]
    assert "Evaluation + Release" in roadmap
    assert "updater final" in roadmap.lower()

    print("master execution roadmap OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

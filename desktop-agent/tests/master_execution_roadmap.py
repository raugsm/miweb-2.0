from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.architecture import MASTER_STAGES, describe_master_stages


EXPECTED_STAGES = [
    "Execution Lock",
    "Runtime Kernel",
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

EXPECTED_STAGE_LINES = [
    "0. Execution Lock",
    "0.5. Runtime Kernel",
    "1. Domain Event Contracts",
    "2. Autonomous Cycle Orchestrator",
    "3. Case Manager",
    "4. Channel Routing Brain",
    "5. Accounting Core evidence-first",
    "6. Product Shell",
    "7. Cabin Authority",
    "8. Safe Eyes / Reader Core",
    "9. Living Memory",
    "10. Business Brain",
    "11. Trust & Safety + Input Arbiter",
    "12. Hands & Verification",
    "13. Tool Registry",
    "14. Cloud Sync / ariadgsm.com",
    "15. Evaluation + Release",
]


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def main() -> int:
    roadmap = read_text(REPO / "docs/ARIADGSM_MASTER_EXECUTION_ROADMAP.md")
    lock = read_text(REPO / "docs/ARIADGSM_EXECUTION_LOCK.md")
    stage_zero = read_text(REPO / "docs/ARIADGSM_STAGE_0_PRODUCT_FOUNDATION.md")

    assert [stage.name for stage in MASTER_STAGES] == EXPECTED_STAGES
    assert [stage["name"] for stage in describe_master_stages()] == EXPECTED_STAGES
    assert MASTER_STAGES[1].number == "0.5"
    assert MASTER_STAGES[1].status == "closed_runtime_kernel_final"
    assert MASTER_STAGES[9].status == "closed_reader_core_base"
    assert MASTER_STAGES[10].status == "closed_living_memory_base"
    assert MASTER_STAGES[11].status == "closed_business_brain_base"
    assert MASTER_STAGES[12].status == "closed_trust_safety_input_arbiter_final"
    assert MASTER_STAGES[13].status == "closed_hands_verification_final"
    assert MASTER_STAGES[14].status == "closed_tool_registry_final"
    assert MASTER_STAGES[15].status == "next_pending"

    for expected_line in EXPECTED_STAGE_LINES:
        assert expected_line in roadmap, expected_line
        assert expected_line in lock, expected_line
        assert expected_line in stage_zero, expected_line

    assert "docs/ARIADGSM_MASTER_EXECUTION_ROADMAP.md" in lock
    assert "### 6.0.5 Runtime Kernel" in lock
    assert "docs/ARIADGSM_RUNTIME_KERNEL_FINAL.md" in lock
    assert "### 6.8 Safe Eyes / Reader Core" in lock
    assert "docs/ARIADGSM_SAFE_EYES_READER_CORE_DESIGN.md" in lock
    assert "### 6.9 Living Memory" in lock
    assert "docs/ARIADGSM_LIVING_MEMORY_DESIGN.md" in lock
    assert "### 6.10 Business Brain" in lock
    assert "docs/ARIADGSM_BUSINESS_BRAIN_DESIGN.md" in lock
    assert "### 6.11 Trust & Safety + Input Arbiter" in lock
    assert "docs/ARIADGSM_TRUST_SAFETY_INPUT_ARBITER_FINAL.md" in lock
    assert "### 6.12 Hands & Verification" in lock
    assert "docs/ARIADGSM_HANDS_VERIFICATION_FINAL.md" in lock
    assert "### 6.13 Tool Registry" in lock
    assert "docs/ARIADGSM_TOOL_REGISTRY_FINAL.md" in lock
    assert "Etapa 14: Cloud Sync / ariadgsm.com" in lock
    assert "Updater final" not in lock[lock.index("## 10. Siguiente bloque activo") :]
    assert "Evaluation + Release" in roadmap
    assert "updater final" in roadmap.lower()

    print("master execution roadmap OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

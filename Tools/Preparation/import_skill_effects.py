import hashlib
import json
import re
import shutil
from pathlib import Path

SOURCE_ROOT = Path(r"E:\Project\PELibDemo\Assets\Art\3D\Hovl Studio")
PROJECT_ROOT = Path(__file__).resolve().parents[2]
DEST_ROOT = PROJECT_ROOT / "Assets" / "LittleGame" / "Art" / "SkillEffectsSources" / "HovlStudio"
REPORT_PATH = PROJECT_ROOT / "Reports" / "Art" / "skill-effects-migration.json"

SELECTED_PREFABS = [
    r"AOE Magic spells Vol.1\Prefabs\Lightning strike.prefab",
    r"AOE Magic spells Vol.1\Prefabs\Lightning hit.prefab",
    r"AOE Magic spells Vol.1\Prefabs\Ise attack.prefab",
    r"AOE Magic spells Vol.1\Prefabs\Energy explosion.prefab",
    r"AOE Magic spells Vol.1\Prefabs\Flower slash.prefab",
    r"AOE Magic spells Vol.1\Prefabs\Leaves buff.prefab",
    r"Magic circles\Prefabs\Loop version\Magic circle 1 loop.prefab",
    r"Magic circles\Prefabs\Loop version\Magic shield 1 loop.prefab",
    r"RPG VFX Bundle\Prefabs\Magic buffs and hits\Buff 1.prefab",
    r"RPG VFX Bundle\Prefabs\Magic buffs and hits\Heal.prefab",
    r"RPG VFX Bundle\Prefabs\Magic buffs and hits\Punch Hit.prefab",
    r"RPG VFX Bundle\Prefabs\Magic buffs and hits\Soft blue buff.prefab",
    r"AAA Projectiles Vol 1\Prefabs\Projectiles(transform)\Projectile 1.prefab",
    r"AAA Projectiles Vol 1\Prefabs\Projectiles(transform)\Projectile 4.prefab",
    r"AAA Projectiles Vol 1\Prefabs\Projectiles(transform)\Projectile 7.prefab",
    r"AAA Projectiles Vol 1\Prefabs\Hit 1.prefab",
    r"AAA Projectiles Vol 1\Prefabs\Hit 4.prefab",
    r"AAA Projectiles Vol 1\Prefabs\Hit 7.prefab",
]

GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")
TEXT_EXTS = {".prefab", ".mat", ".anim", ".controller", ".asset", ".shader"}


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def build_guid_index(root: Path) -> dict[str, Path]:
    index: dict[str, Path] = {}
    for meta in root.rglob("*.meta"):
        asset = meta.with_suffix("")
        if not asset.is_file():
            continue
        try:
            head = meta.read_text(encoding="utf-8", errors="ignore")[:512]
        except OSError:
            continue
        m = GUID_RE.search(head)
        if m:
            index[m.group(1)] = asset
    return index


def collect_dependencies(seeds: list[Path], guid_index: dict[str, Path]) -> list[Path]:
    visited: set[Path] = set()
    queue = list(seeds)
    while queue:
        cur = queue.pop(0)
        if cur in visited or not cur.is_file():
            continue
        # 跳过 .cs 源演示脚本，只提取资产与 Shader
        if cur.suffix.lower() == ".cs":
            continue
        visited.add(cur)
        if cur.suffix.lower() in TEXT_EXTS:
            text = cur.read_text(encoding="utf-8", errors="ignore")
            for guid in set(GUID_RE.findall(text)):
                dep = guid_index.get(guid)
                if dep and dep not in visited and dep.suffix.lower() != ".cs":
                    queue.append(dep)
    return sorted(visited)


def main():
    if not SOURCE_ROOT.exists():
        raise FileNotFoundError(f"Source directory not found: {SOURCE_ROOT}")

    guid_index = build_guid_index(SOURCE_ROOT)
    seeds = [SOURCE_ROOT / rel for rel in SELECTED_PREFABS]
    missing = [str(p) for p in seeds if not p.exists()]
    if missing:
        raise FileNotFoundError(f"Missing selected prefabs: {missing}")

    deps = collect_dependencies(seeds, guid_index)
    records = []
    for src in deps:
        rel = src.relative_to(SOURCE_ROOT)
        dst = DEST_ROOT / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        src_meta = Path(str(src) + ".meta")
        if src_meta.exists():
            shutil.copy2(src_meta, Path(str(dst) + ".meta"))
        records.append({
            "relativePath": str(rel).replace("\\", "/"),
            "sourceSha256": sha256_file(src),
            "size": src.stat().st_size,
        })

    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    report = {
        "sourceRoot": str(SOURCE_ROOT),
        "destRoot": str(DEST_ROOT),
        "selectedPrefabCount": len(seeds),
        "extractedFileCount": len(records),
        "files": records,
    }
    REPORT_PATH.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Extracted {len(records)} files for {len(seeds)} Hovl Studio prefabs -> {DEST_ROOT}")


if __name__ == "__main__":
    main()

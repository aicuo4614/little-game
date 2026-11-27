"""提取第一关使用的 KayKit 角色、动作和场景资源，保留包内许可。"""
from pathlib import Path, PurePosixPath
import zipfile

ROOT = Path(__file__).resolve().parents[2]
VAULT = Path("E:/Art/material-vault/libraries/kaykit/downloads")
DEST = ROOT / "Assets/LittleGame/Art/UnitSources/KayKit"
ADVENTURERS = {"Knight.fbx", "Ranger.fbx", "Mage.fbx", "Rogue.fbx", "knight_texture.png",
               "ranger_texture.png", "mage_texture.png", "rogue_texture.png"}
SKELETONS = {"Skeleton_Warrior.fbx", "Skeleton_Minion.fbx", "Skeleton_Mage.fbx", "Skeleton_Rogue.fbx",
             "skeleton_texture.png"}
ANIMATIONS = {f"Rig_Medium_{name}.fbx" for name in ("General", "MovementBasic", "CombatMelee", "CombatRanged")}


def extract(archive, directory, match_path, names):
    target = DEST / directory
    target.mkdir(parents=True, exist_ok=True)
    found = set()
    with zipfile.ZipFile(VAULT / archive) as bundle:
        for entry in bundle.namelist():
            path = PurePosixPath(entry)
            if path.name.lower() == "license.txt" and len(path.parts) == 2:
                (target / "LICENSE.txt").write_bytes(bundle.read(entry))
            if path.name in names and match_path in entry:
                (target / path.name).write_bytes(bundle.read(entry))
                found.add(path.name)
    if found != names:
        raise RuntimeError(f"{archive} 缺少文件：{names - found}")


extract("adventurers-free-2.0.zip", "Adventurers", "/Characters/fbx/", ADVENTURERS)
extract("skeletons-free-1.1.zip", "Skeletons", "/characters/fbx/", SKELETONS)
extract("skeletons-free-1.1.zip", "Skeletons", "/assets/fbx(unity)/", {"Skeleton_Crossbow.fbx", "Skeleton_Quiver.fbx"})
extract("character-animations-free-1.1.zip", "Animation", "/Animations/fbx/Rig_Medium/", ANIMATIONS)

scenery = ROOT / "Assets/LittleGame/Resources/Courtyard/KayKit"
scenery.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(VAULT / "dungeon-remastered-github-1.0.zip") as bundle:
    for name in ("pillar_decorated.obj", "wall_doorway.obj", "wall_broken.obj"):
        source = next(path for path in bundle.namelist() if path.endswith("/obj/" + name))
        lines = bundle.read(source).decode("utf-8").splitlines()
        # 使用本项目的道路材质。保留顶点、面、法线与 UV。
        (scenery / name).write_text("\n".join(line for line in lines if not line.startswith(("mtllib ", "usemtl "))) + "\n", encoding="utf-8")
    license_path = next(path for path in bundle.namelist() if path.endswith("/Assets/LICENSE.txt"))
    (scenery / "Dungeon-LICENSE.txt").write_bytes(bundle.read(license_path))
with zipfile.ZipFile(VAULT / "forest-nature-free.zip") as bundle:
    for name in ("Tree_1_A_Color1.fbx", "forest_texture.png"):
        source = next(path for path in bundle.namelist() if path.endswith("/fbx(unity)/" + name))
        (scenery / name).write_bytes(bundle.read(source))
    license_path = next(path for path in bundle.namelist() if path.lower().endswith("/license.txt"))
    (scenery / "Forest-LICENSE.txt").write_bytes(bundle.read(license_path))

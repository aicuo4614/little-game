"""直接迁入 Demo 的独立能力与已缓存美术；不修改来源工程。"""
from pathlib import Path
import hashlib, json, shutil, zipfile

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('F:/Git/kabu_mobile/demo/kabu-unity-mcp')
records = []
if (ROOT / 'Reports/Combat/source-migration.json').exists():
    raise RuntimeError('首次迁入已经完成。为保护本工程MCP改动，不允许直接重复覆盖；后续请按来源变更逐项合并。')
for src in SOURCE.rglob('*'):
    rel = src.relative_to(SOURCE)
    if not src.is_file() or any(p in {'.git', 'bin', 'obj', 'node_modules'} for p in rel.parts):
        continue
    dst = ROOT / 'Packages/com.kabu.unity-mcp' / rel
    digest = hashlib.sha256(src.read_bytes()).hexdigest()
    if dst.exists() and hashlib.sha256(dst.read_bytes()).hexdigest() == digest:
        continue
    if dst.exists():
        backup = ROOT / 'Archive~/McpPrior' / rel
        backup.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(dst, backup)
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    records.append({'path': str(rel), 'sha256': digest})

archive = Path('E:/Art/material-vault/libraries/kaykit/downloads/adventurers-free-2.0.zip')
with zipfile.ZipFile(archive) as z:
    license_text = z.read('KayKit_Adventurers_2.0_FREE/License.txt').decode('utf-8-sig')
    if 'CC0' not in license_text:
        raise RuntimeError('预期许可不符')
    art = ROOT / 'Assets/LittleGame/Resources/CombatCharacters'
    art.mkdir(parents=True, exist_ok=True)
    wanted = {'Knight.fbx', 'Mage.fbx', 'Ranger.fbx', 'Barbarian.fbx', 'knight_texture.png', 'mage_texture.png', 'ranger_texture.png', 'barbarian_texture.png'}
    for n in z.namelist():
        if '/Characters/fbx/' in n and Path(n).name in wanted:
            (art / Path(n).name).write_bytes(z.read(n))
    for n in z.namelist():
        if '/Animations/fbx/Rig_Medium/' in n and n.endswith('.fbx'):
            (art / Path(n).name).write_bytes(z.read(n))
    (art / 'LICENSE.txt').write_text(license_text, encoding='utf-8')
report = ROOT / 'Reports/Combat/source-migration.json'
report.parent.mkdir(parents=True, exist_ok=True)
report.write_text(json.dumps({'mcpSource': str(SOURCE), 'changed': records, 'artArchive': str(archive), 'artSha256': hashlib.sha256(archive.read_bytes()).hexdigest()}, ensure_ascii=False, indent=2),encoding='utf-8')
print(f'MCP更新 {len(records)} 个文件；已提取4个角色及Medium骨架动画，许可 CC0')

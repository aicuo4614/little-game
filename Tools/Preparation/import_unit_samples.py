"""只读提取已授权单位样品；保留来源 GUID，不修改外部工程。"""
from pathlib import Path
import hashlib
import json
import re
import shutil

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'Assets/LittleGame/Art/UnitSources'
PE = Path('E:/Project/PELibDemo/Assets/Art/3D')
AUTO = Path('E:/Project/AutoClash_20210125/AutoClash1/Assets/Assets/Characters')
records = []


def copy(src, dst, rewrite=False):
    dst.parent.mkdir(parents=True, exist_ok=True)
    data = src.read_bytes()
    if rewrite and src.suffix == '.prefab':
        # 源演示控制器含大量演示动作；本项目使用独立的表现播放器。
        data = re.sub(rb'm_Controller: \{[^\r\n]+\}', b'm_Controller: {fileID: 0}', data)
    if rewrite and src.suffix == '.mat':
        data = re.sub(rb'm_Shader: \{[^\r\n]+\}', b'm_Shader: {fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}', data)
    if not dst.exists() or dst.read_bytes() != data:
        dst.write_bytes(data)
    meta = Path(str(src) + '.meta')
    if meta.exists() and not Path(str(dst) + '.meta').exists():
        shutil.copy2(meta, Path(str(dst) + '.meta'))
    records.append({'source': str(src), 'destination': str(dst.relative_to(ROOT)),
                    'sourceSha256': hashlib.sha256(src.read_bytes()).hexdigest(),
                    'destinationSha256': hashlib.sha256(data).hexdigest()})


def main():
    pack = PE / 'RPGTinyHeroWavePBR'
    # 角色的预制体引用头部/武器子预制体；编辑器随后烘焙时剔除未选装配。
    for folder in ['Mesh', 'Prefab', 'Material', 'Texture/DefaultPBR']:
        for src in (pack / folder).rglob('*'):
            if src.is_file() and src.suffix != '.meta':
                copy(src, DEST / 'TinyHero' / src.relative_to(pack), True)
    for style, idle, attack in [('SwordAndShield', 'Idle_Battle_SwordAndShiled', 'Attack01_SwordAndShiled'),
                                ('BowAndArrow', 'Idle_Battle_BowAndArrow', 'Attack01_BowAndArrow'),
                                ('MagicWand', 'Idle_Battle_MagicWand', 'Attack01_MagicWand')]:
        files = [idle, attack, 'GetHit01_' + style, 'Die01_' + style]
        for name in files:
            src = pack / 'Animation' / style / (name + '.fbx')
            copy(src, DEST / 'TinyHero' / src.relative_to(pack))
        src = next((pack / 'Animation' / style / 'InPlace').glob('MoveFWD_Battle_InPlace*.fbx'))
        copy(src, DEST / 'TinyHero' / src.relative_to(pack))
    wolf = PE / 'Monsters Ultimate Pack 02 Cute Series/T/Wolf Cute Series'
    for name in ['Wolf', 'Wolf@Idle', 'Wolf@Run Forward In Place', 'Wolf@Bite Attack', 'Wolf@Take Damage', 'Wolf@Die']:
        src = wolf / 'FBX' / (name + '.FBX')
        copy(src, DEST / 'Wolf' / src.name)
    copy(wolf / 'Textures/Wolf.psd', DEST / 'Wolf/Wolf.psd')
    golem = AUTO / '052_Golem_StoneGolem'
    for name in ['GolemMesh', 'IdleBattle_Golem_Anim', 'Walk_Golem_Anim', 'Attack01_Golem_Anim', 'Attack02_Golem_Anim', 'GetHit_Golem_Anim', 'Die_Golem_Anim']:
        src = golem / (name + '.fbx')
        copy(src, DEST / 'Golem' / src.name)
    copy(AUTO / '000_Texture&Material/Texture_Character_Enemy.png', DEST / 'Golem/Texture_Character_Enemy.png')
    copy(AUTO / '000_Texture&Material/PolyartDefaultTex.png', DEST / 'Golem/PolyartDefaultTex.png')
    demo = Path('F:/Git/kabu_mobile/demo')
    shader_source = demo / 'client-demo-assets/Assets/Art/Shaders/Roco'
    for name in ['Roco_Character.shader', 'RocoCharacter.hlsl', 'RocoCharacterOutline.hlsl']:
        src = shader_source / name
        dst = ROOT / 'Assets/LittleGame/Shaders/Roco' / name
        copy(src, dst)
        if name.endswith('.shader'):
            dst.write_text(dst.read_text(encoding='utf-8-sig').replace('    CustomEditor "RocoCharacterShaderGUI"', ''), encoding='utf-8')
            records[-1]['adaptation'] = '移除Demo专用材质GUI，使用本项目资源构建器配置；着色逻辑保留'
        if name == 'RocoCharacterOutline.hlsl':
            text = dst.read_text(encoding='utf-8-sig').replace('float worldPerPixel = (2.0 * viewDepth)', 'float projectionDepth = lerp(viewDepth, 1.0, unity_OrthoParams.w);\n    float worldPerPixel = (2.0 * projectionDepth)')
            dst.write_text(text, encoding='utf-8')
            records[-1]['adaptation'] = '正交镜头像素描边线宽不乘视深，透视行为保留'
    src = demo / 'client-demo/Assets/Scripts/client-shared/Rendering/Runtime/Outline/RocoCharacterOutlineFeature.cs'
    dst = ROOT / 'Assets/LittleGame/Client/Presentation/RocoCharacterOutlineFeature.cs'
    copy(src, dst)
    dst.write_text(dst.read_text(encoding='utf-8-sig').replace('RocoMeshOutline.RefreshShaderGlobalMaxCameraDistance();', 'Shader.SetGlobalFloat("_RocoOutlineMaxCameraDistance", 0);'), encoding='utf-8')
    records[-1]['adaptation'] = '移除Demo整块网格描边配置依赖；当前正交战场不限制描边距离'
    for record in records:
        record['destinationSha256'] = hashlib.sha256((ROOT / record['destination']).read_bytes()).hexdigest()
    output = ROOT / 'Reports/Art/unit-source-migration.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({'date': '2026-11-24', 'authorization': '用户授权PELibDemo模型及AutoClash资源用于本项目', 'assets': records}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'已提取 {len(records)} 个源文件；源工程保持只读。', flush=True)


if __name__ == '__main__':
    main()

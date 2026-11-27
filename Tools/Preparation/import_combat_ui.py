"""提取用户授权的 PELibDemo 奇幻UI散件，保留原图和九宫格边距。"""
from pathlib import Path
import hashlib
import json
import re
import uuid

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('E:/Project/PELibDemo/Assets/Art/3RD_UI/GUI PRO Kit/GUI PRO Kit - Fantasy RPG/ResourcesData/Sprites')
FILES = {
    'Human': 'Component/Icon_PictoIcons_(Original)/btn_icon_human.png',
    'Undead': 'Component/Icon_PictoIcons_(Original)/btn_icon_skull.png',
    'Boss': 'Component/Icon_PictoIcons_(Original)/btn_icon_skull.png',
    'Population': 'Component/Icon_PictoIcons_(Original)/btn_icon_user_1.png',
    'Panel': 'Component/Popup/popup_00_frame.png',
    'Card': 'Component/Frame/frame_iconframe_square_01_Demo_n.png',
    'CardGold': 'Component/Frame/frame_itemframe_00_frame_gold.png',
    'CardBack': 'Component/Frame/frame_itemframe_00_back.png',
    'Divider': 'Component/Frame/frame_tabmenu_focus_01_Demo.png',
    'ButtonGold': 'Component/Button/btn_rectangle_01_n_yellow.png',
    'ButtonGreen': 'Component/Button/btn_rectangle_01_n_green.png',
    'ButtonBlue': 'Component/Button/btn_rectangle_01_n_blue.png',
    'ButtonGray': 'Component/Button/btn_rectangle_01_n_bluegray.png',
    'ButtonDark': 'Component/Button/btn_rectangle_01_n_dark.png',
    'Shield': 'Component/Icon_ItemIcons_(Original)/icon_itemicon_shield.png',
    'Archer': 'Component/Icon_ItemIcons_(Original)/icon_itemicon_arrow.png',
    'Storm': 'Component/Icon_ItemIcons_(Original)/icon_itemicon_energy_purple.png',
    'Castle': 'Component/Icon_ItemIcons_(Original)/icon_itemicon_castle.png',
    'Book': 'Component/Icon_ItemIcons_(Original)/icon_itemicon_book_magic.png',
    'Helmet': 'Component/Icon_ItemIcons_(Original)/icon_itemicon_helmet.png',
    'Frost': 'Component/Icon_PictoIcons_(Original)/btn_icon_snowflake.png',
    'Assassin': 'Component/Icon_PictoIcons_(Original)/btn_icon_dagger.png',
    'Target': 'Component/Icon_PictoIcons_(Original)/btn_icon_target_1.png',
    'Protect': 'Component/Icon_PictoIcons_(Original)/btn_icon_shield_magic.png',
    'Restart': 'Component/Icon_PictoIcons_(Original)/btn_icon_refresh.png',
    'Star': 'Demo/Demo_Icon/icon_star_grade_s.png',
}


def main():
    dest = ROOT / 'Assets/LittleGame/Resources/CombatUI'
    dest.mkdir(parents=True, exist_ok=True)
    records = []
    for alias, relative in FILES.items():
        src = SOURCE / relative
        dst = dest / (alias + '.png')
        data = src.read_bytes()
        if not dst.exists() or dst.read_bytes() != data: dst.write_bytes(data)
        original = Path(str(src) + '.meta').read_text(encoding='utf-8-sig')
        # 只加载整图Single Sprite；原多图模式的唯一切片具有有效边距。
        border = re.findall(r'(?:spriteBorder|border): (\{[^}]+\})', original)[-1]
        meta = re.sub(r'spriteMode: \d+', 'spriteMode: 1', original)
        meta = re.sub(r'spriteBorder: \{[^}]+\}', 'spriteBorder: ' + border, meta)
        if not Path(str(dst) + '.meta').exists():
            meta = re.sub(r'(?m)^guid: [a-f0-9]+', 'guid: ' + uuid.uuid4().hex, meta)
            Path(str(dst) + '.meta').write_text(meta, encoding='utf-8')
        records.append({'alias': alias, 'source': str(src), 'destination': str(dst.relative_to(ROOT)), 'sha256': hashlib.sha256(data).hexdigest(), 'border': border})
    report = ROOT / 'Reports/Art/ui-source-migration.json'
    report.write_text(json.dumps({'authorization': '2026-11-24用户明确授权使用PELibDemo Art内UI并整体改造', 'sprites': records}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'已提取 {len(records)} 个UI散件，未迁入源演示业务或修改源工程。')


if __name__ == '__main__': main()



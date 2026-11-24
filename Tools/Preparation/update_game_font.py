"""从 OFL 字体源生成覆盖全部运行时中文的静态字体子集。"""
from pathlib import Path
from urllib.request import urlretrieve
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools import subset

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Library' / 'FontSource' / 'NotoSansSC.ttf'

def main():
    SOURCE.parent.mkdir(parents=True, exist_ok=True)
    if not SOURCE.exists():
        urlretrieve('https://raw.githubusercontent.com/google/fonts/main/ofl/notosanssc/NotoSansSC%5Bwght%5D.ttf', SOURCE)
    font = TTFont(SOURCE)
    if 'fvar' in font:
        font = instantiateVariableFont(font, {'wght': 400}, inplace=True)
    text = ''.join(p.read_text(encoding='utf-8-sig') for p in (ROOT / 'Assets/LittleGame/Client').rglob('*.cs'))
    text += ''.join(chr(i) for i in range(32, 127))
    missing = sorted({c for c in text if ord(c) > 31 and ord(c) not in font.getBestCmap()})
    if missing:
        raise RuntimeError(f'字体源缺字：{missing}')
    options = subset.Options()
    options.name_IDs = ['*']
    sub = subset.Subsetter(options=options)
    sub.populate(text=text)
    sub.subset(font)
    font.save(ROOT / 'Assets/LittleGame/Resources/GameFont.ttf')
    print(f'字体已更新，覆盖 {len(set(text))} 个字符')

if __name__ == '__main__':
    main()

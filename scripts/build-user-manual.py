"""Build the user manual PDF from the reviewed Markdown and captured UI images.

Requires Windows Calibri fonts, reportlab and Pillow. Run from any directory
with Python. The Markdown supplies the edition date; Version.txt supplies the
PDF footer version.

Pass --quick to build the shorter guide from docs/quick-guide.md.
"""
import argparse
from pathlib import Path
import re
from xml.sax.saxutils import escape

from PIL import Image as PILImage
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle, Image, KeepTogether

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--quick', action='store_true', help='Build the shorter quick guide.')
args = parser.parse_args()
LABEL = 'Quick Guide' if args.quick else 'User Manual'
SOURCE = ROOT / ('docs/quick-guide.md' if args.quick else 'docs/user-manual.md')
OUTPUT = ROOT / ('output/pdf/Preset-Maestro-Quick-Guide.pdf' if args.quick else 'output/pdf/Preset-Maestro-User-Manual.pdf')
VERSION = (ROOT / 'PresetMaestro/Version.txt').read_text(encoding='utf-8-sig').strip()
EDITION_PREFIX = 'Quick guide' if args.quick else 'User manual'
EDITION = next(line.removeprefix(EDITION_PREFIX).strip()
               for line in SOURCE.read_text(encoding='utf-8').splitlines()
               if line.startswith(EDITION_PREFIX + ' '))
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
FONT = Path('C:/Windows/Fonts')
for name, filename in [('Manual','calibri.ttf'), ('Manual-Bold','calibrib.ttf'), ('Manual-Italic','calibrii.ttf')]:
    pdfmetrics.registerFont(TTFont(name, str(FONT / filename)))
pdfmetrics.registerFontFamily('Manual', normal='Manual', bold='Manual-Bold', italic='Manual-Italic', boldItalic='Manual-Bold')

INK = colors.HexColor('#243247')
BLUE = colors.HexColor('#176581')
MUTED = colors.HexColor('#5B6C7B')
WIDTH = A4[0] - 88
styles = {
    'body': ParagraphStyle('Body', fontName='Manual', fontSize=11, leading=14.2, spaceAfter=6, textColor=INK),
    'title': ParagraphStyle('Title', fontName='Manual-Bold', fontSize=29, leading=33, spaceAfter=14, textColor=INK, keepWithNext=True),
    'h1': ParagraphStyle('Chapter', fontName='Manual-Bold', fontSize=22, leading=26, spaceAfter=13, textColor=BLUE, keepWithNext=True),
    'h2': ParagraphStyle('Task', fontName='Manual-Bold', fontSize=13, leading=16, spaceBefore=9, spaceAfter=6, textColor=BLUE, keepWithNext=True),
    'step': ParagraphStyle('Step', fontName='Manual', fontSize=11, leading=14.2, leftIndent=15, firstLineIndent=-15, spaceAfter=6, textColor=INK),
    'cell': ParagraphStyle('Cell', fontName='Manual', fontSize=10.2, leading=13, textColor=INK),
    'caption': ParagraphStyle('Caption', fontName='Manual-Italic', fontSize=9, leading=11.5, spaceBefore=5, spaceAfter=10, textColor=MUTED),
}
headings = [x[2:] for x in SOURCE.read_text(encoding='utf-8').splitlines() if x.startswith('# ')]
anchor_for = {title: 'chapter-'+str(i) for i,title in enumerate(headings)}
page_numbers = {}
total_pages = 0

def inline(s):
    s = escape(s)
    s = re.sub(r'\[([^]]+)\]\((https?://[^)]+)\)', r'<link href="\2" color="#176581"><u>\1</u></link>', s)
    s = re.sub(r'\*\*(.+?)\*\*', r'<b>\1</b>', s)
    s = re.sub(r'\*(.+?)\*', r'<i>\1</i>', s)
    return s

class ManualDoc(SimpleDocTemplate):
    def afterFlowable(self, item):
        if isinstance(item, Paragraph) and hasattr(item,'chapter'):
            title=item.chapter
            key=anchor_for[title]
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(title,key,level=0)
            page_numbers[title]=self.page

def footer(c, doc):
    w,h = A4
    c.setStrokeColor(colors.HexColor('#D7E2E7')); c.setLineWidth(.6)
    c.line(44,h-34,w-44,h-34); c.line(44,38,w-44,38)
    c.setFont('Manual',9); c.setFillColor(MUTED)
    c.drawString(44,h-26,'PRESET MAESTRO  /  ' + LABEL.upper())
    c.drawString(44,25,f'{VERSION} {EDITION}')
    c.drawRightString(w-44,25,f'{doc.page}' + (f' / {total_pages}' if total_pages else ''))

def make_story():
    lines=SOURCE.read_text(encoding='utf-8').splitlines()
    story=[]; i=0
    while i<len(lines):
        line=lines[i].strip(); i+=1
        if not line: continue
        if line=='<!-- pagebreak -->': story.append(PageBreak()); continue
        if line.startswith('# '):
            title=line[2:]; item=Paragraph(inline(title),styles['title' if title==headings[0] else 'h1']); item.chapter=title
            story.append(item); continue
        if line.startswith('## '): story.append(Paragraph(inline(line[3:]),styles['h2'])); continue
        if line.startswith('|'):
            rows=[line]
            while i<len(lines) and lines[i].strip().startswith('|'):
                rows.append(lines[i].strip());i+=1
            contents=[]
            for n,row in enumerate(rows):
                if re.fullmatch(r'[| :\-]+',row): continue
                cells=[]
                for cell in [x.strip() for x in row.strip('|').split('|')]:
                    value=inline(cell)
                    if cell in anchor_for:
                        value=f'<link href="#{anchor_for[cell]}" color="#176581">{value}</link>'
                        if cell in page_numbers: value+=f' <b>· {page_numbers[cell]}</b>'
                    if n==0: value='<b>'+value+'</b>'
                    cells.append(Paragraph(value,styles['cell']))
                contents.append(cells)
            table=Table(contents,colWidths=[WIDTH*.44,WIDTH*.56],hAlign='LEFT',repeatRows=1)
            table.setStyle(TableStyle([('VALIGN',(0,0),(-1,-1),'TOP'),('BACKGROUND',(0,0),(-1,0),colors.HexColor('#EAF1F4')),('LINEBELOW',(0,0),(-1,-1),.4,colors.HexColor('#D7E2E7')),('LEFTPADDING',(0,0),(-1,-1),8),('RIGHTPADDING',(0,0),(-1,-1),8),('TOPPADDING',(0,0),(-1,-1),6),('BOTTOMPADDING',(0,0),(-1,-1),6)]))
            story.extend([table,Spacer(1,8)]);continue
        if line.startswith('!['):
            match=re.fullmatch(r'!\[(.*?)\]\((.*?)\)',line)
            caption,path=match.groups(); image_path=SOURCE.parent/path
            w,h=PILImage.open(image_path).size
            maxh=260 if 'favorite-editor' in path else 220 if 'midi-mapping' in path else 135
            ratio=min(WIDTH/w,maxh/h)
            img=Image(str(image_path),width=w*ratio,height=h*ratio,hAlign='CENTER')
            story.append(KeepTogether([Spacer(1,3),img,Paragraph(inline(caption),styles['caption'])]));continue
        numbered=bool(re.match(r'^\d+\. ',line))
        story.append(Paragraph(inline(line),styles['step' if numbered else 'body']))
    return story

for iteration in range(2):
    doc=ManualDoc(str(OUTPUT),pagesize=A4,leftMargin=44,rightMargin=44,topMargin=49,bottomMargin=49,title='Preset Maestro - ' + LABEL,author='Preset Maestro',subject='Task-based guide for guitarists, draft for feedback')
    doc.build(make_story(),onFirstPage=footer,onLaterPages=footer)
    total_pages=doc.page
print(OUTPUT)
print('Pages:',total_pages)
for title,page in page_numbers.items(): print(page,title)

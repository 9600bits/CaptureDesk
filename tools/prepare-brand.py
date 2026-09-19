"""Convert the generated brand artwork into transparent Windows icon assets."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
source = Image.open(root / 'output/imagegen/capturedesk-icon-concept.png').convert('RGB')
# Flatten the generated ink and remove the ivory paper, preserving edge coverage.
alpha = source.getchannel('R').point(lambda value: max(0, min(255, round((242-value)*255/190))))
mark = Image.new('RGBA', source.size, '#356B4B')
mark.putalpha(alpha)
mark = mark.crop(alpha.point(lambda value: 255 if value > 128 else 0).getbbox())
canvas = Image.new('RGBA', (1024, 1024))
mark.thumbnail((896, 896), Image.Resampling.LANCZOS)
canvas.alpha_composite(mark, ((1024-mark.width)//2, (1024-mark.height)//2))
target = root / 'src/CaptureDesk.App/Assets/Brand'
target.mkdir(parents=True, exist_ok=True)
canvas.resize((256,256), Image.Resampling.LANCZOS).save(target / 'CaptureDesk.png')
tile = Image.new('RGBA', (1024, 1024))
ImageDraw.Draw(tile).rounded_rectangle((16,16,1008,1008), radius=120, fill='#F5F6F2')
tile.alpha_composite(canvas.resize((880,880), Image.Resampling.LANCZOS), (72,72))
tile.save(target / 'CaptureDesk.ico', sizes=[(n,n) for n in (16,20,24,32,40,48,64,128,256)])
canvas.resize((256,256), Image.Resampling.LANCZOS).save(root / 'output/imagegen/capturedesk-icon-final.png')

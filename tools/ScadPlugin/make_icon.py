"""Иконка кнопки плагина SCAD «Экспорт в OpenCS»: стиль иконки OpenCS (синий фон,
сечение с оранжевой арматурой) + белая стрелка «экспорт»."""
import sys
from PIL import Image, ImageDraw

S = 8  # суперсэмплинг


def draw(size):
    n = size * S
    im = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    u = n / 24.0  # единица сетки 24×24

    def r(x0, y0, x1, y1):
        return [x0 * u, y0 * u, x1 * u, y1 * u]

    # Фон: синий скруглённый квадрат с вертикальным градиентом.
    bg = Image.new('RGBA', (n, n))
    gd = ImageDraw.Draw(bg)
    top, bot = (24, 92, 170), (12, 52, 112)
    for y in range(n):
        t = y / (n - 1)
        gd.line([(0, y), (n, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bot)) + (255,))
    mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle(r(0.5, 0.5, 23.5, 23.5), radius=4.5 * u, fill=255)
    im.paste(bg, (0, 0), mask)

    # Сечение: светлый прямоугольник с тонкой рамкой.
    d.rounded_rectangle(r(3, 2.5, 13, 21.5), radius=1.2 * u, fill=(226, 230, 236, 255),
                        outline=(255, 255, 255, 255), width=max(1, int(0.8 * u)))
    # Арматура: по два стержня у верхней и нижней грани, центры — в серединах пикселей.
    rad = 1.5
    for cx, cy in [(5.5, 5.5), (10.5, 5.5), (5.5, 18.5), (10.5, 18.5)]:
        d.ellipse(r(cx - rad, cy - rad, cx + rad, cy + rad), fill=(236, 92, 20, 255))

    # Стрелка «экспорт» вправо.
    white = (255, 255, 255, 255)
    d.rectangle(r(13.5, 10.6, 18.2, 13.4), fill=white)
    d.polygon([(17.3 * u, 7.2 * u), (22.3 * u, 12 * u), (17.3 * u, 16.8 * u)], fill=white)

    return im.resize((size, size), Image.LANCZOS)


out_ico, out_png = sys.argv[1], sys.argv[2]
frames = {s: draw(s) for s in (16, 24, 32, 48)}
frames[48].save(out_ico, format='ICO', bitmap_format='bmp', sizes=[(48, 48), (32, 32), (24, 24), (16, 16)],
                append_images=[frames[32], frames[24], frames[16]])
# Превью: 24 px крупно (как увидит пользователь) и 48 px.
prev = Image.new('RGBA', (24 * 8 + 48 * 4 + 20, 24 * 8), (240, 240, 240, 255))
prev.paste(frames[24].resize((192, 192), Image.NEAREST), (0, 0), frames[24].resize((192, 192), Image.NEAREST))
prev.paste(frames[48].resize((192, 192), Image.LANCZOS), (212, 0), frames[48].resize((192, 192), Image.LANCZOS))
prev.save(out_png)

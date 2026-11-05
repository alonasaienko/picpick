"""Створення прев'ю з вотермаркою за FR-203.

Чиста функція: на вході байти оригіналу, на виході байти JPEG. Черги, MinIO
і внутрішнього API тут немає, їх додає Worker (#57).

Ручна перевірка:
    python src/worker/preview.py фото.jpg прев'ю.jpg
"""
import argparse
import io
import math
import sys
import warnings
from pathlib import Path

from PIL import Image, ImageCms, ImageDraw, ImageFont, ImageOps, UnidentifiedImageError

MAX_SIDE = 1600
JPEG_QUALITY = 85

# T-203 (п.9): ліміт 100 Мп перевіряється явно до декодування пікселів.
# MAX_IMAGE_PIXELS піднято до того самого значення, щоб попередження Pillow
# (за замовчуванням ~89 Мп) не відхиляло фото, які ще в межах ліміту.
MAX_PIXELS = 100_000_000
Image.MAX_IMAGE_PIXELS = MAX_PIXELS

# Для MVP лише JPEG і PNG. HEIC додасть pillow-heif (register_heif_opener і
# формат "HEIF"), RAW декодує rawpy окремою гілкою з тим самим лімітом.
SUPPORTED_FORMATS = ("JPEG", "PNG")

FONT_PATH = Path(__file__).parent / "fonts" / "NotoSans-Bold.ttf"
WATERMARK_TEXT = "PicPick"
WATERMARK_ANGLE = 30  # градусів, проти годинникової стрілки
# Довжина одного напису відносно ширини прев'ю. Після повороту на 30° його
# горизонтальна проєкція ≈ 0.45 × cos 30° ≈ 0.39 ширини, тобто більше третини.
WATERMARK_LENGTH = 0.45
WATERMARK_FILL = (255, 255, 255, 90)  # білий, ~35 % непрозорості
WATERMARK_STROKE = (0, 0, 0, 40)  # тонкий темний контур, щоб напис був видно на світлому

SRGB_PROFILE = ImageCms.createProfile("sRGB")


class PreviewError(Exception):
    """Файл неможливо обробити: не той формат, пошкоджений або завеликий.

    Повтор не допоможе, тому Worker має повідомити «помилка», а не
    «спроба невдала» (FR-205).
    """


def make_preview(original: bytes, text: str = WATERMARK_TEXT) -> bytes:
    """Повертає JPEG-прев'ю з довгою стороною до 1600 px і вотермаркою."""
    with warnings.catch_warnings():
        # Друга лінія захисту T-203: попередження про «бомбу» стає помилкою.
        warnings.simplefilter("error", Image.DecompressionBombWarning)
        try:
            image = _decode(original)
        except (Image.DecompressionBombError, Image.DecompressionBombWarning) as exc:
            raise PreviewError(f"зображення більше за {MAX_PIXELS // 1_000_000} Мп") from exc
        except UnidentifiedImageError as exc:
            raise PreviewError("формат не підтримується: лише " + ", ".join(SUPPORTED_FORMATS)) from exc
        except (OSError, SyntaxError, ValueError) as exc:
            raise PreviewError(f"не вдалося декодувати зображення: {exc}") from exc

    image = _watermark(image, text)

    out = io.BytesIO()
    # exif, icc_profile і xmp не передаються: у прев'ю немає метаданих (T-205).
    image.save(out, "JPEG", quality=JPEG_QUALITY, optimize=True, progressive=True)
    return out.getvalue()


def _decode(original: bytes) -> Image.Image:
    image = Image.open(io.BytesIO(original), formats=SUPPORTED_FORMATS)
    width, height = image.size
    if width * height > MAX_PIXELS:
        raise PreviewError(f"зображення {width}×{height} більше за {MAX_PIXELS // 1_000_000} Мп")

    # Для JPEG декодер одразу зменшує в 2, 4 або 8 разів, але не менше за
    # 1600 px: 24 Мп фото займає в пам'яті в рази менше.
    image.draft("RGB", (MAX_SIDE, MAX_SIDE))
    image.load()

    # Повертаємо пікселі за EXIF Orientation, бо сам EXIF у прев'ю не потрапить.
    image = ImageOps.exif_transpose(image)
    image = _to_srgb(image)
    image.thumbnail((MAX_SIDE, MAX_SIDE), Image.Resampling.LANCZOS)  # не збільшує
    return image


def _to_srgb(image: Image.Image) -> Image.Image:
    """Зводить будь-який режим до RGB у кольоровому просторі sRGB."""
    if image.mode in ("I;16", "I;16B", "I;16L", "I"):
        # 16-бітні PNG: масштабуємо до 8 біт, інакше convert обріже значення.
        image = image.convert("I").point(lambda v: v / 256).convert("L")
    if image.mode in ("RGBA", "LA", "PA") or "transparency" in image.info:
        rgba = image.convert("RGBA")
        image = Image.new("RGB", rgba.size, (255, 255, 255))
        image.paste(rgba, mask=rgba.getchannel("A"))

    icc = image.info.get("icc_profile")
    if icc and image.mode in ("RGB", "CMYK"):
        # Фото з телефона часто в Display P3 чи Adobe RGB. Профіль у прев'ю
        # не копіюємо, тому переводимо пікселі в sRGB, щоб не «злиняли» кольори.
        try:
            source = ImageCms.ImageCmsProfile(io.BytesIO(icc))
            return ImageCms.profileToProfile(image, source, SRGB_PROFILE, outputMode="RGB")
        except ImageCms.PyCMSError:
            pass  # битий профіль: беремо пікселі як є
    return image.convert("RGB")


def _watermark(image: Image.Image, text: str) -> Image.Image:
    """Накладає напис по діагоналі «цеглинками» на весь кадр.

    Один напис завжди стоїть точно в центрі кадру (FR-203: центральна частина,
    щонайменше третина ширини), решта повторюються навколо, щоб вотермарку
    не можна було просто обрізати.
    """
    width, height = image.size
    font = _fit_font(text, WATERMARK_LENGTH * width)
    stroke = _stroke(font)
    left, top, right, bottom = font.getbbox(text, stroke_width=stroke)
    text_w, text_h = right - left, bottom - top

    # Повертаємо один напис, а не весь шар: так у кілька разів швидше.
    tile = Image.new("RGBA", (text_w + 2 * stroke, text_h + 2 * stroke), (0, 0, 0, 0))
    ImageDraw.Draw(tile).text((tile.width / 2, tile.height / 2), text, font=font, anchor="mm",
                              fill=WATERMARK_FILL, stroke_width=stroke, stroke_fill=WATERMARK_STROKE)
    tile = tile.rotate(WATERMARK_ANGLE, resample=Image.Resampling.BICUBIC, expand=True)

    # Сітка в системі координат напису: крок уздовж рядка й між рядками.
    step_x = text_w * 1.3
    step_y = text_h * 2.5
    angle = math.radians(WATERMARK_ANGLE)
    cos, sin = math.cos(angle), math.sin(angle)
    reach = math.hypot(width, height) / 2 + max(tile.size)
    rows = math.ceil(reach / step_y)
    cols = math.ceil(reach / step_x) + 1

    result = image.convert("RGBA")
    for row in range(-rows, rows + 1):
        shift = step_x / 2 if row % 2 else 0  # непарні ряди зсунуті, як цегла
        for col in range(-cols, cols + 1):
            dx, dy = col * step_x + shift, row * step_y
            # Поворот точки сітки на той самий кут, що й напис (проти годинникової).
            x = width / 2 + dx * cos + dy * sin - tile.width / 2
            y = height / 2 - dx * sin + dy * cos - tile.height / 2
            _paste_clipped(result, tile, round(x), round(y))

    # Нове зображення без info, тож метадані оригіналу (exif, xmp,
    # icc_profile) далі не йдуть.
    return result.convert("RGB")


def _paste_clipped(base: Image.Image, tile: Image.Image, x: int, y: int) -> None:
    """alpha_composite, що дозволяє напису частково виходити за край кадру."""
    src_x, src_y = max(0, -x), max(0, -y)
    dst_x, dst_y = max(0, x), max(0, y)
    w = min(tile.width - src_x, base.width - dst_x)
    h = min(tile.height - src_y, base.height - dst_y)
    if w > 0 and h > 0:
        base.alpha_composite(tile, (dst_x, dst_y), (src_x, src_y, src_x + w, src_y + h))


def _fit_font(text: str, target_length: float) -> ImageFont.FreeTypeFont:
    """Підбирає кегль, за якого напис має задану довжину в пікселях."""
    reference = ImageFont.truetype(str(FONT_PATH), 100)
    size = max(8, round(100 * target_length / reference.getlength(text)))
    return ImageFont.truetype(str(FONT_PATH), size)


def _stroke(font: ImageFont.FreeTypeFont) -> int:
    return max(1, font.size // 40)


def main() -> int:
    parser = argparse.ArgumentParser(description="Створити прев'ю з вотермаркою (FR-203).")
    parser.add_argument("source", type=Path, help="оригінал: JPEG або PNG")
    parser.add_argument("target", type=Path, help="куди записати прев'ю (JPEG)")
    parser.add_argument("--text", default=WATERMARK_TEXT, help="текст вотермарки")
    args = parser.parse_args()

    try:
        preview = make_preview(args.source.read_bytes(), args.text)
    except PreviewError as exc:
        print(f"Помилка: {exc}", file=sys.stderr)
        return 1
    args.target.write_bytes(preview)
    with Image.open(io.BytesIO(preview)) as result:
        print(f"{args.target}: {result.width}×{result.height}, {len(preview) // 1024} КБ")
    return 0


if __name__ == "__main__":
    sys.exit(main())

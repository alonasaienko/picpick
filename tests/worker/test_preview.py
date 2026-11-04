"""Тести make_preview за FR-203 і T-203, T-205 (п.9)."""
import io
import struct
import zlib

import pytest
from PIL import Image, ImageChops, ImageDraw, ImageFont

import preview
from preview import MAX_SIDE, PreviewError, make_preview

ORIENTATION = 0x0112
GPS_IFD = 0x8825


def encode(image: Image.Image, fmt: str = "JPEG", **params) -> bytes:
    out = io.BytesIO()
    image.save(out, fmt, **params)
    return out.getvalue()


def open_preview(data: bytes) -> Image.Image:
    image = Image.open(io.BytesIO(data))
    image.load()
    return image


@pytest.mark.parametrize(
    ("size", "expected"),
    [
        ((3000, 2000), (1600, 1067)),  # горизонтальне
        ((2000, 3000), (1067, 1600)),  # вертикальне
        ((4000, 4000), (1600, 1600)),  # квадратне
        ((6228, 4000), (1600, 1028)),  # нестандартні пропорції
    ],
)
def test_long_side_is_1600_and_aspect_ratio_kept(size, expected):
    result = open_preview(make_preview(encode(Image.new("RGB", size, "gray"))))

    assert result.format == "JPEG"
    assert result.size == expected


def test_small_photo_is_not_upscaled():
    result = open_preview(make_preview(encode(Image.new("RGB", (800, 600), "gray"))))

    assert result.size == (800, 600)


def test_png_with_transparency_becomes_jpeg_on_white():
    original = Image.new("RGBA", (2000, 1000), (0, 0, 0, 0))

    result = open_preview(make_preview(encode(original, "PNG")))

    assert result.format == "JPEG"
    assert result.mode == "RGB"
    assert result.size == (1600, 800)
    # Прозорий фон став білим, а не чорним; кут вільний від центрального напису.
    assert min(result.getpixel((5, 5))) > 200


def test_16_bit_png_keeps_brightness():
    original = Image.new("I;16", (400, 300), 40000)  # ~61 % від 65535

    result = open_preview(make_preview(encode(original, "PNG")))

    assert result.size == (400, 300)
    assert 140 < result.getpixel((5, 5))[0] < 170  # 40000 / 256 ≈ 156


def test_exif_orientation_is_applied_to_pixels():
    # Камера телефона записала кадр «лежачим» і Orientation = 6
    # (для показу повернути на 90° за годинниковою стрілкою).
    stored = Image.new("RGB", (2400, 1200), "blue")
    stored.paste((255, 0, 0), (0, 0, 600, 300))  # червоний блок угорі ліворуч
    exif = Image.Exif()
    exif[ORIENTATION] = 6

    result = open_preview(make_preview(encode(stored, exif=exif.tobytes())))

    assert result.size == (800, 1600)  # стоїть вертикально, а не на боці
    # Після повороту за годинниковою стрілкою лівий верхній кут стає правим верхнім.
    r, g, b = result.getpixel((result.width - 20, 20))
    assert r > b + 100
    r, g, b = result.getpixel((20, 20))
    assert b > r + 100
    assert ORIENTATION not in result.getexif()


def test_exif_and_gps_are_not_copied():
    exif = Image.Exif()
    exif[0x010F] = "SecretCam"  # Make
    exif[0x0110] = "Model X"  # Model
    exif.get_ifd(GPS_IFD).update({1: "N", 2: (50.0, 27.0, 0.0), 3: "E", 4: (30.0, 31.0, 0.0)})
    original = encode(Image.new("RGB", (2000, 1500), "gray"), exif=exif.tobytes(), icc_profile=b"")

    data = make_preview(original)
    result = open_preview(data)

    assert len(result.getexif()) == 0
    assert "exif" not in result.info
    assert "xmp" not in result.info
    assert b"Exif" not in data
    assert b"SecretCam" not in data


def test_original_bytes_are_untouched():
    original = encode(Image.new("RGB", (2000, 1500), "gray"))
    copy = bytes(original)

    make_preview(original)

    assert original == copy


def test_watermark_is_in_center_and_spans_a_third_of_width():
    original = Image.new("RGB", (1600, 1000), (128, 128, 128))
    background = open_preview(encode(original, quality=preview.JPEG_QUALITY))

    result = open_preview(make_preview(encode(original)))

    diff = ImageChops.difference(result, background).convert("L").point(lambda v: 255 if v > 20 else 0)
    width, height = result.size
    # Горизонтальна смуга через центр кадру заввишки 10 % висоти.
    band = diff.crop((0, int(height * 0.45), width, int(height * 0.55)))
    left, _, right, _ = band.getbbox()
    assert right - left >= width / 3
    # Напис саме по центру: змінені пікселі є по обидва боки від середини.
    assert left < width / 2 < right


def test_watermark_is_semi_transparent():
    original = Image.new("RGB", (1600, 1000), (0, 0, 0))

    result = open_preview(make_preview(encode(original))).convert("L")

    # На чорному фоні білий напис не стає чисто білим: крізь нього видно фото.
    assert 0 < result.getextrema()[1] < 200


def test_bundled_font_has_cyrillic_glyphs():
    font = ImageFont.truetype(str(preview.FONT_PATH), 64)

    def render(char: str) -> bytes:
        canvas = Image.new("L", (80, 80))
        ImageDraw.Draw(canvas).text((40, 40), char, font=font, fill=255, anchor="mm")
        return canvas.tobytes()

    missing = render("\U0010FFFD")  # символу немає в шрифті: малюється «порожній квадрат»
    for char in "ПікЖЄЇґ":
        assert render(char) != missing, char


def test_cyrillic_watermark_renders():
    original = encode(Image.new("RGB", (1600, 1000), "gray"))

    result = open_preview(make_preview(original, text="Олена Петренко"))

    assert result.size == (1600, 1000)


def png_header_only(width: int, height: int) -> bytes:
    """PNG на кількадесят байтів, що в заголовку заявляє величезний розмір."""
    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IEND", b"")


def test_decompression_bomb_is_rejected_before_decoding():
    bomb = png_header_only(50_000, 50_000)  # 2,5 млрд пікселів, понад 7 ГБ у пам'яті

    with pytest.raises(PreviewError, match="100 Мп"):
        make_preview(bomb)


def test_limit_is_exactly_100_megapixels():
    with pytest.raises(PreviewError):
        make_preview(png_header_only(10_001, 10_000))


@pytest.mark.parametrize(
    "data",
    [
        b"",
        b"not an image at all",
        encode(Image.new("RGB", (100, 100)), "GIF"),  # формат поза MVP
        encode(Image.new("RGB", (2000, 1500), "gray"))[:2000],  # обрізаний JPEG
    ],
    ids=["empty", "garbage", "gif", "truncated-jpeg"],
)
def test_undecodable_file_raises_preview_error(data):
    with pytest.raises(PreviewError):
        make_preview(data)

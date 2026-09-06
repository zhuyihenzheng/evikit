#!/usr/bin/env python3
"""サンプルプロジェクト用のダミー画面 (1280x800) を生成する。

実行方法:
    python3 examples/gen-sample-images.py
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

W, H = 1280, 800
BASE = Path(__file__).resolve().parent / "reference" / "evidence"

FONT_REGULAR = "/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc"
FONT_BOLD = "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc"

WHITE = (255, 255, 255)
BG = (243, 245, 248)
CHROME = (222, 225, 230)
LINE = (206, 212, 218)
TEXT = (33, 37, 41)
MUTED = (108, 117, 125)
PRIMARY = (31, 56, 100)
RED = (211, 47, 47)


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD if bold else FONT_REGULAR, size)


def browser_frame(tab_title: str, url: str) -> tuple[Image.Image, ImageDraw.ImageDraw]:
    """ブラウザ風の外枠（タブ + アドレスバー）を描いた画像を返す。"""
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W, 78], fill=CHROME)
    for i, color in enumerate([(237, 106, 94), (245, 191, 79), (97, 196, 84)]):
        d.ellipse([18 + i * 22, 14, 32 + i * 22, 28], fill=color)
    d.rounded_rectangle([96, 8, 520, 34], radius=6, fill=WHITE)
    d.text((112, 13), tab_title, font=font(14), fill=TEXT)
    d.rounded_rectangle([16, 44, W - 16, 70], radius=13, fill=WHITE, outline=LINE)
    d.text((34, 50), url, font=font(14), fill=MUTED)
    return img, d


def field(d: ImageDraw.ImageDraw, x: int, y: int, w: int, label: str, value: str) -> None:
    d.text((x, y), label, font=font(15), fill=TEXT)
    d.rectangle([x, y + 24, x + w, y + 62], fill=WHITE, outline=LINE)
    d.text((x + 12, y + 34), value, font=font(16), fill=TEXT if value else MUTED)


def login_card(d: ImageDraw.ImageDraw, error: str = "") -> None:
    x0, y0, x1, y1 = 390, 160, 890, 620
    d.rounded_rectangle([x0, y0, x1, y1], radius=10, fill=WHITE, outline=LINE)
    d.text((x0 + 40, y0 + 40), "顧客管理システム", font=font(28, bold=True), fill=PRIMARY)
    d.text((x0 + 40, y0 + 84), "ログイン", font=font(18), fill=MUTED)
    field(d, x0 + 40, y0 + 130, 420, "ユーザーID", "U001")
    field(d, x0 + 40, y0 + 216, 420, "パスワード", "••••••••")
    d.rounded_rectangle([x0 + 40, y0 + 310, x0 + 460, y0 + 356], radius=6, fill=PRIMARY)
    d.text((x0 + 205, y0 + 322), "ログイン", font=font(18, bold=True), fill=WHITE)
    if error:
        d.text((x0 + 40, y0 + 378), error, font=font(17, bold=True), fill=RED)


def make_login() -> None:
    img, d = browser_frame("ログイン - 顧客管理システム", "stg.example.local/login")
    login_card(d)
    save(img, "TC-001/E01.png")


def make_error() -> None:
    img, d = browser_frame("ログイン - 顧客管理システム", "stg.example.local/login")
    login_card(d, error="Invalid credentials")
    save(img, "TC-002/E01.png")


def make_home() -> None:
    img, d = browser_frame("ホーム - 顧客管理システム", "stg.example.local/home")
    d.rectangle([0, 78, W, 138], fill=PRIMARY)
    d.text((28, 96), "顧客管理システム", font=font(22, bold=True), fill=WHITE)
    d.text((W - 250, 100), "山田 太郎", font=font(18, bold=True), fill=WHITE)
    d.ellipse([W - 290, 96, W - 262, 124], fill=(155, 187, 224))

    d.rectangle([0, 138, 230, H], fill=(235, 238, 242))
    for i, item in enumerate(["ホーム", "顧客一覧", "契約管理", "帳票出力", "設定"]):
        y = 168 + i * 46
        if i == 0:
            d.rectangle([0, y - 10, 230, y + 30], fill=(214, 223, 236))
        d.text((28, y), item, font=font(16), fill=TEXT)

    d.text((266, 168), "ホーム", font=font(24, bold=True), fill=TEXT)
    d.text((266, 208), "最終ログイン: 2026-09-04 10:12:35", font=font(14), fill=MUTED)

    headers = ["顧客ID", "顧客名", "登録日"]
    rows = [
        ["00012", "株式会社アルファ", "2026-08-01"],
        ["00034", "ベータ商事", "2026-08-14"],
        ["00108", "ガンマ工業", "2026-09-01"],
    ]
    x, y, widths = 266, 260, [160, 340, 200]
    d.rectangle([x, y, x + sum(widths), y + 38], fill=CHROME, outline=LINE)
    cx = x
    for text, w in zip(headers, widths):
        d.text((cx + 12, y + 10), text, font=font(15, bold=True), fill=TEXT)
        cx += w
    for r, row in enumerate(rows):
        ry = y + 38 + r * 38
        d.rectangle([x, ry, x + sum(widths), ry + 38], fill=WHITE, outline=LINE)
        cx = x
        for text, w in zip(row, widths):
            d.text((cx + 12, ry + 10), text, font=font(15), fill=TEXT)
            cx += w
    save(img, "TC-001/E02.png")


def save(img: Image.Image, rel: str) -> None:
    path = BASE / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)
    print(f"wrote {path}")


if __name__ == "__main__":
    make_login()
    make_home()
    make_error()

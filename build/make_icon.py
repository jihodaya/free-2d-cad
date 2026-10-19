# Free 2D CAD 아이콘 생성: 남색 둥근 사각형 + 흰 F + 가운데 주황 십자 커서(CAD 느낌) + 옅은 모눈
# 사용법: python build\make_icon.py  →  src\Free2DCad.App\Assets\Free2DCad.ico (16~256 여러 크기), Free2DCad.png
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "Free2DCad.App" / "Assets"
OUT.mkdir(parents=True, exist_ok=True)

S = 1024  # 크게 그린 뒤 줄여서 부드럽게


def draw(size: int, grid: bool) -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # 바탕: 위→아래 남색 그라데이션, 둥근 모서리
    pad = 40
    radius = 190
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    top, bottom = (34, 74, 128), (13, 33, 64)
    for y in range(S):
        t = y / (S - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        gd.line([(0, y), (S, y)], fill=c + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, S - pad, S - pad], radius=radius, fill=255)
    img.paste(grad, (0, 0), mask)

    # 옅은 모눈 (큰 아이콘에서만)
    if grid:
        step = 96
        for x in range(pad + step, S - pad, step):
            d.line([(x, pad + 30), (x, S - pad - 30)], fill=(255, 255, 255, 28), width=4)
        for y in range(pad + step, S - pad, step):
            d.line([(pad + 30, y), (S - pad - 30, y)], fill=(255, 255, 255, 28), width=4)

    # 흰 F (굵은 획): 왼쪽 세로 획 + 위 가로 획 + 가운데 가로 획
    w = 150 if size >= 48 else 190
    left, right = 300, S - 230
    top_y, bot_y = 220, S - 220
    white = (245, 248, 252, 255)
    d.rectangle([left - w // 2, top_y, left + w // 2, bot_y], fill=white)
    bar = 130 if size >= 48 else 170
    d.rectangle([left, top_y, right, top_y + bar], fill=white)
    cy = S // 2
    d.rectangle([left, cy - bar // 2 + 20, right - 90, cy + bar // 2 + 20], fill=white)

    # 주황 십자 커서 + 가운데 작은 네모 (CAD 선택 상자)
    orange = (255, 140, 26, 255)
    cx = S // 2
    arm = 230 if size >= 48 else 250
    th = 34 if size >= 48 else 60
    d.rectangle([cx - arm, cy - th // 2, cx + arm, cy + th // 2], fill=orange)
    d.rectangle([cx - th // 2, cy - arm, cx + th // 2, cy + arm], fill=orange)
    box = 70 if size >= 48 else 90
    d.rectangle([cx - box, cy - box, cx + box, cy + box], outline=orange, width=th)

    return img.resize((size, size), Image.LANCZOS)


sizes = [16, 24, 32, 48, 64, 128, 256]
images = [draw(s, grid=s >= 64) for s in sizes]
images[-1].save(OUT / "Free2DCad.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])
images[-1].save(OUT / "Free2DCad.png")
print("saved", OUT / "Free2DCad.ico")

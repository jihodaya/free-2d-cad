# Free 2D CAD 내장 해치 패턴 만들기 (모든 패턴을 이 파일에서 직접 정의한다)
# 사용법: python tools/patterns/make_patterns.py  →  src/Free2DCad.Core/Hatching/Free2DCadPatterns.pat
#
# 패턴 하나 = 되풀이되는 칸(격자 벡터 a, b)과 그 칸 안의 선분 목록. 이 스크립트가 선분마다
# .pat 한 줄(각도, 시작점, 다음 선까지의 이동(선 방향, 수직), 대시·공백 길이)을 계산한다.
# 단위는 mm. 해치 축척 1이면 아래 크기 그대로 그려진다.
import math
from fractions import Fraction
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "src" / "Free2DCad.Core" / "Hatching" / "Free2DCadPatterns.pat"

EPS = 1e-7


def fmt(v: float) -> str:
    if abs(v) < 1e-9:
        v = 0.0
    s = f"{v:.6f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def lattice_line(a, b, d):
    """격자 (a, b)에서 방향 d에 평행한 가장 짧은 격자 벡터 u와, u와 함께 격자를 이루면서 수직 거리가 가장 작은 w."""
    best_u = None
    for i in range(-24, 25):
        for j in range(-24, 25):
            if i == 0 and j == 0:
                continue
            v = (i * a[0] + j * b[0], i * a[1] + j * b[1])
            cross = v[0] * d[1] - v[1] * d[0]
            dot = v[0] * d[0] + v[1] * d[1]
            if abs(cross) < 1e-6 and dot > EPS:
                if best_u is None or dot < best_u[1] - EPS:
                    best_u = ((i, j), dot, v)
    if best_u is None:
        raise ValueError(f"방향 {d}에 평행한 격자 벡터가 없음")
    (ui, uj), ulen, u = best_u
    det_ab = a[0] * b[1] - a[1] * b[0]
    best_w = None
    for i in range(-24, 25):
        for j in range(-24, 25):
            w = (i * a[0] + j * b[0], i * a[1] + j * b[1])
            det = u[0] * w[1] - u[1] * w[0]
            if abs(abs(det) - abs(det_ab)) > 1e-6:
                continue
            perp = -w[0] * d[1] + w[1] * d[0]  # d를 반시계 90° 돌린 방향 성분
            if perp <= EPS:
                continue
            if best_w is None or perp < best_w[0] - EPS:
                best_w = (perp, w)
    perp, w = best_w
    along = w[0] * d[0] + w[1] * d[1]
    along = along - math.floor(along / ulen + 1e-9) * ulen  # 선 방향 이동은 한 주기 안으로
    return ulen, along, perp


def pattern_lines(a, b, segments):
    lines = []
    for seg in segments:
        (x0, y0), (x1, y1) = seg
        length = math.hypot(x1 - x0, y1 - y0)
        if length < EPS:
            raise ValueError("점은 dot()으로")
        d = ((x1 - x0) / length, (y1 - y0) / length)
        ang = math.degrees(math.atan2(d[1], d[0])) % 180
        if abs(ang - 180) < 1e-9:
            ang = 0
        if ang < 0:
            ang += 180
        # 0~180° 방향으로 맞춤 (반대 방향이면 시작점을 끝점으로)
        r = math.radians(ang)
        dd = (math.cos(r), math.sin(r))
        if dd[0] * d[0] + dd[1] * d[1] < 0:
            x0, y0, x1, y1 = x1, y1, x0, y0
        ulen, along, perp = lattice_line(a, b, dd)
        dashes = [] if abs(length - ulen) < 1e-6 else [length, -(ulen - length)]
        lines.append((ang, x0, y0, along, perp, dashes))
    return lines


def dots(a, b, points):
    """점: 길이 0 대시. 방향은 a 쪽."""
    lines = []
    ang = math.degrees(math.atan2(a[1], a[0])) % 180
    r = math.radians(ang)
    dd = (math.cos(r), math.sin(r))
    ulen, along, perp = lattice_line(a, b, dd)
    for (x, y) in points:
        lines.append((ang, x, y, along, perp, [0, -ulen]))
    return lines


def parallel(angle, spacing, x0=0.0, y0=0.0, dashes=None, shift=0.0):
    """같은 간격의 평행선 한 무리 (dashes가 있으면 대시 선)."""
    return [(angle, x0, y0, shift, spacing, dashes or [])]


R3 = math.sqrt(3)
P = []  # (이름, 탭, 설명, 줄 목록)


def add(name, tab, desc, lines):
    P.append((name, tab, desc, lines))


# ----- 기타: 기본 -----
add("SOLID", "기타", "채우기", [])
add("LINE", "기타", "평행선 (간격 3)", parallel(0, 3))
add("NET", "기타", "격자 (3 × 3)", parallel(0, 3) + parallel(90, 3))
add("NET45", "기타", "마름모 격자 (45°, 간격 3)", parallel(45, 3) + parallel(135, 3))
add("DASH", "기타", "엇갈린 파선", parallel(0, 3, dashes=[3, -3], shift=3))
add("DOTS", "기타", "점 (엇갈림)", parallel(0, 2, dashes=[0, -4], shift=2))
add("CROSS", "기타", "십자 표시 (6 간격)",
    pattern_lines((6, 0), (0, 6), [((-1, 0), (1, 0)), ((0, -1), (0, 1))]))
add("SQUARES", "기타", "작은 사각형 (2 × 2, 4 간격)",
    pattern_lines((4, 0), (0, 4), [((0, 0), (2, 0)), ((0, 2), (2, 2)), ((0, 0), (0, 2)), ((2, 0), (2, 2))]))
add("CHECKER", "기타", "바둑판 대각 (6 간격)",
    pattern_lines((6, 0), (0, 6), [((0, 0), (3, 3)), ((3, 0), (0, 3))]))

# 벽돌·블록 (길이 방향 줄눈 엇갈림)
add("BRICK", "기타", "벽돌 쌓기 (10 × 5)",
    pattern_lines((10, 0), (5, 5), [((0, 0), (10, 0)), ((0, 0), (0, 5))]))
add("BLOCK", "기타", "블록 쌓기 (20 × 10)",
    pattern_lines((20, 0), (10, 10), [((0, 0), (20, 0)), ((0, 0), (0, 10))]))
add("STACK", "기타", "줄 맞춤 블록 (20 × 10)",
    pattern_lines((20, 0), (0, 10), [((0, 0), (20, 0)), ((0, 0), (0, 10))]))
add("TILE", "기타", "타일 (10 × 10, 줄눈 1)",
    pattern_lines((11, 0), (0, 11), [((0, 0), (10, 0)), ((0, 10), (10, 10)), ((0, 0), (0, 10)), ((10, 0), (10, 10))]))
# 육각형·삼각형 (정다각형 격자)
a = 3.0
h = a * R3 / 2
hex_a, hex_b = (1.5 * a, h), (0.0, 2 * h)
hex_segs = []
for k in range(6):
    t0 = math.radians(60 * k)
    t1 = math.radians(60 * (k + 1))
    p0 = (a * math.cos(t0), a * math.sin(t0))
    p1 = (a * math.cos(t1), a * math.sin(t1))
    hex_segs.append((p0, p1))
# 이웃 육각형과 변을 나누므로 세 변(위·왼쪽 위·왼쪽 아래)만 그린다
add("HEX", "기타", "육각형 (변 3)", pattern_lines(hex_a, hex_b, hex_segs[1:4]))
add("TRIANGLES", "기타", "정삼각형 격자 (변 4)",
    parallel(0, 4 * R3 / 2) + parallel(60, 4 * R3 / 2) + parallel(120, 4 * R3 / 2))
add("ZIGZAG", "기타", "지그재그 (폭 4, 높이 2, 간격 4)",
    pattern_lines((4, 0), (0, 4), [((0, 0), (2, 2)), ((2, 2), (4, 0))]))

# 재료·지형
add("EARTH", "기타", "흙 (가로·세로 짧은 선 묶음)",
    pattern_lines((8, 0), (0, 8),
                  [((0, y), (3, y)) for y in (0.0, 1.0, 2.0)]
                  + [((x, 4), (x, 7)) for x in (4.5, 5.5, 6.5)]))
add("GRASS", "기타", "풀 (세 갈래 짧은 선)",
    pattern_lines((6, 0), (3, 5),
                  [((0, 0), (0, 2)), ((-0.3, 0), (-1.2, 1.5)), ((0.3, 0), (1.2, 1.5))]))
add("SAND", "기타", "모래 (흩어진 점)",
    dots((5, 0), (0, 5), [(0.4, 0.6), (2.1, 1.7), (3.8, 0.3), (1.2, 3.4), (3.3, 3.9), (4.6, 2.5)]))
add("GRAVEL", "기타", "자갈 (작은 다각형)",
    pattern_lines((10, 0), (0, 10),
                  [((1, 1), (3, 0.6)), ((3, 0.6), (3.6, 2.2)), ((3.6, 2.2), (2, 3)), ((2, 3), (1, 1)),
                   ((6, 4), (8.4, 4.4)), ((8.4, 4.4), (7.6, 6.2)), ((7.6, 6.2), (6, 4)),
                   ((2.4, 6.5), (4.2, 6.2)), ((4.2, 6.2), (4.6, 8.0)), ((4.6, 8.0), (3.0, 8.6)), ((3.0, 8.6), (2.4, 6.5))]))
add("CONCRETE", "기타", "콘크리트 (작은 삼각형과 점)",
    pattern_lines((12, 0), (0, 12),
                  [((1, 1), (3, 1)), ((3, 1), (2, 2.7)), ((2, 2.7), (1, 1)),
                   ((7, 6), (8.6, 7.2)), ((8.6, 7.2), (6.8, 7.8)), ((6.8, 7.8), (7, 6))])
    + dots((12, 0), (0, 12), [(5, 2), (10, 3.5), (3, 8), (9.5, 10), (1.5, 5.5), (5.5, 10.5)]))
add("INSULATION", "기타", "단열재 (S자 대신 꺾인 선 묶음)",
    pattern_lines((6, 0), (0, 6),
                  [((0, 0), (1.5, 3)), ((1.5, 3), (3, 0)), ((3, 0), (4.5, 3)), ((4.5, 3), (6, 0))]))
add("WATER", "기타", "물 (짧은 가로선 엇갈림)",
    pattern_lines((8, 0), (4, 2), [((0, 0), (3, 0))]))
add("WOOD", "기타", "목재 단면 (가는 선 묶음)",
    parallel(0, 6, 0, 0) + parallel(0, 6, 0, 1) + parallel(0, 6, 0, 2.5, dashes=[6, -2], shift=3))
add("STEEL", "기타", "강재 (45° 두 줄)", parallel(45, 4, 0, 0) + parallel(45, 4, 0, 0.8))

# ----- ANSI: ANSI Y14.2 단면 재료 기호를 본뜬 자체 정의 (45° 기본, 간격 3) -----
d45 = (math.cos(math.radians(45)), math.sin(math.radians(45)))
n45 = (-d45[1], d45[0])


def off45(k):
    """45° 선을 수직으로 k만큼 옮긴 시작점."""
    return (n45[0] * k, n45[1] * k)


add("ANSI31", "ANSI", "일반 재료, 주철 (45°, 간격 3)", parallel(45, 3))
add("ANSI32", "ANSI", "강철 (45° 두 줄 묶음)", parallel(45, 6) + [(45, *off45(1.0), 0, 6, [])])
add("ANSI33", "ANSI", "청동·황동·구리 (실선과 파선 번갈아)",
    parallel(45, 6) + [(45, *off45(3.0), 0, 6, [3, -1.5])])
add("ANSI34", "ANSI", "플라스틱·고무 (네 줄 묶음)",
    parallel(45, 12) + [(45, *off45(k), 0, 12, []) for k in (1.0, 2.0, 3.0)])
add("ANSI35", "ANSI", "내화 재료 (실선과 점 파선)",
    parallel(45, 6) + [(45, *off45(3.0), 0, 6, [5, -1.5, 0, -1.5])])
add("ANSI36", "ANSI", "대리석·석재·유리 (엇갈린 파선)", parallel(45, 3, dashes=[5, -2, 0, -2], shift=2.5))
add("ANSI37", "ANSI", "납·아연·전기 절연체 (교차 45°)", parallel(45, 3) + parallel(135, 3))
add("ANSI38", "ANSI", "알루미늄 (45° 선과 135° 짧은 선)",
    parallel(45, 3) + parallel(135, 6, dashes=[3, -6]))

# ----- ISO: ISO 128 선 모양을 0° 평행선(간격 5)으로 (선 굵기 1 기준 길이: 대시 12, 장대시 24, 단대시 6, 공백 3, 점 0.5) -----
ISO = [
    ("ISO02", "파선", [12, -3]),
    ("ISO03", "간격 넓은 파선", [12, -18]),
    ("ISO04", "일점 장쇄선", [24, -3, 0.5, -3]),
    ("ISO05", "이점 장쇄선", [24, -3, 0.5, -3, 0.5, -3]),
    ("ISO06", "삼점 장쇄선", [24, -3, 0.5, -3, 0.5, -3, 0.5, -3]),
    ("ISO07", "점선", [0.5, -3]),
    ("ISO08", "장단 파선", [24, -3, 6, -3]),
    ("ISO09", "장 이단 파선", [24, -3, 6, -3, 6, -3]),
    ("ISO10", "일점 쇄선", [12, -3, 0.5, -3]),
    ("ISO11", "이중 파선 일점", [12, -3, 12, -3, 0.5, -3]),
    ("ISO12", "파선 이점", [12, -3, 0.5, -3, 0.5, -3]),
    ("ISO13", "이중 파선 이점", [12, -3, 12, -3, 0.5, -3, 0.5, -3]),
    ("ISO14", "파선 삼점", [12, -3, 0.5, -3, 0.5, -3, 0.5, -3]),
    ("ISO15", "이중 파선 삼점", [12, -3, 12, -3, 0.5, -3, 0.5, -3, 0.5, -3]),
]
for name, desc, dash in ISO:
    add(name, "ISO", f"ISO 128 {desc} (간격 5)", parallel(0, 5, dashes=dash, shift=sum(abs(x) for x in dash) / 2))


def write():
    out = [
        "; Free 2D CAD 내장 해치 패턴 (mm). tools/patterns/make_patterns.py로 만든 파일 — 직접 고치지 말고 스크립트를 고친다.",
        "; 모든 패턴은 이 프로젝트에서 직접 정의했다 (ANSI·ISO 탭은 각 표준의 재료 기호·선 모양을 본뜬 자체 정의).",
        "; 형식: 각도, 기준점X, 기준점Y, 이동(선 방향), 간격(수직) [, 대시, 공백, ...]",
        "; 설명 첫 단어 [ANSI] [ISO] [기타]는 패턴 목록 창의 탭.",
    ]
    for name, tab, desc, lines in P:
        out.append(f"*{name}, [{tab}] {desc}")
        for ang, x0, y0, dx, dy, dashes in lines:
            parts = [fmt(ang), fmt(x0), fmt(y0), fmt(dx), fmt(dy)] + [fmt(v) for v in dashes]
            out.append(", ".join(parts))
    OUT.write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"{len(P)} patterns -> {OUT}")


if __name__ == "__main__":
    write()

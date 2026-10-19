# 맞춤법 검사용 영어 단어 목록 만들기 (Free 2D CAD 문자 편집기).
# Hunspell en_US 사전(SCOWL, 허가: README_en_US.txt — 저작권 표시 유지 조건으로 자유 사용·배포)의
# 접두사·접미사 규칙을 펼쳐 단어마다 한 줄인 목록을 gzip으로 저장한다.
# 사용법: python make_wordlist.py <en_US.aff> <en_US.dic> <출력.words.gz>
import gzip
import re
import sys


def parse_aff(path):
    rules = {}  # flag -> (kind, cross, [(strip, add, cond_regex)])
    with open(path, encoding='utf-8') as f:
        lines = [l.rstrip('\n') for l in f]
    i = 0
    while i < len(lines):
        parts = lines[i].split()
        if len(parts) >= 4 and parts[0] in ('PFX', 'SFX') and parts[2] in ('Y', 'N'):
            kind, flag, cross, count = parts[0], parts[1], parts[2] == 'Y', int(parts[3])
            entries = []
            for k in range(count):
                p = lines[i + 1 + k].split()
                strip = '' if p[2] == '0' else p[2]
                add = '' if p[3] == '0' else p[3].split('/')[0]
                cond = p[4] if len(p) > 4 else '.'
                rx = re.compile(('^' + cond) if kind == 'PFX' else (cond + '$'))
                entries.append((strip, add, rx))
            rules[flag] = (kind, cross, entries)
            i += 1 + count
            continue
        i += 1
    return rules


def expand(word, flags, rules):
    out = {word}
    sfx_forms = []
    for fl in flags:
        r = rules.get(fl)
        if not r or r[0] != 'SFX':
            continue
        for strip, add, rx in r[2]:
            if rx.search(word) and (not strip or word.endswith(strip)):
                form = (word[:-len(strip)] if strip else word) + add
                out.add(form)
                if r[1]:
                    sfx_forms.append(form)
    for fl in flags:
        r = rules.get(fl)
        if not r or r[0] != 'PFX':
            continue
        for strip, add, rx in r[2]:
            bases = [word] + (sfx_forms if r[1] else [])
            for b in bases:
                if rx.search(b) and (not strip or b.startswith(strip)):
                    out.add(add + (b[len(strip):] if strip else b))
    return out


def main(aff, dic, dst):
    rules = parse_aff(aff)
    words = set()
    with open(dic, encoding='utf-8') as f:
        next(f)
        for line in f:
            line = line.strip()
            if not line:
                continue
            entry = line.split('\t')[0].split(' ')[0]
            word, _, flags = entry.partition('/')
            if any(c.isdigit() for c in word):
                continue  # 숫자(1st 등)는 검사하지 않음
            if '!' in flags:
                pass  # NOSUGGEST: 맞는 단어지만 추천하지 않음 — 목록에는 둔다
            words |= expand(word, flags, rules)
    words = sorted(w for w in words if w and not any(c.isdigit() for c in w))
    with gzip.open(dst, 'wt', encoding='utf-8', newline='\n') as g:
        g.write('\n'.join(words))
    print(len(words), 'words ->', dst)


if __name__ == '__main__':
    main(*sys.argv[1:4])

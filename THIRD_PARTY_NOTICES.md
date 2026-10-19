# 외부 구성 요소 / Third-party notices

이 저장소와 배포본에는 아래 외부 구성 요소가 들어 있거나 빌드 때 내려받아 씁니다. 각 구성 요소는 자기 라이선스를 따릅니다.

## 소스로 포함

| 구성 요소 | 위치 | 라이선스 |
| :--- | :--- | :--- |
| [ACadSharp](https://github.com/DomCR/ACadSharp) (CSUtilities 포함) — Albert Domenech | `third_party/ACadSharp` | MIT ([LICENSE](third_party/ACadSharp/LICENSE)). 이 프로젝트에서 고친 내용은 [patches/](patches/README.md)에 있습니다. |

## 함께 배포하는 글꼴 (`build/Fonts`)

| 글꼴 | 라이선스 |
| :--- | :--- |
| 나눔고딕·나눔고딕 Bold·나눔명조·나눔바른고딕·D2Coding (NAVER) | SIL Open Font License 1.1 ([OFL-Nanum.txt](build/Fonts/OFL-Nanum.txt)) |
| Noto Sans KR (Google/Adobe) | SIL Open Font License 1.1 ([OFL-NotoSansKR.txt](build/Fonts/OFL-NotoSansKR.txt)) |
| [osifont](https://github.com/hikikomori82/osifont) | GNU LGPL 3 / GPL 3 + font exception ([LGPL-3.0.txt](build/Fonts/LGPL-3.0.txt), [GPL-3.0.txt](build/Fonts/GPL-3.0.txt)) |

SHX 글꼴은 각 제작사 저작물이라 포함하지 않았습니다.

## 해치 패턴·선종류

내장 해치 패턴(`src/Free2DCad.Core/Hatching/Free2DCadPatterns.pat`, `tools/patterns/make_patterns.py`로 생성)과 내장 선종류(`src/Free2DCad.Core/Styles/LinetypeLibrary.cs`)는 이 프로젝트에서 직접 정의한 것으로 MIT 라이선스입니다. 다른 CAD 프로그램의 패턴·선종류 파일은 포함하지 않았습니다. ANSI·ISO 이름의 항목은 각 표준의 재료 기호·선 모양(ISO 128)을 본떠 직접 만든 정의입니다.

## 맞춤법 검사 단어 목록

| 구성 요소 | 위치 | 라이선스 |
| :--- | :--- | :--- |
| en_US Hunspell dictionary from [SCOWL](http://wordlist.sourceforge.net) (2020.12.07) | `src/Free2DCad.App/Assets/Spelling` | SCOWL / Hunspell en_US 라이선스 (허용적, 저작권 고지 유지) — [README_en_US.txt](src/Free2DCad.App/Assets/Spelling/README_en_US.txt) |

## NuGet 패키지 (빌드 때 내려받음)

| 패키지 | 라이선스 |
| :--- | :--- |
| Avalonia.Desktop, Avalonia.Themes.Fluent | MIT |
| SkiaSharp | MIT |
| System.Drawing.Common | MIT |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk (테스트 전용) | Apache-2.0 / MIT |

## 상표

이 프로젝트는 기존 CAD 프로그램을 만드는 어떤 회사와도 관계가 없으며, 그 회사들의 승인이나 후원을 받지 않았습니다. 다른 회사의 이름과 상표는 각 소유자의 것입니다.

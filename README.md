# Free 2D CAD

**Windows에서 쓰는 무료 2D CAD**입니다. DWG·DXF 도면을 열고, 그리고, 고치고, 저장할 수 있습니다.
유료 라이브러리 없이 DWG를 직접 읽고 씁니다. 명령어·단축키·화면은 널리 쓰이는 CAD 한국어판과 비슷하게 만들었습니다.

*A free 2D CAD editor for Windows (C# / .NET 8 / Avalonia). Reads and writes DWG/DXF natively, no paid SDK.*

---

## 이런 걸 할 수 있어요

- **도면 열기·저장**: DWG(R2000 ~ 2018 이후), DXF. 기본 저장 형식은 DWG 2004
- **그리기**: 선·폴리선·원·호·타원·스플라인·해치(빗금)·문자·여러 줄 문자·표·블록
- **치수**: 선형·정렬·각도·호 길이·반지름·지름·세로좌표·기준선·연속, 공차, 지시선
- **편집**: 이동·복사·회전·축척·대칭·자르기·연장·모깎기·배열, 그립 편집, 속성 창
- **관리**: 도면층·문자 스타일·치수 스타일·선종류, 배치(도면 공간)·뷰포트
- **출력**: PDF·이미지

사용법은 [사용 설명서](docs/사용설명서.md)를 보세요.

## 시작하기

### 바로 쓰기 (빌드 없이)
1. [Releases](https://github.com/jihodaya/free-2d-cad/releases/latest)에서 `Free2DCad-버전-win-x64.zip`을 내려받습니다.
2. 압축을 풀고 `Free2DCad` 폴더 안의 `Free2DCad.exe`를 실행합니다. 설치할 것이 없습니다 (Windows 10/11 64비트).
3. Windows가 "PC 보호" 창을 띄우면 **추가 정보 → 실행**을 누르세요.

### 직접 빌드하기
필요한 것: Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```

`dist\Free2DCad\Free2DCad.exe`가 만들어집니다(.NET이 없는 PC에서도 실행되는 파일 하나). 개발할 때는 아래 명령을 씁니다.

```bash
dotnet build Free2DCad.sln -c Release
```

```bash
dotnet test tests/Free2DCad.Core.Tests
```

## ⚠ 배포본에 넣지 않은 것 — 가지고 있으면 넣어서 쓰세요

다른 회사의 저작물은 넣지 않았습니다. 없어도 프로그램은 잘 돌아가지만, **가지고 있는 파일을 실행 파일 옆 폴더에 넣으면** 원래 CAD와 더 똑같이 보입니다.

| 빠진 것 | 넣을 곳 (`Free2DCad.exe` 옆) | 넣으면 | 안 넣으면 |
| :--- | :--- | :--- | :--- |
| SHX 글꼴 (`romans.shx`, `whgtxt.shx` 등) | `Fonts\SHX\` | 도면 문자가 원래 모양·폭으로 | TrueType 글꼴로 대신 그림 |
| 해치 패턴 파일 (가지고 있는 `*.pat`, 회사 패턴) | `Patterns\` | 패턴 목록에 추가, 같은 이름은 그 정의로 | 직접 만든 내장 패턴 47종 |
| 선종류 파일 (가지고 있는 `*.lin`, 회사 선종류) | `Linetypes\` | 선종류 목록에 추가, 같은 이름은 그 정의로 | 직접 만든 내장 선종류 |
| 회사 템플릿 도면 (`*.dwg`, `*.dxf`) | `Templates\` | 새 도면을 회사 양식으로 시작 | 내장 A3 템플릿 |

자세한 방법과 파일을 구하는 곳은 **[빠진 자료 채워 넣기](docs/자료_추가하기.md)** 에 정리해 두었습니다.
다른 CAD에서 만든 도면은 해치·선종류 정의가 도면 안에 들어 있어서, 위 파일이 없어도 원래대로 보입니다.

## 폴더 구성 (개발자용)

| 폴더 | 내용 |
| :--- | :--- |
| `src/Free2DCad.App` | 데스크톱 앱 (리본, 명령줄, 캔버스, 대화상자) |
| `src/Free2DCad.Core` | 도면 문서·명령·편집·스타일·해치·출력 |
| `src/Free2DCad.Rendering` | 도면을 화면에 그리기 (SHX/TrueType 문자, Skia) |
| `tests/Free2DCad.Core.Tests` | 테스트 (xUnit) |
| `tools/Free2DCad.RenderCli` | 명령줄 렌더링 도구 (DWG → PNG) |
| `tools/patterns` | 내장 해치 패턴을 만드는 스크립트 |
| `third_party/ACadSharp` | DWG/DXF 읽기·쓰기 라이브러리 [ACadSharp](https://github.com/DomCR/ACadSharp)(MIT) 소스 + 이 프로젝트의 수정 사항([patches](patches/README.md)) |
| `build` | 배포 스크립트, 함께 배포하는 무료 글꼴 |
| `docs` | 사용 설명서, 자료 추가 안내 |

## 라이선스

- 이 프로젝트의 코드와 내장 해치 패턴·선종류·템플릿: [MIT License](LICENSE). 해치 패턴과 선종류는 모두 이 프로젝트에서 직접 정의했습니다.
- 함께 들어 있는 외부 구성 요소(ACadSharp, 무료 글꼴, 맞춤법 단어 목록 등): [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- **이 프로젝트는 기존 CAD 프로그램을 만드는 어떤 회사와도 관계가 없으며**, 그 회사들의 승인이나 후원을 받지 않았습니다. 다른 회사의 이름과 상표는 각 소유자의 것입니다.

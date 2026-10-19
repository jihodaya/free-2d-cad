# 서드파티 패치

## acadsharp-f2d-fixes.patch

- 기준: ACadSharp master `4cd77590` (2026-09-24), `third_party/ACadSharp` 위치
- 적용: `cd third_party/ACadSharp && git apply ../../patches/acadsharp-f2d-fixes.patch`

| 수정 | 증상 (기존 CAD 기준) |
| :--- | :--- |
| `CadUtils`: `ANSI_949`/`KSC5601`/`ANSI_1361` 코드페이지 이름 추가, 인덱스는 ANSI 쪽(40)을 사용 | 새 도면을 R2004 이하로 저장하면 한글이 `??`로 표시됨 |
| `writeTextStyle`: XRef 블록 핸들을 null로 기록 | AUDIT: TextStyle "XRefBlockId (3) is not Null" 수십 건 |
| `writeLeader`: 연결된 주석(AssociatedAnnotation) 핸들 기록 | AUDIT: Leader "Annotation enabled but Id is Null" |
| XData 문자열(R2004 이하): 길이를 바이트 수로 기록 | AUDIT: Layout/PlotSettings "XData contains invalid groupcode (1192)" (한글 XData) |
| `CadDocument.RemoveCadObject`: 블록 레코드를 지울 때 BLOCK/ENDBLK도 해제 | 블록 작성 후 명령 취소 → 명령 복구 시 'already assigned to a document' 예외 |
| `MText`/`Dimension`: 줄 간격 스타일 기본값 1 | 새 MText/치수 AUDIT 'Line spacing style (0)' 오류 |
| `CadUtils.GetCodePageName`: ANSI_949 대문자 반환 | 다시 읽은 도면의 코드페이지가 'kcs5601'로 표시 |

검증 결과: 실무 도면 2개(저장소에는 넣지 않음)를 AC1018과 AC1015로 다시 저장 → 기존 CAD에서 복구 메시지 없음, AUDIT 오류 0건(원본과 동일).
- `DimensionLinear`/`DimensionAligned`: 측정점이 치수선 위에 있으면 확장선 방향이 0 벡터가 되어 NaN 선이 생겼다. 이 선이 장면 범위를 NaN으로 만들어 화면의 모든 선이 사라졌다 → 유한하지 않거나 길이 0인 확장선은 만들지 않음 (`Dimension.isFiniteLine`).
- `DwgHeaderWriter`: TILEMODELIGHTSYNCH를 문자 '0'(48) 대신 1로 기록 → R2007 이상 DWG의 AUDIT 헤더 오류 해결.
- DXF `writeLeader`: 주석이 없는 지시선은 생성 유형 3(주석 없음)으로, 있으면 340 핸들 기록 → DXF AUDIT "Annotation enabled but Id is Null" 해결.
- DXF `writeExtendedData`: 버전 제한(R2000 이하 이름 31자)으로 APPID 표에서 빠진 앱의 확장 데이터는 쓰지 않음 → DXF 2000을 기존 CAD가 "Invalid RegApp"으로 못 열던 문제 해결.

검증(2026-10-01): test-2를 DWG 2000/2004/2010/2013/2018, DXF 2000/2004/2007/2010/2013/2018, DWT(2004)로 저장 → 기존 CAD에서 모두 복구 메시지 없이 열리고 AUDIT 오류 0건. (DWG R14는 ACadSharp 쓰기 오류로 제외, DWG 2007은 ACadSharp 미지원)
- `CadObjectCollection`: 내부 HashSet을 넣은 순서를 지키는 `OrderedEntrySet`으로 바꾸고 `Reorder` 추가 → 지운 자리에 새 객체가 끼어 그리기 순서가 섞이던 문제 해결, DRAWORDER/HATCHTOBACK/TEXTTOFRONT 구현.
- `Dimension.buildAngularBlock` + `DimensionAngular2Line`/`DimensionAngular3Pt.UpdateBlock`: 각도 치수 블록(호·화살표·연장선·문자)을 직접 만듦 (원래는 블록이 비어 보이지 않았음).
- 테이블(R2007 이하) 쓰기와 `isEntitySupported`의 테이블 제외 해제 → 표가 저장 후 사라지던 문제 해결.
- `DimensionArc`: 블록(호·화살표·보조선·문자)을 각도 치수 빌더로 만들고, 측정값은 시작·끝 각도로 계산(180° 넘는 호), 문자는 각도가 아닌 길이 + 호 기호(⌒).
- `Leader.AssociatedAnnotation`: 공개 setter (지시선 작성 시 주석 MText 연결).
- `GroupCollection.Add`: 무명 그룹 이름을 기존 최대 번호 + 1로 (원래 같은 이름이 중복 생성됨).

검증(2026-10-01): 새 객체 견본(호 길이·세로좌표·연속 치수, 지시선+문자, 그룹, 구성선·광선, 점, 폭 폴리선, 도넛, 구름형, 솔리드, 속성 블록)을 DWG 2004/2018, DXF 2018로 저장 → 기존 CAD에서 복구 메시지 없이 열리고 AUDIT 오류 0건.
- `DimensionAngular2Line`: 교점(Center)을 평면 직선 교점으로 계산(기존 Line3D 교점은 끝점이 맞닿는 경우 NaN → 치수가 안 보였음), 측정값·블록 모두 호 점이 있는 부채꼴 기준(기존 값은 보각이 나왔음).
- `Dimension.buildAngularBlock`: 문자 위치를 기존 CAD 규칙대로 — 수평 문자(DIMTIH)이고 치수선이 수평에 가까우면 DIMTAD "위"는 위쪽(+Y), 기울었으면 호 가운데에 두고 호를 끊음. 문자 각도(TextRotation) 반영.
- `Dimension.dimensionArrow`: 기본 화살촉(SOLID)에 치수선 색 (기존 ByBlock이라 치수선과 색이 달랐음).
- `Dimension.GetMeasurementText`: 반지름·지름 접두어(R, Ø)를 치수 종류 하위 비트로 판단 (문자 위치를 옮긴 치수는 접두어가 빠졌음).
- DWG `writeXRecord` 문자열(R2004 이하): 길이를 바이트 수로 (한글이 있으면 기존 CAD "Object improperly read").

검증(2026-10-01 오후): 다중선·와이프아웃·꺾기 반지름·각도 3종·연관 배열 그룹·도면층 상태(XRecord) 견본을 DWG 2004/2018, DXF 2018로 저장 → 기존 CAD에서 복구 없이 열리고 AUDIT 0건, 각도 치수 배치가 기존 CAD 화면과 같음.
- `DimensionLargeRadial` (새 파일) + DWG/DXF 읽기·쓰기: 꺾기 반지름 치수 LARGE_RADIAL_DIMENSION (AcDbRadialDimensionLarge). DWG 필드 순서 10·15·40·13·14는 기존 CAD가 만든 파일 값과 대조해 확인. 블록 모양(재정의 중심 → 평행선 → DIMJOGANG 사선 → 원 위 점)은 기존 CAD 블록과 같음. 기존에는 기존 CAD 파일을 열면 이 치수가 사라졌음(다시 저장하면 손실).
- `DimensionAligned.UpdateBlock`: 부호 있는 간격 (반대쪽에 놓으면 첫 보조선이 반대로 가서 치수선이 기울었음). `DimensionLinear.UpdateBlock`: 회전 선형 치수의 첫 보조선 끝을 투영으로 계산 (교점이 NaN이라 치수선이 사라졌음).

검증(2026-10-02): 꺾기 반지름 치수 견본 DWG 2018 → 기존 CAD AUDIT 0건, 속성창에서 "꺾임 치수"로 인식(재정의 중심·꺾임 위치 값 일치).
- DXF `writeLeaderLine`: 지시선 색(92)을 씀 (기존 CAD가 쓰는 값). 그리고 Free 2D CAD가 만드는 MULTILEADER는 문맥 데이터 흐름 방향(172)을 가로(1)로 둔다 — 0이면 기존 CAD가 객체를 인식만 하고 그리지 못했음.

검증(2026-10-02): Free 2D CAD MLEADER 견본 DWG 2013/2018 → 기존 CAD AUDIT 0건, 화면·속성(화살촉 4, 착륙 거리 8, 내용, 정렬)이 기존 CAD가 만든 다중지시선과 같음.
- `DwgStreamReaderBase.ReadTextUnicode` (R2004 이하): 문자열 앞의 코드 페이지 값은 목록 순번(ANSI_949 = 40)이므로 순번으로 코드 페이지를 찾는다 — 전에는 순번을 코드 페이지 번호로 써서 XRecord의 한글이 깨지거나(인코딩 없음) 객체를 못 읽었음.

검증(2026-10-02): Free 2D CAD 도면층 상태(기존 CAD 형식 ACAD_LAYERSTATES) 견본 DWG 2004 → 기존 CAD AUDIT 0건, 기존 CAD 도면층 상태 관리자에 목록으로 보임. 기존 CAD가 만든 GCSTATE도 Free 2D CAD에서 그대로 읽힘.
- `Dimension.IsAngular`: 치수 종류를 아래 4비트 값으로 비교 (지름 3이 각도 2 비트를 가져 HasFlag로 각도로 오인 → 지름 치수 문자가 "Ø131.8°"처럼 각도로 나왔음).
- `DimensionDiameter.UpdateBlock`: 문자를 GetMeasurementText로 (임시 "#.##"라 Ø·정밀도가 빠졌음). 반지름·지름 블록에 화살촉(원 위 점) 추가, 원 안 치수선은 DIMTOFL이 켜졌을 때만.

검증(2026-10-06): Free 2D CAD DIMRADIUS·DIMDIAMETER 견본 DWG → 기존 CAD AUDIT 0건, 화면(화살촉·지시선·R20.0/Ø40.0)이 기존 CAD 치수와 같음.
- `Dimension.GetActiveDimensionStyle` + `ApplyStyleOverrideValue`: DSTYLE 재정의(XData "ACAD")를 제대로 적용 — 색상(176/177/178/70)은 색상 번호, 참조(340 문자 스타일, 341~344 화살촉 블록, 345~347 선종류)는 핸들로 문서에서 찾음 (전에는 색상·핸들 재정의가 무시되거나 예외). 복사본은 참조 객체를 복제하지 않음.
- 치수 블록 생성은 `ActiveStyle`(재정의를 적용한 스타일, `createBlock`에서 정함)을 씀 — 전에는 스타일만 보고 치수별 재정의를 무시했음.
- `addLinearDimensionLine` (선형·정렬): 치수선 1·2(DIMSD1/DIMSD2)를 반쪽씩 끄고, 화살촉 블록은 DIMSAH에 따라 DIMBLK1/2 또는 DIMBLK (`FirstArrowBlock`/`SecondArrowBlock`). 정렬 치수의 둘째 화살촉이 첫 점에 그려지던 문제 고침.
- `ExtendedDataDictionary.Remove(name)`: 재정의를 모두 지우면 ACAD 확장 데이터를 없앤다.

검증(2026-10-06): 속성창에서 치수선 색상·문자 색상·문자 높이·화살촉(건축 틱)을 바꾼 치수 DWG → 기존 CAD AUDIT 0건, 기존 CAD 속성창에 같은 값(빨강/파랑/건축 틱)으로 보이고 화면도 같음.
- `Dimension.layoutLinearDimension` (선형·정렬 치수 공통): 기존 CAD 블록과 같은 배치 — 문자는 DIMTAD(위·밖·JIS·아래)만큼 간격 + 글자 높이/2 띄움, 수평 문자를 수직 치수선에 두면 선 위 가운데에 두고 치수선을 끊음, DIMTIH/DIMTOH 수평·정렬 문자 회전, 맞춤(DIMATFIT·DIMTIX·DIMTOFL)으로 화살촉을 보조선 밖으로 + 화살촉 길이 짧은 선, 바깥 문자는 치수선을 문자까지 늘리거나(띄운 문자) 받침선 위에(수직 치수의 수평 문자), 기본 화살촉은 밑변에서 치수선 시작. 사용자 문자 위치는 치수선 위의 점으로 보고 DIMTAD 만큼 띄워 그림 (기존 CAD와 같음). 전에는 문자 가운데가 치수선에서 간격만큼만 떨어져 치수선과 겹치고, 좁은 치수도 화살촉이 안쪽에서 문자와 겹쳤음.
- `Dimension.TextMeasurer`: 앱이 실제 글꼴로 문자 크기를 잰다 (Free2DCad: 글자 모양 범위). 문자 높이·폭이 기존 CAD 블록 값과 0.01 안쪽으로 같음.
- `Dimension.radialLanding` (반지름·지름): 바깥 수평 문자는 화살촉 → 화살촉 길이 지시선 → 수평 받침선(간격 + 문자 폭) → 위에 문자 (기존 CAD 블록과 같음).
- 선형·각도·꺾기 반지름 치수에서 문자 재정의(<>)를 두 번 적용하던 것 고침 (GetMeasurementText가 이미 처리).

검증(2026-10-06 저녁): 같은 좌표로 기존 CAD DIMLINEAR/DIMRADIUS/DIMDIAMETER로 만든 치수와 Free 2D CAD 치수 블록 대조 — 선 끝·화살촉·끊김·받침선·문자 위치가 0.01 안쪽으로 같음(지름 기호 ⌀ 글자 폭만 0.2 차이). 기존 CAD AUDIT 0.
- `Insert.ReplaceBlock(BlockRecord)`: 같은 도면의 다른 블록 정의로 바꾸기 (속성창 블록 참조 "이름"). 속성 값은 그대로.
- `Dimension.ActiveStyleOf(owner, style)` / `StyleOverrideMapOf(owner)`: DSTYLE 재정의 계산을 공통 정적 함수로 — `Leader.GetActiveDimensionStyle()`도 같은 계산 (지시선 화살촉·색·크기 재정의).
- 선형 배치의 맞춤 값을 DIMATFIT(289)로 (전에는 옛 DIMFIT(287)를 봤음).
- `Dimension.GetMeasurementText`: 한계(DIMLIM `\S위^아래;`), 공차(DIMTOL 대칭 `%%p`·편차 `\S+상한^-하한;`, DIMTDEC·DIMTZIN·DIMTFAC), 대체 단위(DIMALT ` [값]`, DIMALTF·DIMALTD·DIMALTZ·DIMAPOST의 `[]`) — 기존 CAD가 다시 그린 블록과 같은 문자열. 쌓기 문자 앞에 DIMTOLJ `\A1;` (없으면 기존 CAD가 위 칸을 띄워 그림).
- 선형 배치: 바깥 문자는 화살촉이 안이어도 둘째 보조선 + 화살촉 길이 + 간격부터. 기본 치수(DIMGAP 음수)는 간격 두 배 띄우고 문자 둘레 상자(선 4개).
- 지름 접두 기본값 ∅(U+2205, 기존 CAD와 같음, 전에는 Ø).
- (Free 2D CAD 쪽, ACadSharp 밖) MTEXT 열 확장 데이터 읽기·쓰기·나눈 열 합치기는 `src/Free2DCad.Rendering/MTextColumns.cs`.
- `DwgStreamWriterBase.WriteVariableText`/`WriteTextUnicode`·DXF ASCII 문자열 (R2004 이하): 코드 페이지에 없는 글자는 `\U+XXXX`로 (기존 CAD와 같음, 전에는 "?"로 깨짐).

검증(2026-10-06 밤): 지시선 치수선 색 재정의(빨강), 다중선 축척 변경, 표 크기 변경을 저장한 DWG → 기존 CAD AUDIT 0건, 화면 같음. 해치 배경 색(HATCHBACKGROUNDCOLOR 0xC3000005)·하이퍼링크(PE_URL) 견본도 기존 CAD에서 파랑 배경·링크로 보임.

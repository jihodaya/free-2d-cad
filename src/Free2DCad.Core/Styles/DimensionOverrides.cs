using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.XData;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Styles;

/// <summary>
/// 치수 하나의 스타일 재정의 (기존 CAD의 XData "ACAD" → "DSTYLE" { 코드, 값 ... }).
/// 속성창에서 치수선 색상·문자 색상 같은 값을 바꾸면 스타일은 그대로 두고 그 치수에만 재정의를 저장한다.
/// 값은 DXF 원시 값: 색상 = 색상 번호(short), 참조 = 핸들(ulong), 예/아니오 = 0/1, 열거형 = 정수.
/// </summary>
public static class DimensionOverrides
{
	public const int DimLineColor = 176;       // DIMCLRD
	public const int ExtLineColor = 177;       // DIMCLRE
	public const int TextColor = 178;          // DIMCLRT
	public const int TextFillColor = 70;       // DIMTFILLCLR
	public const int TextFill = 69;            // DIMTFILL
	public const int TextHeight = 140;         // DIMTXT
	public const int ArrowSize = 41;           // DIMASZ
	public const int TextGap = 147;            // DIMGAP
	public const int ScaleFactor = 40;         // DIMSCALE
	public const int ExtLineExtension = 44;    // DIMEXE
	public const int ExtLineOffset = 42;       // DIMEXO
	public const int CenterMark = 141;         // DIMCEN
	public const int DimLineWeight = 371;      // DIMLWD
	public const int ExtLineWeight = 372;      // DIMLWE
	public const int SuppressDimLine1 = 281;   // DIMSD1
	public const int SuppressDimLine2 = 282;   // DIMSD2
	public const int SuppressExtLine1 = 75;    // DIMSE1
	public const int SuppressExtLine2 = 76;    // DIMSE2
	public const int TextStyleHandle = 340;    // DIMTXSTY
	public const int ArrowBlock = 342;         // DIMBLK
	public const int Arrow1 = 343;             // DIMBLK1
	public const int Arrow2 = 344;             // DIMBLK2
	public const int SeparateArrows = 173;     // DIMSAH
	public const int DimLineType = 345;        // DIMLTYPE
	public const int ExtLineType1 = 346;       // DIMLTEX1
	public const int ExtLineType2 = 347;       // DIMLTEX2
	public const int TextInside = 73;          // DIMTIH
	public const int TextOutside = 74;         // DIMTOH
	public const int TextVertical = 77;        // DIMTAD
	public const int TextHorizontal = 280;     // DIMJUST
	public const int TextInsideExt = 174;      // DIMTIX
	public const int TextOutsideExt = 172;     // DIMTOFL
	public const int Fit = 289;                // DIMATFIT
	public const int TextMove = 279;           // DIMTMOVE
	public const int DecimalPlaces = 271;      // DIMDEC
	public const int Rounding = 45;            // DIMRND
	public const int LinearScale = 144;        // DIMLFAC
	public const int Postfix = 3;              // DIMPOST
	public const int DecimalSeparator = 278;   // DIMDSEP
	public const int ZeroHandling = 78;        // DIMZIN
	public const int LinearUnit = 277;         // DIMLUNIT
	public const int AngularDecimals = 179;    // DIMADEC
	public const int AngularUnit = 275;        // DIMAUNIT
	public const int AltEnabled = 170;         // DIMALT
	public const int AltScale = 143;           // DIMALTF
	public const int AltDecimals = 171;        // DIMALTD
	public const int AltRounding = 148;        // DIMALTRND
	public const int AltPostfix = 4;           // DIMAPOST
	public const int AltUnit = 273;            // DIMALTU
	public const int TolEnabled = 71;          // DIMTOL
	public const int TolLimits = 72;           // DIMLIM
	public const int TolPlus = 47;             // DIMTP
	public const int TolMinus = 48;            // DIMTM
	public const int TolDecimals = 272;        // DIMTDEC
	public const int TolScale = 146;           // DIMTFAC
	public const int TolAlign = 283;           // DIMTOLJ
	public const int DimLineExtension = 46;    // DIMDLE
	public const int FixedExtLine = 290;       // DIMFXLON
	public const int FixedExtLength = 49;      // DIMFXL
	public const int SuppressOutsideDimLines = 175; // DIMSOXD
	public const int AltZeroHandling = 285;    // DIMALTZ
	public const int TolZeroHandling = 284;    // DIMTZIN
	public const int AltTolDecimals = 274;     // DIMALTTD
	public const int AltTolZeroHandling = 286; // DIMALTTZ

	private static readonly DxfClassMap StyleMap = DxfClassMap.Create<DimensionStyle>();

	/// <summary>치수에 저장된 재정의 (코드 → 원시 값). 없으면 빈 사전.</summary>
	public static Dictionary<int, object> Read(Entity dim)
	{
		var result = new Dictionary<int, object>();
		if (!dim.ExtendedData.TryGet(AppId.DefaultName, out ExtendedData edata))
		{
			return result;
		}
		var records = edata.Records;
		int start = FindSection(records);
		if (start < 0)
		{
			return result;
		}
		for (int i = start + 2; i + 1 < records.Count; i += 2)
		{
			if (records[i] is ExtendedDataControlString)
			{
				break;
			}
			if (records[i] is ExtendedDataInteger16 code)
			{
				result[code.Value] = records[i + 1].RawValue;
			}
		}
		return result;
	}

	/// <summary>재정의 전체를 쓴다 (비면 DSTYLE 구역을 지운다). ACAD 앱의 다른 데이터는 그대로 둔다.</summary>
	public static void Write(Entity dim, IReadOnlyDictionary<int, object> values)
	{
		ExtendedData? edata = dim.ExtendedData.TryGet(AppId.DefaultName, out var found) ? found : null;
		var before = new List<ExtendedDataRecord>();
		var after = new List<ExtendedDataRecord>();
		if (edata != null)
		{
			var records = edata.Records;
			int start = FindSection(records);
			if (start < 0)
			{
				before.AddRange(records);
			}
			else
			{
				before.AddRange(records.Take(start));
				int end = start + 2;
				while (end < records.Count && records[end] is not ExtendedDataControlString)
				{
					end += 2;
				}
				after.AddRange(records.Skip(end + 1));
			}
		}

		var section = new List<ExtendedDataRecord>();
		if (values.Count > 0)
		{
			section.Add(new ExtendedDataString(DimensionStyle.StyleOverrideEntryName));
			section.Add(new ExtendedDataControlString(false));
			foreach (var (code, value) in values.OrderBy(kv => kv.Key))
			{
				if (!StyleMap.DxfProperties.TryGetValue(code, out DxfProperty? prop))
				{
					continue;
				}
				section.Add(new ExtendedDataInteger16((short)code));
				section.Add(ExtendedDataRecord.Create(prop.GroupCode, value));
			}
			section.Add(new ExtendedDataControlString(true));
		}

		var all = before.Concat(section).Concat(after).ToList();
		if (all.Count == 0)
		{
			dim.ExtendedData.Remove(AppId.DefaultName);
			return;
		}
		edata ??= dim.ExtendedData.TryAdd(AppId.DefaultName, new ExtendedData());
		edata.Records.Clear();
		edata.Records.AddRange(all);
	}

	/// <summary>
	/// 치수들에 재정의 하나를 넣거나(value) 지우고(null) 치수 블록을 다시 만드는 되돌리기 가능한 동작.
	/// 값이 스타일 값과 같으면 재정의를 지운다 (기존 CAD처럼 불필요한 재정의를 남기지 않음).
	/// </summary>
	public static IUndoableAction SetAction(IReadOnlyList<Entity> dims, int code, object? value, string name)
	{
		return SetAction(dims, new Dictionary<int, object?> { [code] = value }, name);
	}

	/// <summary>재정의 여러 개를 한 번에 (예: 화살촉 1 = DIMSAH + DIMBLK1 + DIMBLK2).</summary>
	public static IUndoableAction SetAction(IReadOnlyList<Entity> dims, IReadOnlyDictionary<int, object?> changes, string name)
	{
		var actions = new List<IUndoableAction>();
		foreach (var dim in dims)
		{
			var oldMap = Read(dim);
			var newMap = new Dictionary<int, object>(oldMap);
			foreach (var (code, value) in changes)
			{
				object? styleRaw = RawOf(StyleOf(dim), code);
				if (value == null || Equals(Normalize(value), Normalize(styleRaw)))
				{
					newMap.Remove(code);
				}
				else
				{
					newMap[code] = value;
				}
			}
			actions.Add(new EntityEditAction(name, dim,
				() => { Write(dim, newMap); (dim as Dimension)?.UpdateBlock(); },
				() => { Write(dim, oldMap); (dim as Dimension)?.UpdateBlock(); }));
		}
		return actions.Count == 1 ? actions[0] : new CompositeAction(name, actions);
	}

	/// <summary>치수 스타일 바꾸기 (재정의는 그대로 둔다, 블록 다시 만들기).</summary>
	public static IUndoableAction StyleAction(IReadOnlyList<Dimension> dims, DimensionStyle style, string name = "치수 스타일 변경")
	{
		var actions = dims.Select(dim =>
		{
			var old = dim.Style;
			return (IUndoableAction)new EntityEditAction(name, dim,
				() => { dim.Style = style; dim.UpdateBlock(); },
				() => { dim.Style = old; dim.UpdateBlock(); });
		}).ToList();
		return actions.Count == 1 ? actions[0] : new CompositeAction(name, actions);
	}

	/// <summary>치수 값 하나를 바꾸고 블록을 다시 만드는 동작 (문자 위치·회전·재정의 문자 등).</summary>
	public static IUndoableAction EditAction<T>(IReadOnlyList<Dimension> dims, Func<Dimension, T> get, Action<Dimension, T> set, T value, string name)
	{
		var actions = dims.Select(dim =>
		{
			T old = get(dim);
			return (IUndoableAction)new EntityEditAction(name, dim,
				() => { set(dim, value); dim.UpdateBlock(); },
				() => { set(dim, old); dim.UpdateBlock(); });
		}).ToList();
		return actions.Count == 1 ? actions[0] : new CompositeAction(name, actions);
	}

	/// <summary>화살촉 종류 (속성창 표시 이름, 블록 이름; "" = 닫힌 채움). 기존 CAD 속성창 이름. 기존 CAD의 DIMBLK 이름.</summary>
	public static readonly (string Name, string Block)[] Arrows =
	{
		("닫힌 채움", ""), ("닫힌 빈칸", "_ClosedBlank"), ("닫힘", "_Closed"), ("점", "_Dot"), ("건축 틱", "_ArchTick"),
		("사선", "_Oblique"), ("열림", "_Open"), ("원점 지시기", "_Origin"), ("원점 지시기 2", "_Origin2"), ("직각", "_Open90"),
		("열린 30", "_Open30"), ("작은 점", "_DotSmall"), ("점 빈칸", "_DotBlank"), ("작은 점 빈칸", "_Small"), ("상자", "_BoxBlank"),
		("채워진 상자", "_BoxFilled"), ("데이텀 삼각형", "_DatumBlank"), ("채워진 데이텀 삼각형", "_DatumFilled"), ("적분", "_Integral"), ("없음", "_None"),
	};

	/// <summary>화살촉 표시 이름 (블록 없음 = 닫고 채움, 모르는 블록은 그 이름).</summary>
	public static string ArrowName(BlockRecord? block)
	{
		if (block == null || string.IsNullOrEmpty(block.Name))
		{
			return Arrows[0].Name;
		}
		foreach (var (name, b) in Arrows)
		{
			if (string.Equals(b, block.Name, StringComparison.OrdinalIgnoreCase))
			{
				return name;
			}
		}
		return block.Name;
	}

	/// <summary>화살촉 블록을 도면에서 찾고 없으면 만든다 ("" = null = 닫고 채움).</summary>
	public static BlockRecord? GetOrCreateArrow(CadDocument doc, string blockName)
	{
		if (string.IsNullOrEmpty(blockName))
		{
			return null;
		}
		var existing = doc.BlockRecords.FirstOrDefault(b => string.Equals(b.Name, blockName, StringComparison.OrdinalIgnoreCase));
		if (existing != null)
		{
			return existing;
		}
		BlockRecord? created = blockName.ToUpperInvariant() switch
		{
			"_CLOSEDBLANK" => ACadSharp.Blocks.DimensionArrowhead.ClosedBlank,
			"_CLOSED" => ACadSharp.Blocks.DimensionArrowhead.Closed,
			"_DOT" => ACadSharp.Blocks.DimensionArrowhead.Dot,
			"_ARCHTICK" => ACadSharp.Blocks.DimensionArrowhead.ArchitecturalTick,
			"_OBLIQUE" => ACadSharp.Blocks.DimensionArrowhead.Oblique,
			"_OPEN" => ACadSharp.Blocks.DimensionArrowhead.Open,
			"_ORIGIN" => ACadSharp.Blocks.DimensionArrowhead.OriginIndicator,
			"_ORIGIN2" => ACadSharp.Blocks.DimensionArrowhead.OriginIndicator2,
			"_OPEN90" => ACadSharp.Blocks.DimensionArrowhead.Open90,
			"_OPEN30" => ACadSharp.Blocks.DimensionArrowhead.Open30,
			"_DOTSMALL" => ACadSharp.Blocks.DimensionArrowhead.DotSmall,
			"_DOTBLANK" => ACadSharp.Blocks.DimensionArrowhead.DotBlank,
			"_SMALL" => ACadSharp.Blocks.DimensionArrowhead.DotSmallBlank,
			"_BOXBLANK" => ACadSharp.Blocks.DimensionArrowhead.BoxBlank,
			"_BOXFILLED" => ACadSharp.Blocks.DimensionArrowhead.BoxFilled,
			"_DATUMBLANK" => ACadSharp.Blocks.DimensionArrowhead.DatumTriangle,
			"_DATUMFILLED" => ACadSharp.Blocks.DimensionArrowhead.DatumFilled,
			"_INTEGRAL" => ACadSharp.Blocks.DimensionArrowhead.Integral,
			"_NONE" => ACadSharp.Blocks.DimensionArrowhead.None,
			_ => null,
		};
		if (created == null)
		{
			return null;
		}
		created.Name = blockName;
		// 꼭짓점 둘인 폴리선은 닫지 않는다 (기존 CAD의 _ArchTick과 같음 — ACadSharp 기본값은 닫힘이라 같은 선을 두 번 그림)
		foreach (var pl in created.Entities.OfType<ACadSharp.Entities.LwPolyline>().Where(p => p.Vertices.Count == 2 && p.IsClosed))
		{
			pl.IsClosed = false;
		}
		doc.BlockRecords.Add(created);
		return created;
	}

	/// <summary>재정의를 적용한 현재 값 (원시 값).</summary>
	public static object? Effective(Entity dim, int code)
	{
		return Read(dim).TryGetValue(code, out var v) ? v : RawOf(StyleOf(dim), code);
	}

	/// <summary>재정의가 있는지.</summary>
	public static bool IsOverridden(Entity dim, int code) => Read(dim).ContainsKey(code);

	/// <summary>치수·지시선의 치수 스타일.</summary>
	public static DimensionStyle? StyleOf(Entity e) => e switch
	{
		Dimension d => d.Style,
		Leader l => l.Style,
		_ => null,
	};

	/// <summary>스타일의 원시 값 (색상 = 번호, 참조 = 핸들).</summary>
	public static object? RawOf(DimensionStyle? style, int code)
	{
		if (style == null)
		{
			return null;
		}
		switch (code)
		{
			case DimLineColor: return style.DimensionLineColor.Index;
			case ExtLineColor: return style.ExtensionLineColor.Index;
			case TextColor: return style.TextColor.Index;
			case TextFillColor: return style.TextBackgroundColor.Index;
			case TextStyleHandle: return style.Style?.Handle ?? 0UL;
			case 341: return style.LeaderArrow?.Handle ?? 0UL;
			case ArrowBlock: return style.ArrowBlock?.Handle ?? 0UL;
			case Arrow1: return style.DimArrow1?.Handle ?? 0UL;
			case Arrow2: return style.DimArrow2?.Handle ?? 0UL;
			case DimLineType: return style.LineType?.Handle ?? 0UL;
			case ExtLineType1: return style.LineTypeExt1?.Handle ?? 0UL;
			case ExtLineType2: return style.LineTypeExt2?.Handle ?? 0UL;
		}
		if (!StyleMap.DxfProperties.TryGetValue(code, out DxfProperty? prop))
		{
			return null;
		}
		return prop.GetValue(style);
	}

	/// <summary>비교용 정규화 (bool/열거형/정수 → long, 실수 → double).</summary>
	private static object? Normalize(object? v)
	{
		return v switch
		{
			null => null,
			bool b => b ? 1L : 0L,
			Enum e => Convert.ToInt64(e),
			short or int or long or byte or ushort or ulong or char => Convert.ToInt64(v),
			float or double => Math.Round(Convert.ToDouble(v), 9),
			_ => v,
		};
	}

	private static int FindSection(List<ExtendedDataRecord> records)
	{
		for (int i = 0; i + 1 < records.Count; i++)
		{
			if (records[i] is ExtendedDataString s && s.Value == DimensionStyle.StyleOverrideEntryName
				&& records[i + 1] is ExtendedDataControlString)
			{
				return i;
			}
		}
		return -1;
	}
}

using ACadSharp.Entities;
using ACadSharp.XData;

namespace Free2DCad.Rendering;

/// <summary>
/// 여러 줄 문자의 열 (기존 CAD "열"/"칼럼 설정"). Type 0 없음, 1 정적, 2 동적.
/// 정적·동적 자동 높이는 모든 열이 DefinedHeight, 동적 수동 높이는 열마다 Heights[i]. 마지막 열은 남은 글을 모두 담는다.
/// </summary>
public sealed record MTextColumnInfo(short Type, bool AutoHeight, int Count, bool FlowReversed, double Width, double Gutter, double[] Heights, double DefinedHeight)
{
	/// <summary>열 i의 높이 (0 이하 = 제한 없음).</summary>
	public double HeightOf(int i) =>
		this.Type == 2 && !this.AutoHeight ? (i < this.Heights.Length ? this.Heights[i] : 0) : this.DefinedHeight;

	/// <summary>열 수 (정적은 Count, 동적은 높이 목록 수와 Count 중 큰 값, 최소 1).</summary>
	public int Columns => Math.Max(1, this.Type == 2 && !this.AutoHeight ? Math.Max(this.Count, this.Heights.Length) : this.Count);

	/// <summary>글상자 전체 폭 = 열 폭 × 열 수 + 간격 × (열 수 - 1).</summary>
	public double TotalWidth => this.Width * this.Columns + this.Gutter * (this.Columns - 1);

	/// <summary>
	/// 기존 CAD가 R2004로 저장하며 둘째 열부터 따로 나눈 여러 줄 문자 (ACAD_MTEXT_COLUMNS = 나머지 열 문자들의 핸들).
	/// 이 문자는 첫 열 글만 가지고 있으므로 열 폭으로 줄만 나누고 다음 열로 넘기지 않는다.
	/// </summary>
	public bool Split { get; init; }
}

/// <summary>
/// 열 정보 읽기·쓰기. 기존 CAD는 R2004 도면에 기존 CAD와 같은 확장 데이터 "ACAD"로 저장한다:
/// ACAD_MTEXT_COLUMN_INFO_BEGIN, 75 종류, 79 자동 높이, 76 열 수, 78 흐름 반대, 48 열 폭, 49 간격, 50 높이 수 + 높이들, ..._END,
/// ACAD_MTEXT_DEFINED_HEIGHT_BEGIN, 46 높이, ..._END. (R2007 이상 파일은 MText.ColumnData)
/// </summary>
public static class MTextColumns
{
	private const string App = "ACAD";
	private const string ColBegin = "ACAD_MTEXT_COLUMN_INFO_BEGIN";
	private const string ColEnd = "ACAD_MTEXT_COLUMN_INFO_END";
	private const string HBegin = "ACAD_MTEXT_DEFINED_HEIGHT_BEGIN";
	private const string HEnd = "ACAD_MTEXT_DEFINED_HEIGHT_END";

	/// <summary>열 정보 (없거나 열 없음이면 null).</summary>
	public static MTextColumnInfo? Read(MText m)
	{
		if (m.ExtendedData.TryGet(App, out ExtendedData data))
		{
			var recs = data.Records;
			int b = recs.FindIndex(r => r is ExtendedDataString s && s.Value == ColBegin);
			if (b >= 0)
			{
				short type = 0, auto = 0, count = 0, flow = 0;
				double width = 0, gutter = 0, defined = 0;
				var heights = new List<double>();
				for (int i = b + 1; i + 1 < recs.Count; i++)
				{
					if (recs[i] is ExtendedDataString { Value: ColEnd })
					{
						break;
					}
					if (recs[i] is not ExtendedDataInteger16 code)
					{
						continue;
					}
					switch (code.Value)
					{
						case 75: type = Short(recs[++i]); break;
						case 79: auto = Short(recs[++i]); break;
						case 76: count = Short(recs[++i]); break;
						case 78: flow = Short(recs[++i]); break;
						case 48: width = Real(recs[++i]); break;
						case 49: gutter = Real(recs[++i]); break;
						case 50:
							{
								int n = Short(recs[++i]);
								for (int k = 0; k < n && i + 1 < recs.Count; k++)
								{
									heights.Add(Real(recs[++i]));
								}
								break;
							}
					}
				}
				int hb = recs.FindIndex(r => r is ExtendedDataString s && s.Value == HBegin);
				if (hb >= 0 && hb + 2 < recs.Count && recs[hb + 1] is ExtendedDataInteger16 { Value: 46 })
				{
					defined = Real(recs[hb + 2]);
				}
				if (type is 1 or 2 && width > 0)
				{
					bool split = recs.Any(r => r is ExtendedDataString { Value: "ACAD_MTEXT_COLUMNS_BEGIN" });
					return new MTextColumnInfo(type, auto != 0, count, flow != 0, width, gutter, heights.ToArray(), defined > 0 ? defined : m.RectangleHeight) { Split = split };
				}
				return null;
			}
		}
		var cd = m.ColumnData;
		if (cd != null && cd.ColumnType != ColumnType.NoColumns && cd.Width > 0)
		{
			return new MTextColumnInfo((short)cd.ColumnType, cd.AutoHeight, cd.ColumnCount, cd.FlowReversed, cd.Width, cd.Gutter, cd.Heights.ToArray(), m.RectangleHeight);
		}
		return null;
	}

	/// <summary>열 정보 바꾸기 (null = 열 없음). 다른 "ACAD" 확장 데이터는 그대로 둔다. 글상자 폭은 전체 폭으로.</summary>
	public static void Write(MText m, MTextColumnInfo? info)
	{
		var data = m.ExtendedData.TryGet(App, out ExtendedData existing) ? existing : null;
		if (data != null)
		{
			RemoveSection(data.Records, ColBegin, ColEnd);
			RemoveSection(data.Records, "ACAD_MTEXT_COLUMNS_BEGIN", "ACAD_MTEXT_COLUMNS_END");
			RemoveSection(data.Records, HBegin, HEnd);
		}
		if (info == null || info.Type == 0)
		{
			if (data != null && data.Records.Count == 0)
			{
				m.ExtendedData.Remove(App);
			}
			return;
		}
		data ??= m.ExtendedData.TryAdd(App, new ExtendedData());
		var recs = new List<ExtendedDataRecord>
		{
			new ExtendedDataString(ColBegin),
			new ExtendedDataInteger16(75), new ExtendedDataInteger16(info.Type),
			new ExtendedDataInteger16(79), new ExtendedDataInteger16((short)(info.AutoHeight ? 1 : 0)),
			new ExtendedDataInteger16(76), new ExtendedDataInteger16((short)info.Count),
			new ExtendedDataInteger16(78), new ExtendedDataInteger16((short)(info.FlowReversed ? 1 : 0)),
			new ExtendedDataInteger16(48), new ExtendedDataReal(info.Width),
			new ExtendedDataInteger16(49), new ExtendedDataReal(info.Gutter),
			new ExtendedDataInteger16(50), new ExtendedDataInteger16((short)info.Heights.Length),
		};
		recs.AddRange(info.Heights.Select(h => (ExtendedDataRecord)new ExtendedDataReal(h)));
		recs.Add(new ExtendedDataString(ColEnd));
		recs.Add(new ExtendedDataString(HBegin));
		recs.Add(new ExtendedDataInteger16(46));
		recs.Add(new ExtendedDataReal(info.DefinedHeight));
		recs.Add(new ExtendedDataString(HEnd));
		data.Records.AddRange(recs);
		m.RectangleWidth = info.TotalWidth;
		m.RectangleHeight = info.DefinedHeight;
	}

	/// <summary>
	/// R2004 도면에 열을 나눠 저장한 여러 줄 문자(첫 열 + ACAD_MTEXT_COLUMNS가 가리키는 나머지 열 문자들)를
	/// 기존 CAD가 열 때처럼 하나로 합친다: 나머지 열 문자의 글을 첫 문자 뒤에 잇고 그 문자들은 지운다. 합친 개수를 돌려준다.
	/// </summary>
	public static int MergeSplit(ACadSharp.CadDocument doc)
	{
		int merged = 0;
		var all = doc.Entities.OfType<MText>().Concat(doc.BlockRecords.SelectMany(b => b.Entities.OfType<MText>())).Distinct().ToList();
		foreach (var m in all)
		{
			if (!m.ExtendedData.TryGet(App, out ExtendedData data))
			{
				continue;
			}
			var recs = data.Records;
			int b = recs.FindIndex(r => r is ExtendedDataString { Value: "ACAD_MTEXT_COLUMNS_BEGIN" });
			if (b < 0)
			{
				continue;
			}
			int e = recs.FindIndex(b, r => r is ExtendedDataString { Value: "ACAD_MTEXT_COLUMNS_END" });
			var children = recs.Skip(b + 1).Take((e < 0 ? recs.Count : e) - b - 1).OfType<ExtendedDataHandle>()
				.Select(h => doc.GetCadObject(h.Value) as MText).Where(c => c != null && !ReferenceEquals(c, m)).Cast<MText>().ToList();
			foreach (var child in children)
			{
				m.Value += child.Value;
				try
				{
					if (child.Owner is ACadSharp.Tables.BlockRecord owner)
					{
						owner.Entities.Remove(child);
					}
					else
					{
						doc.Entities.Remove(child);
					}
				}
				catch (Exception)
				{
					// 지울 수 없으면 그대로 (글이 두 번 보일 수 있음)
				}
			}
			RemoveSection(recs, "ACAD_MTEXT_COLUMNS_BEGIN", "ACAD_MTEXT_COLUMNS_END");
			merged++;
		}
		return merged;
	}

	private static void RemoveSection(List<ExtendedDataRecord> recs, string begin, string end)
	{
		int b = recs.FindIndex(r => r is ExtendedDataString s && s.Value == begin);
		if (b < 0) return;
		int e = recs.FindIndex(b, r => r is ExtendedDataString s && s.Value == end);
		recs.RemoveRange(b, (e < 0 ? recs.Count - 1 : e) - b + 1);
	}

	private static short Short(ExtendedDataRecord r) => r switch
	{
		ExtendedDataInteger16 i => i.Value,
		ExtendedDataInteger32 i => (short)i.Value,
		_ => 0,
	};

	private static double Real(ExtendedDataRecord r) => r switch
	{
		ExtendedDataReal d => d.Value,
		ExtendedDataInteger16 i => i.Value,
		_ => 0,
	};
}

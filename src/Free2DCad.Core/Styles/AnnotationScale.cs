using System.Runtime.CompilerServices;
using ACadSharp;
using ACadSharp.Tables;

namespace Free2DCad.Core.Styles;

/// <summary>
/// 주석 축척 (상태 표시줄 "1:1"): 새로 만드는 문자·치수·지시선의 크기 배율.
/// 예) 1:50이면 출력 높이 3.5의 문자를 도면에 175 높이로 만든다 (모형 공간 1:1 작도 후 1:50 출력).
/// 치수·지시선은 현재 치수 스타일을 복사해 전체 축척(DIMSCALE)만 곱한 스타일("스타일 이름 1-50")을 쓴다.
/// </summary>
public static class AnnotationScale
{
	private static readonly ConditionalWeakTable<CadDocument, Box> Scales = new();

	private sealed class Box
	{
		public double Value = 1;
	}

	/// <summary>표준 축척 목록 (이름, 배율 = 도면 단위 / 출력 단위).</summary>
	public static readonly (string Label, double Factor)[] Standard =
	{
		("1:1", 1), ("1:2", 2), ("1:4", 4), ("1:5", 5), ("1:8", 8), ("1:10", 10), ("1:16", 16), ("1:20", 20),
		("1:25", 25), ("1:30", 30), ("1:40", 40), ("1:50", 50), ("1:100", 100), ("1:200", 200), ("1:500", 500),
		("2:1", 0.5), ("4:1", 0.25), ("5:1", 0.2), ("10:1", 0.1),
	};

	/// <summary>자동 제안에 쓰는 흔한 축척 (1:2 ~ 1:500).</summary>
	private static readonly double[] SuggestFactors = { 2, 5, 10, 20, 50, 100, 200, 500 };

	/// <summary>
	/// 주석 축척 자동 제안: 그린 도형 범위(width × height)가 용지(sheet, 새 도면 = A3 420×297)보다 2배 넘게 크면
	/// 그 범위가 용지에 들어가는 가장 작은 흔한 축척(1:2·1:5·1:10·1:20·1:50·…)을 돌려준다. 제안할 것이 없으면 null.
	/// 예) 4,200 × 2,000 mm를 그린 A3 도면 → 1:10 (문자 2.5가 도면에서 25로 만들어져 화면에서 읽을 만한 크기).
	/// </summary>
	public static double? Suggest(double width, double height, double sheetWidth, double sheetHeight)
	{
		if (!(width > 0) && !(height > 0) || !(sheetWidth > 0) || !(sheetHeight > 0))
		{
			return null;
		}
		double ratio = Math.Max(width / sheetWidth, height / sheetHeight);
		if (ratio < 2)
		{
			return null;
		}
		foreach (double f in SuggestFactors)
		{
			if (f >= ratio * 0.95)
			{
				return f;
			}
		}
		return SuggestFactors[^1];
	}

	/// <summary>지금 주석 축척. 도면에 저장된 CANNOSCALE(기존 CAD와 같은 자리)을 처음 한 번 읽는다.</summary>
	public static double Get(CadDocument doc)
	{
		if (Scales.TryGetValue(doc, out var b)) return b.Value;
		double f = ReadStored(doc) ?? 1.0;
		Scales.GetOrCreateValue(doc).Value = f;
		return f;
	}

	/// <summary>주석 축척을 바꾸고 도면에도 저장 (변수 사전 CANNOSCALE + 축척 목록).</summary>
	public static void Set(CadDocument doc, double factor)
	{
		if (!(factor > 0)) return;
		Scales.GetOrCreateValue(doc).Value = factor;
		try
		{
			Store(doc, factor);
		}
		catch (Exception)
		{
			// 저장 자리를 만들 수 없는 도면: 이번 실행 동안만 기억
		}
	}

	private static double? ReadStored(CadDocument doc)
	{
		try
		{
			if (doc.RootDictionary.TryGetEntry<ACadSharp.Objects.CadDictionary>(ACadSharp.Objects.CadDictionary.VariableDictionary, out var vars)
				&& vars.TryGetEntry<ACadSharp.Objects.DictionaryVariable>(ACadSharp.Objects.DictionaryVariable.CurrentAnnotationScale, out var v)
				&& !string.IsNullOrEmpty(v.Value))
			{
				// 축척 목록에서 같은 이름을 찾으면 그 값, 아니면 "1:50" 같은 이름을 풀어서
				var sc = doc.Scales.FirstOrDefault(x => string.Equals(x.Name, v.Value, StringComparison.OrdinalIgnoreCase));
				if (sc != null && sc.PaperUnits > 0 && sc.DrawingUnits > 0) return sc.DrawingUnits / sc.PaperUnits;
				return Parse(v.Value);
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	/// <summary>"1:50" → 50, "2:1" → 0.5.</summary>
	public static double? Parse(string label)
	{
		var parts = label.Split(':');
		if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double paper)
			&& double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double drawing)
			&& paper > 0 && drawing > 0)
		{
			return drawing / paper;
		}
		return null;
	}

	private static void Store(CadDocument doc, double factor)
	{
		string label = Label(factor);
		if (!doc.Scales.Any(x => string.Equals(x.Name, label, StringComparison.OrdinalIgnoreCase)))
		{
			bool enlarge = factor < 1;
			doc.Scales.Add(new ACadSharp.Objects.Scale(label)
			{
				PaperUnits = enlarge ? 1 / factor : 1,
				DrawingUnits = enlarge ? 1 : factor,
				IsUnitScale = Math.Abs(factor - 1) < 1e-12,
			});
		}
		var root = doc.RootDictionary;
		if (!root.TryGetEntry<ACadSharp.Objects.CadDictionary>(ACadSharp.Objects.CadDictionary.VariableDictionary, out var vars))
		{
			vars = new ACadSharp.Objects.CadDictionary(ACadSharp.Objects.CadDictionary.VariableDictionary);
			root.Add(vars);
		}
		if (vars.TryGetEntry<ACadSharp.Objects.DictionaryVariable>(ACadSharp.Objects.DictionaryVariable.CurrentAnnotationScale, out var v))
		{
			v.Value = label;
		}
		else
		{
			vars.Add(new ACadSharp.Objects.DictionaryVariable(ACadSharp.Objects.DictionaryVariable.CurrentAnnotationScale, label));
		}
	}

	public static string Label(double factor)
	{
		foreach (var (label, f) in Standard)
		{
			if (Math.Abs(f - factor) < 1e-9) return label;
		}
		return factor >= 1 ? $"1:{factor:0.###}" : $"{1 / factor:0.###}:1";
	}

	/// <summary>주석 축척을 반영한 치수 스타일 (1:1이면 원래 스타일, 아니면 축척 스타일을 만들거나 찾아서).</summary>
	public static DimensionStyle Apply(CadDocument doc, DimensionStyle style)
	{
		double f = Get(doc);
		if (Math.Abs(f - 1) < 1e-12) return style;
		string name = $"{style.Name} {Label(f).Replace(':', '-')}";
		if (doc.DimensionStyles.TryGetValue(name, out var existing)) return existing;
		var copy = (DimensionStyle)style.Clone();
		copy.Name = name;
		copy.ScaleFactor = (style.ScaleFactor > 0 ? style.ScaleFactor : 1.0) * f;
		doc.DimensionStyles.Add(copy);
		return copy;
	}
}

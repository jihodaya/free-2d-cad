using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Styles;
using Ov = Free2DCad.Core.Styles.DimensionOverrides;
using Color = ACadSharp.Color;

namespace Free2DCad.App;

/// <summary>
/// 치수 속성 (기존 CAD와 같은 분류): 기타(치수 스타일), 선 화살촉, 텍스트, 맞춤, 기본 단위, 대체 단위, 허용 오차.
/// 값을 바꾸면 스타일은 그대로 두고 그 치수에만 재정의(DSTYLE)를 저장하고 치수 블록을 다시 만든다 (되돌리기 가능).
/// </summary>
public sealed partial class PropertyPanel
{
	private sealed record DimChoice(string Name, IReadOnlyDictionary<int, object?> Values);

	private void BuildDimensionProperties(IReadOnlyList<Dimension> dims)
	{
		var doc = this._doc;
		if (doc == null || dims.Count == 0)
		{
			return;
		}
		var cad = doc.Document;
		var d0 = dims[0];
		bool radial = dims.All(d => d is DimensionRadius or DimensionDiameter);
		bool linear = dims.All(d => d is DimensionLinear or DimensionAligned);

		// 1) 기타: 치수 스타일, 주석
		var misc = new StackPanel { Spacing = 0 };
		var styleNames = cad.DimensionStyles.Select(s => s.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
		var curStyles = dims.Select(d => d.Style?.Name ?? "Standard").Distinct().ToList();
		string curStyle = curStyles.Count == 1 ? curStyles[0] : Varies;
		if (curStyle == Varies) styleNames.Insert(0, Varies);
		this.AddPropertyRow(misc, "치수 스타일", this.CreateSimpleDropdown(styleNames, curStyle, name =>
		{
			if (cad.DimensionStyles.TryGetValue(name, out var st))
			{
				doc.Execute(Ov.StyleAction(dims, st));
			}
		}));
		this.AddPropertyRow(misc, "주석", "아니오");
		this._categoriesPanel.Children.Add(this.CreateCategorySection("기타", misc));

		// 2) 선 화살촉 (기존 CAD 순서·이름: 치수보조선 = "연장선")
		var lines = new StackPanel { Spacing = 0 };
		this.DimArrowRow(lines, dims, "화살촉 1", first: true);
		this.DimArrowRow(lines, dims, "화살촉 2", first: false);
		this.DimNumberRow(lines, dims, "화살촉 크기", Ov.ArrowSize, v => v >= 0);
		if (radial)
		{
			double cen = Convert.ToDouble(Ov.Effective(d0, Ov.CenterMark) ?? 0.0, CultureInfo.InvariantCulture);
			double size = Math.Abs(cen) > 1e-12 ? Math.Abs(cen) : 2.5;
			this.DimChoiceRow(lines, dims, "중심 표시", Ov.CenterMark, new[]
			{
				new DimChoice("없음", new Dictionary<int, object?> { [Ov.CenterMark] = 0.0 }),
				new DimChoice("표시", new Dictionary<int, object?> { [Ov.CenterMark] = size }),
				new DimChoice("선", new Dictionary<int, object?> { [Ov.CenterMark] = -size }),
			}, v => Math.Abs(Convert.ToDouble(v, CultureInfo.InvariantCulture)) < 1e-12 ? "없음" : Convert.ToDouble(v, CultureInfo.InvariantCulture) > 0 ? "표시" : "선");
			this.AddSharedNumericRow(lines, "중심 표시 크기", Math.Abs(cen), v =>
			{
				if (v < 0) return;
				doc.Execute(Ov.SetAction(dims, Ov.CenterMark, cen < 0 ? -v : v, "중심 표시 크기 변경"));
			});
		}
		this.DimWeightRow(lines, dims, "치수선 선 두께", Ov.DimLineWeight);
		this.DimWeightRow(lines, dims, "연장선 선 두께", Ov.ExtLineWeight);
		this.DimOnOffRow(lines, dims, "치수선 1", Ov.SuppressDimLine1, inverted: true);
		this.DimOnOffRow(lines, dims, "치수선 2", Ov.SuppressDimLine2, inverted: true);
		this.DimColorRow(lines, dims, "치수선 색상", Ov.DimLineColor);
		this.DimLinetypeRow(lines, dims, "치수선 선종류", Ov.DimLineType);
		this.DimNumberRow(lines, dims, "치수선 연장", Ov.DimLineExtension, v => v >= 0);
		this.DimLinetypeRow(lines, dims, "연장선 1 선종류", Ov.ExtLineType1);
		this.DimLinetypeRow(lines, dims, "연장선 2 선종류", Ov.ExtLineType2);
		this.DimOnOffRow(lines, dims, "연장선 1", Ov.SuppressExtLine1, inverted: true);
		this.DimOnOffRow(lines, dims, "연장선 2", Ov.SuppressExtLine2, inverted: true);
		this.DimOnOffRow(lines, dims, "연장선 고정", Ov.FixedExtLine, inverted: false);
		this.DimNumberRow(lines, dims, "연장선 고정 길이", Ov.FixedExtLength, v => v >= 0);
		this.DimColorRow(lines, dims, "연장선 색상", Ov.ExtLineColor);
		this.DimNumberRow(lines, dims, "연장선 연장", Ov.ExtLineExtension, v => v >= 0);
		this.DimNumberRow(lines, dims, "연장선 간격띄우기", Ov.ExtLineOffset, v => v >= 0);
		this._categoriesPanel.Children.Add(this.CreateCategorySection("선 화살촉", lines));

		// 3) 텍스트
		var text = new StackPanel { Spacing = 0 };
		this.DimFillRow(text, dims);
		this.DimChoiceRow(text, dims, "분수 유형", 276, Enumerated(276, "수평", "대각선", "쌓지 않음"));
		this.DimColorRow(text, dims, "텍스트 색상", Ov.TextColor);
		this.DimNumberRow(text, dims, "텍스트 높이", Ov.TextHeight, v => v > 0);
		this.DimNumberRow(text, dims, "텍스트 간격띄우기", Ov.TextGap, _ => true);
		this.DimOnOffRow(text, dims, "외부 텍스트 정렬", Ov.TextOutside, inverted: false);
		this.DimChoiceRow(text, dims, "텍스트 위치 수평", Ov.TextHorizontal, Enumerated(Ov.TextHorizontal, "중앙에 배치됨", "첫 번째 확장선 근처", "두 번째 확장선 근처", "첫 번째 확장선 위", "두 번째 확장선 위"));
		this.DimChoiceRow(text, dims, "텍스트 위치 수직", Ov.TextVertical, Enumerated(Ov.TextVertical, "중앙에 배치됨", "위", "밖", "JIS", "아래"));
		this.DimTextStyleRow(text, dims);
		this.DimOnOffRow(text, dims, "텍스트 내부 정렬", Ov.TextInside, inverted: false);
		this.AddSharedNumericRow(text, "텍스트 위치 X", dims.Count == 1 ? d0.TextMiddlePoint.X : null, v =>
			doc.Execute(Ov.EditAction<(XYZ P, bool U)>(dims, d => (d.TextMiddlePoint, d.IsTextUserDefinedLocation), (d, s) => { d.TextMiddlePoint = s.P; d.IsTextUserDefinedLocation = s.U; },
				(new XYZ(v, d0.TextMiddlePoint.Y, d0.TextMiddlePoint.Z), true), "텍스트 위치 변경")));
		this.AddSharedNumericRow(text, "텍스트 위치 Y", dims.Count == 1 ? d0.TextMiddlePoint.Y : null, v =>
			doc.Execute(Ov.EditAction<(XYZ P, bool U)>(dims, d => (d.TextMiddlePoint, d.IsTextUserDefinedLocation), (d, s) => { d.TextMiddlePoint = s.P; d.IsTextUserDefinedLocation = s.U; },
				(new XYZ(d0.TextMiddlePoint.X, v, d0.TextMiddlePoint.Z), true), "텍스트 위치 변경")));
		this.AddSharedNumericRow(text, "텍스트 회전", DimCommonNum(dims, d => d.TextRotation * 180 / Math.PI), v =>
			doc.Execute(Ov.EditAction(dims, d => d.TextRotation, (d, r) => d.TextRotation = r, v * Math.PI / 180, "텍스트 회전 변경")));
		this.AddPropertyRow(text, "Text view direction", "Left-to-Right");
		this.AddPropertyRow(text, "측정 값", dims.Count == 1 ? MeasurementText(d0) : Varies);
		var texts = dims.Select(d => d.Text ?? "").Distinct().ToList();
		this.AddEditableTextRow(text, "텍스트 재정의", texts.Count == 1 ? texts[0] : Varies, nv =>
		{
			if (nv == Varies) return;
			doc.Execute(Ov.EditAction(dims, d => d.Text ?? "", (d, t) => d.Text = t, nv, "치수 문자 변경"));
		});
		this._categoriesPanel.Children.Add(this.CreateCategorySection("텍스트", text));

		// 4) 맞춤
		var fit = new StackPanel { Spacing = 0 };
		this.DimOnOffRow(fit, dims, "강제 치수선", Ov.TextOutsideExt, inverted: false);
		this.DimOnOffRow(fit, dims, "내부 치수선", Ov.SuppressOutsideDimLines, inverted: true);
		this.DimNumberRow(fit, dims, "전체 치수 축척", Ov.ScaleFactor, v => v >= 0);
		this.DimChoiceRow(fit, dims, "맞춤", Ov.Fit, Enumerated(Ov.Fit, "텍스트와 화살촉", "화살촉만", "텍스트만", "최적 맞춤"));
		this.DimOnOffRow(fit, dims, "내부 텍스트", Ov.TextInsideExt, inverted: false);
		this.DimChoiceRow(fit, dims, "텍스트 이동", Ov.TextMove, Enumerated(Ov.TextMove, "치수선과 텍스트 이동", "텍스트 이동 및 지시선 추가", "텍스트 이동 및 지시선 없음"));
		this._categoriesPanel.Children.Add(this.CreateCategorySection("맞춤", fit));

		// 5) 기본 단위
		bool angular = dims.All(d => d is DimensionAngular2Line or DimensionAngular3Pt);
		var units = new StackPanel { Spacing = 0 };
		this.DimChoiceRow(units, dims, "소수점 구분자", Ov.DecimalSeparator, new[]
		{
			new DimChoice(".", new Dictionary<int, object?> { [Ov.DecimalSeparator] = (short)'.' }),
			new DimChoice(",", new Dictionary<int, object?> { [Ov.DecimalSeparator] = (short)',' }),
			new DimChoice("공백", new Dictionary<int, object?> { [Ov.DecimalSeparator] = (short)' ' }),
		}, v => Convert.ToChar(Convert.ToInt32(v, CultureInfo.InvariantCulture)) switch { ',' => ",", ' ' => "공백", _ => "." });
		this.DimAffixRows(units, dims, Ov.Postfix, "<>", "치수 접두사", "치수 접미사");
		this.DimNumberRow(units, dims, "치수 반올림", Ov.Rounding, v => v >= 0);
		this.DimNumberRow(units, dims, "치수 축척 선형", Ov.LinearScale, v => Math.Abs(v) > 1e-12);
		if (angular)
		{
			this.DimChoiceRow(units, dims, "각도 치수 단위", Ov.AngularUnit, Enumerated(Ov.AngularUnit, "십진 도수", "도/분/초", "그라디안", "라디안"));
			this.DimChoiceRow(units, dims, "정밀도", Ov.AngularDecimals, Precision(Ov.AngularDecimals));
		}
		else
		{
			this.DimChoiceRow(units, dims, "치수 단위", Ov.LinearUnit, Enumerated(Ov.LinearUnit, 1, UnitNames));
			this.DimZeroRows(units, dims, Ov.ZeroHandling, "");
			this.DimChoiceRow(units, dims, "정밀도", Ov.DecimalPlaces, Precision(Ov.DecimalPlaces));
		}
		this._categoriesPanel.Children.Add(this.CreateCategorySection("기본 단위", units));

		// 6) 대체 단위
		var alt = new StackPanel { Spacing = 0 };
		this.DimOnOffRow(alt, dims, "대체 사용", Ov.AltEnabled, inverted: false);
		this.DimChoiceRow(alt, dims, "대체 형식", Ov.AltUnit, Enumerated(Ov.AltUnit, 1, UnitNames));
		this.DimChoiceRow(alt, dims, "대체 정밀도", Ov.AltDecimals, Precision(Ov.AltDecimals));
		this.DimNumberRow(alt, dims, "대체 반올림", Ov.AltRounding, v => v >= 0);
		this.DimNumberRow(alt, dims, "대체 축척 인자", Ov.AltScale, v => Math.Abs(v) > 1e-12);
		this.DimZeroRows(alt, dims, Ov.AltZeroHandling, "대체 ");
		this.DimAffixRows(alt, dims, Ov.AltPostfix, "[]", "대체 접두사", "대체 접미사");
		this._categoriesPanel.Children.Add(this.CreateCategorySection("대체 단위", alt));

		// 7) 허용 오차
		var tol = new StackPanel { Spacing = 0 };
		this.DimToleranceDisplayRow(tol, dims);
		this.DimNumberRow(tol, dims, "허용 오차 하한", Ov.TolMinus, _ => true);
		this.DimNumberRow(tol, dims, "허용 오차 상한", Ov.TolPlus, _ => true);
		this.DimChoiceRow(tol, dims, "허용 오차 위치 수직", Ov.TolAlign, Enumerated(Ov.TolAlign, "아래", "중간", "위"));
		this.DimChoiceRow(tol, dims, "허용 오차 정밀도", Ov.TolDecimals, Precision(Ov.TolDecimals));
		this.DimZeroRows(tol, dims, Ov.TolZeroHandling, "허용 오차 ");
		this.DimNumberRow(tol, dims, "허용 오차 텍스트 높이", Ov.TolScale, v => v > 0);
		this.DimChoiceRow(tol, dims, "대체 허용 오차 정밀도", Ov.AltTolDecimals, Precision(Ov.AltTolDecimals));
		this.DimZeroRows(tol, dims, Ov.AltTolZeroHandling, "대체 허용 오차 ");
		this._categoriesPanel.Children.Add(this.CreateCategorySection("허용 오차", tol));
	}

	private static readonly string[] UnitNames = { "과학적", "소수점", "공학적", "건축", "분수", "윈도우 데스크탑" };

	/// <summary>측정 값 표시 (각도는 도, 그 밖은 길이).</summary>
	private static string MeasurementText(Dimension d) =>
		d is DimensionAngular2Line or DimensionAngular3Pt
			? (d.Measurement * 180 / Math.PI).ToString("0.####", CultureInfo.InvariantCulture) + "°"
			: d.Measurement.ToString("0.0000", CultureInfo.InvariantCulture);

	// ── 값 읽기 ──

	/// <summary>치수들의 공통 재정의 값 (서로 다르면 null, varies = true).</summary>
	private static object? DimCommon(IReadOnlyList<Entity> dims, int code, out bool varies)
	{
		var vals = dims.Select(d => Ov.Effective(d, code)).ToList();
		var keys = vals.Select(RawKey).Distinct().ToList();
		varies = keys.Count > 1;
		return varies ? null : vals[0];
	}

	private static double? DimCommonNum(IReadOnlyList<Dimension> dims, Func<Dimension, double> get)
	{
		var vals = dims.Select(d => Math.Round(get(d), 9)).Distinct().ToList();
		return vals.Count == 1 ? vals[0] : null;
	}

	/// <summary>원시 값 비교용 문자열 (bool·열거형·정수는 숫자로).</summary>
	private static string RawKey(object? v) => v switch
	{
		null => "",
		bool b => b ? "1" : "0",
		Enum e => Convert.ToInt64(e).ToString(CultureInfo.InvariantCulture),
		double d => Math.Round(d, 9).ToString(CultureInfo.InvariantCulture),
		IConvertible c when v is not string => Convert.ToInt64(c, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
		_ => v.ToString() ?? "",
	};

	private static DimChoice[] Enumerated(int code, params string[] names) => Enumerated(code, 0, names);

	private static DimChoice[] Enumerated(int code, int first, params string[] names) =>
		names.Select((n, i) => new DimChoice(n, new Dictionary<int, object?> { [code] = (short)(first + i) })).ToArray();

	private static DimChoice[] Precision(int code) =>
		Enumerable.Range(0, 9).Select(i => new DimChoice(i == 0 ? "0" : "0." + new string('0', i), new Dictionary<int, object?> { [code] = (short)i })).ToArray();

	// ── 행 종류 ──

	private void DimNumberRow(StackPanel panel, IReadOnlyList<Entity> dims, string label, int code, Func<double, bool> valid)
	{
		var v = DimCommon(dims, code, out bool varies);
		double? cur = varies || v == null ? null : Convert.ToDouble(v, CultureInfo.InvariantCulture);
		void Apply(double nv)
		{
			if (!valid(nv) || this._doc == null) return;
			this._doc.Execute(Ov.SetAction(dims, code, nv, label + " 변경"));
		}
		// 기존 CAD처럼 소수 넷째 자리까지 (0.2000)
		if (cur.HasValue) this.AddEditableNumericRow(panel, label, cur.Value, Apply);
		else this.AddSharedNumericRow(panel, label, null, Apply);
	}

	/// <summary>고르기 행. 현재 값과 같은 선택지를 보여 준다 (nameOf가 있으면 그것으로 이름을 정한다).</summary>
	private void DimChoiceRow(StackPanel panel, IReadOnlyList<Entity> dims, string label, int code, DimChoice[] choices, Func<object, string>? nameOf = null)
	{
		var v = DimCommon(dims, code, out bool varies);
		string cur;
		if (varies) cur = Varies;
		else if (v == null) cur = choices[0].Name;
		else if (nameOf != null) cur = nameOf(v);
		else cur = choices.FirstOrDefault(c => c.Values.TryGetValue(code, out var cv) && RawKey(cv) == RawKey(v))?.Name ?? RawKey(v);
		var names = choices.Select(c => c.Name).ToList();
		if (!names.Contains(cur)) names.Insert(0, cur);
		this.AddPropertyRow(panel, label, this.CreateSimpleDropdown(names, cur, name =>
		{
			var choice = choices.FirstOrDefault(c => c.Name == name);
			if (choice != null && this._doc != null)
			{
				this._doc.Execute(Ov.SetAction(dims, choice.Values, label + " 변경"));
			}
		}));
	}

	private void DimOnOffRow(StackPanel panel, IReadOnlyList<Entity> dims, string label, int code, bool inverted)
	{
		// inverted: 값이 "억제" (DIMSD1 등) → 켜짐 = false
		this.DimChoiceRow(panel, dims, label, code, new[]
		{
			new DimChoice("켜짐", new Dictionary<int, object?> { [code] = !inverted }),
			new DimChoice("꺼짐", new Dictionary<int, object?> { [code] = inverted }),
		}, v => (RawKey(v) == "1") != inverted ? "켜짐" : "꺼짐");
	}

	private void DimColorRow(StackPanel panel, IReadOnlyList<Entity> dims, string label, int code)
	{
		var v = DimCommon(dims, code, out bool varies);
		short idx = varies || v == null ? (short)0 : Convert.ToInt16(v, CultureInfo.InvariantCulture);
		var combo = new ComboBox
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			FontSize = 13,
			Height = 20,
			MinHeight = 20,
			Padding = new Thickness(2, 0),
			Background = Brushes.Transparent,
			Foreground = CadTheme.PropertyValueFg,
			BorderThickness = new Thickness(0),
		};
		combo.ItemTemplate = new FuncDataTemplate<ColorOption>((item, _) =>
		{
			var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			sp.Children.Add(new Border { Width = 10, Height = 10, Background = item.Brush, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center });
			sp.Children.Add(new TextBlock { Text = item.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
			return sp;
		});
		var items = ColorOptions.ToList();
		ColorOption? selected;
		if (varies)
		{
			selected = new ColorOption(Varies, Color.ByBlock, Brushes.Transparent);
			items.Insert(0, selected);
		}
		else
		{
			selected = items.FirstOrDefault(o => o.Color.Index == idx);
			if (selected == null)
			{
				var c = new Color(idx);
				selected = new ColorOption($"색상 {idx}", c, new SolidColorBrush(Avalonia.Media.Color.FromRgb(c.R, c.G, c.B)));
				items.Insert(0, selected);
			}
		}
		combo.ItemsSource = items;
		combo.SelectedItem = selected;
		combo.SelectionChanged += (_, _) =>
		{
			if (this._updatingUi || this._doc == null) return;
			if (combo.SelectedItem is ColorOption opt && opt.Name != Varies && !ReferenceEquals(opt, selected))
			{
				this._doc.Execute(Ov.SetAction(dims, code, opt.Color.Index, label + " 변경"));
			}
		};
		this.AddPropertyRow(panel, label, combo);
	}

	private void DimWeightRow(StackPanel panel, IReadOnlyList<Entity> dims, string label, int code)
	{
		var choices = Weights.Select(w => new DimChoice(WeightName(w), new Dictionary<int, object?> { [code] = (short)w })).ToArray();
		this.DimChoiceRow(panel, dims, label, code, choices, v => WeightName((LineWeightType)Convert.ToInt16(v, CultureInfo.InvariantCulture)));
	}

	private void DimLinetypeRow(StackPanel panel, IReadOnlyList<Dimension> dims, string label, int code)
	{
		var cad = this._doc!.Document;
		var lts = cad.LineTypes.OrderBy(l => l.Name.Equals("ByBlock", StringComparison.OrdinalIgnoreCase) ? 0 : l.Name.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
			.ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
		var choices = lts.Select(l => new DimChoice(l.Name, new Dictionary<int, object?> { [code] = l.Handle })).ToArray();
		this.DimChoiceRow(panel, dims, label, code, choices, v =>
		{
			ulong h = Convert.ToUInt64(v, CultureInfo.InvariantCulture);
			return h == 0 ? "ByBlock" : lts.FirstOrDefault(l => l.Handle == h)?.Name ?? "ByBlock";
		});
	}

	private void DimTextStyleRow(StackPanel panel, IReadOnlyList<Dimension> dims)
	{
		var cad = this._doc!.Document;
		var styles = cad.TextStyles.Where(s => !s.IsShapeFile && !string.IsNullOrEmpty(s.Name)).ToList();
		var choices = styles.Select(s => new DimChoice(s.Name, new Dictionary<int, object?> { [Ov.TextStyleHandle] = s.Handle })).ToArray();
		this.DimChoiceRow(panel, dims, "텍스트 스타일", Ov.TextStyleHandle, choices, v =>
		{
			ulong h = Convert.ToUInt64(v, CultureInfo.InvariantCulture);
			return styles.FirstOrDefault(s => s.Handle == h)?.Name ?? "Standard";
		});
	}

	/// <summary>
	/// 화살촉 1/2: 고르면 DIMSAH를 켜고 DIMBLK1·DIMBLK2를 정한다 (다른 쪽은 지금 모양 그대로).
	/// </summary>
	private void DimArrowRow(StackPanel panel, IReadOnlyList<Dimension> dims, string label, bool first)
	{
		var cad = this._doc!.Document;
		var names = dims.Select(d =>
		{
			var st = d.GetActiveDimensionStyle();
			return Ov.ArrowName(first ? Dimension.FirstArrowBlock(st) : Dimension.SecondArrowBlock(st));
		}).Distinct().ToList();
		string cur = names.Count == 1 ? names[0] : Varies;
		var items = Ov.Arrows.Select(a => a.Name).ToList();
		if (!items.Contains(cur)) items.Insert(0, cur);
		this.AddPropertyRow(panel, label, this.CreateSimpleDropdown(items, cur, name =>
		{
			var arrow = Ov.Arrows.FirstOrDefault(a => a.Name == name);
			if (arrow.Name == null || this._doc == null) return;
			var block = Ov.GetOrCreateArrow(cad, arrow.Block);
			var actions = dims.Select(d =>
			{
				var st = d.GetActiveDimensionStyle();
				var other = first ? Dimension.SecondArrowBlock(st) : Dimension.FirstArrowBlock(st);
				return Ov.SetAction(new[] { d }, new Dictionary<int, object?>
				{
					[Ov.SeparateArrows] = true,
					[first ? Ov.Arrow1 : Ov.Arrow2] = block?.Handle ?? 0UL,
					[first ? Ov.Arrow2 : Ov.Arrow1] = other?.Handle ?? 0UL,
				}, label + " 변경");
			}).ToList();
			this._doc.Execute(actions.Count == 1 ? actions[0] : new Free2DCad.Core.Actions.CompositeAction(label + " 변경", actions));
		}));
	}

	/// <summary>채우기 색상: 없음 / 배경 / 색상 (DIMTFILL + DIMTFILLCLR).</summary>
	private void DimFillRow(StackPanel panel, IReadOnlyList<Dimension> dims)
	{
		var choices = new List<DimChoice>
		{
			new("없음", new Dictionary<int, object?> { [Ov.TextFill] = (short)0, [Ov.TextFillColor] = null }),
			new("배경", new Dictionary<int, object?> { [Ov.TextFill] = (short)1, [Ov.TextFillColor] = null }),
		};
		choices.AddRange(ColorOptions.Skip(2).Select(o => new DimChoice(o.Name, new Dictionary<int, object?> { [Ov.TextFill] = (short)2, [Ov.TextFillColor] = o.Color.Index })));
		this.DimChoiceRow(panel, dims, "채우기 색상", Ov.TextFill, choices.ToArray(), v =>
		{
			int mode = Convert.ToInt32(v, CultureInfo.InvariantCulture);
			if (mode == 0) return "없음";
			if (mode == 1) return "배경";
			short idx = Convert.ToInt16(Ov.Effective(dims[0], Ov.TextFillColor) ?? (short)0, CultureInfo.InvariantCulture);
			return ColorOptions.FirstOrDefault(o => o.Color.Index == idx)?.Name ?? $"색상 {idx}";
		});
	}

	/// <summary>접두사·접미사 (DIMPOST "앞<>뒤", DIMAPOST "앞[]뒤").</summary>
	private void DimAffixRows(StackPanel panel, IReadOnlyList<Dimension> dims, int code, string marker, string prefixLabel, string suffixLabel)
	{
		var v = DimCommon(dims, code, out bool varies);
		string post = varies ? "" : v as string ?? "";
		int at = post.IndexOf(marker, StringComparison.Ordinal);
		string prefix = at >= 0 ? post[..at] : "";
		string suffix = at >= 0 ? post[(at + marker.Length)..] : post;
		void Apply(string p, string s)
		{
			string value = p.Length == 0 && s.Length == 0 ? "" : p.Length == 0 ? s : p + marker + s;
			this._doc?.Execute(Ov.SetAction(dims, code, value, prefixLabel + " 변경"));
		}
		this.AddEditableTextRow(panel, prefixLabel, varies ? Varies : prefix, nv => { if (nv != Varies) Apply(nv, suffix); });
		this.AddEditableTextRow(panel, suffixLabel, varies ? Varies : suffix, nv => { if (nv != Varies) Apply(prefix, nv); });
	}

	/// <summary>
	/// 0 생략 (DIMZIN 계열): 앞자리(4)·뒷자리(8) 비트와 피트·인치(하위 2비트: 0 = 둘 다 생략, 1 = 둘 다 표시,
	/// 2 = 인치만 생략, 3 = 피트만 생략). 기존 CAD처럼 "예"/"No".
	/// </summary>
	private void DimZeroRows(StackPanel panel, IReadOnlyList<Dimension> dims, int code, string prefix)
	{
		var v = DimCommon(dims, code, out bool varies);
		int bits = varies || v == null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
		int low = bits & 3;
		bool feet = low == 0 || low == 3, inch = low == 0 || low == 2;
		void Row(string label, bool on, Func<bool, int> make)
		{
			string cur = varies ? Varies : on ? "예" : "No";
			var items = new List<string> { "예", "No" };
			if (varies) items.Insert(0, Varies);
			this.AddPropertyRow(panel, label, this.CreateSimpleDropdown(items, cur, name =>
			{
				if (name == Varies || this._doc == null) return;
				this._doc.Execute(Ov.SetAction(dims, code, (short)make(name == "예"), label + " 변경"));
			}));
		}
		static int Low(bool f, bool i) => f && i ? 0 : !f && !i ? 1 : i ? 2 : 3;
		Row(prefix + "앞자리 0 생략", (bits & 4) != 0, on => on ? bits | 4 : bits & ~4);
		Row(prefix + "뒷자리 0 생략", (bits & 8) != 0, on => on ? bits | 8 : bits & ~8);
		Row(prefix + "0 피트 생략", feet, on => (bits & ~3) | Low(on, inch));
		Row(prefix + "0 인치 생략", inch, on => (bits & ~3) | Low(feet, on));
	}

	/// <summary>허용 오차 표시: 없음 / 대칭 / 편차 / 한계 / 기본 (DIMTOL·DIMLIM, 대칭은 하한 = 상한, 기본은 DIMGAP 음수).</summary>
	private void DimToleranceDisplayRow(StackPanel panel, IReadOnlyList<Dimension> dims)
	{
		var d0 = dims[0];
		object tp = Ov.Effective(d0, Ov.TolPlus) ?? 0.0;
		double gap = Math.Abs(Convert.ToDouble(Ov.Effective(d0, Ov.TextGap) ?? 0.625, CultureInfo.InvariantCulture));
		var choices = new[]
		{
			new DimChoice("없음", new Dictionary<int, object?> { [Ov.TolEnabled] = false, [Ov.TolLimits] = false, [Ov.TextGap] = gap }),
			new DimChoice("대칭", new Dictionary<int, object?> { [Ov.TolEnabled] = true, [Ov.TolLimits] = false, [Ov.TolMinus] = tp, [Ov.TextGap] = gap }),
			new DimChoice("편차", new Dictionary<int, object?> { [Ov.TolEnabled] = true, [Ov.TolLimits] = false, [Ov.TextGap] = gap }),
			new DimChoice("한계", new Dictionary<int, object?> { [Ov.TolEnabled] = false, [Ov.TolLimits] = true, [Ov.TextGap] = gap }),
			new DimChoice("기본", new Dictionary<int, object?> { [Ov.TolEnabled] = false, [Ov.TolLimits] = false, [Ov.TextGap] = -gap }),
		};
		this.DimChoiceRow(panel, dims, "허용 오차 표시", Ov.TolEnabled, choices, v =>
		{
			bool tol = RawKey(v) == "1";
			if (RawKey(Ov.Effective(d0, Ov.TolLimits)) == "1") return "한계";
			if (tol) return RawKey(Ov.Effective(d0, Ov.TolMinus)) == RawKey(Ov.Effective(d0, Ov.TolPlus)) ? "대칭" : "편차";
			return Convert.ToDouble(Ov.Effective(d0, Ov.TextGap) ?? 0.0, CultureInfo.InvariantCulture) < 0 ? "기본" : "없음";
		});
	}
}

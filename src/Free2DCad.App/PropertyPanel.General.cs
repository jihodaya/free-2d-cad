using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ACadSharp;
using ACadSharp.Entities;
using Free2DCad.Core.Actions;
using CSMath;

namespace Free2DCad.App;

/// <summary>
/// 속성 팔레트 일반 항목 (선 종류 비율, 선가중치, 두께, 투명도)과 형상 편집 행 보강.
/// 선택이 없으면 새 객체의 기본값(CELTSCALE, CELWEIGHT)을 바꾼다. 값이 서로 다르면 *다양함*.
/// </summary>
public sealed partial class PropertyPanel
{
	private const string Varies = "*다양함*";

	private static readonly LineWeightType[] Weights =
	{
		LineWeightType.ByLayer, LineWeightType.ByBlock, LineWeightType.Default, LineWeightType.W0, LineWeightType.W5, LineWeightType.W9,
		LineWeightType.W13, LineWeightType.W15, LineWeightType.W18, LineWeightType.W20, LineWeightType.W25, LineWeightType.W30,
		LineWeightType.W35, LineWeightType.W40, LineWeightType.W50, LineWeightType.W53, LineWeightType.W60, LineWeightType.W70,
		LineWeightType.W80, LineWeightType.W90, LineWeightType.W100, LineWeightType.W106, LineWeightType.W120, LineWeightType.W140,
		LineWeightType.W158, LineWeightType.W200, LineWeightType.W211,
	};

	private static string WeightName(LineWeightType w) => w switch
	{
		LineWeightType.ByLayer => "도면층별",
		LineWeightType.ByBlock => "블록별",
		LineWeightType.Default => "기본값",
		_ => $"{(int)w / 100.0:0.00} mm",
	};

	/// <summary>선가중치: 선택 객체(없으면 새 객체 기본값)에 적용.</summary>
	private Control CreateLineweightDropdown(IReadOnlyCollection<Entity>? selected)
	{
		var doc = this._doc;
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
			MaxDropDownHeight = 320,
		};
		var items = Weights.Select(w => (object)w).ToList();
		LineWeightType? current = selected == null || selected.Count == 0
			? doc?.Document.Header.CurrentEntityLineWeight ?? LineWeightType.ByLayer
			: selected.Select(e => e.LineWeight).Distinct().Count() == 1 ? selected.First().LineWeight : null;
		if (current == null) items.Insert(0, Varies);
		else if (!Weights.Contains(current.Value)) items.Insert(0, current.Value); // 표준이 아닌 값(예: 29)도 그대로 보이게
		combo.ItemTemplate = new FuncDataTemplate<object>((it, _) =>
		{
			var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			if (it is LineWeightType w)
			{
				double px = w > 0 ? Math.Clamp((int)w / 25.0, 0.6, 4) : 1;
				sp.Children.Add(new Avalonia.Controls.Shapes.Line { StartPoint = new Avalonia.Point(0, 5), EndPoint = new Avalonia.Point(18, 5), Stroke = Brushes.Black, StrokeThickness = px, VerticalAlignment = VerticalAlignment.Center });
				sp.Children.Add(new TextBlock { Text = WeightName(w), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
			}
			else
			{
				sp.Children.Add(new TextBlock { Text = it?.ToString() ?? "", FontSize = 13 });
			}
			return sp;
		});
		combo.ItemsSource = items;
		combo.SelectedItem = current.HasValue ? current.Value : Varies;
		combo.SelectionChanged += (_, _) =>
		{
			if (this._updatingUi || doc == null || combo.SelectedItem is not LineWeightType w) return;
			if (selected == null || selected.Count == 0)
			{
				var old = doc.Document.Header.CurrentEntityLineWeight;
				doc.Execute(new ModifyEntityPropertyAction("현재 선가중치", () => doc.Document.Header.CurrentEntityLineWeight = w, () => doc.Document.Header.CurrentEntityLineWeight = old));
				return;
			}
			var olds = selected.ToDictionary(e => e, e => e.LineWeight);
			doc.Execute(new ModifyEntityPropertyAction("선가중치 변경",
				() => { foreach (var e in selected) e.LineWeight = w; },
				() => { foreach (var kv in olds) kv.Key.LineWeight = kv.Value; }));
		};
		return combo;
	}

	/// <summary>선택 객체 공통 값 (서로 다르면 null).</summary>
	private static double? Common(IReadOnlyCollection<Entity> sel, Func<Entity, double?> get)
	{
		var vals = sel.Select(get).Where(v => v.HasValue).Select(v => Math.Round(v!.Value, 9)).Distinct().ToList();
		return vals.Count == 1 ? vals[0] : null;
	}

	/// <summary>숫자 행: 값이 다르면 *다양함*으로 보이고, 입력하면 모두 그 값으로.</summary>
	private void AddSharedNumericRow(StackPanel panel, string label, double? value, Action<double> onApply)
	{
		if (this._recording != null)
		{
			string t0 = value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : Varies;
			this.AddPropertyRow(panel, label, this.EditBox(t0, t =>
			{
				if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || double.TryParse(t, out v)) onApply(v);
			}));
			return;
		}
		var box = new TextBox
		{
			Text = value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : Varies,
			FontSize = 13,
			Height = 20,
			MinHeight = 20,
			Padding = new Thickness(4, 0),
			Background = Brushes.Transparent,
			Foreground = CadTheme.PropertyValueFg,
			BorderThickness = new Thickness(0),
			VerticalAlignment = VerticalAlignment.Center,
		};
		box.KeyDown += (_, ke) =>
		{
			if (ke.Key == Avalonia.Input.Key.Enter)
			{
				if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || double.TryParse(box.Text, out v))
				{
					onApply(v);
				}
				ke.Handled = true;
			}
		};
		this.AddPropertyRow(panel, label, box);
	}

	private static PropertyInfo? ThicknessProp(Entity e) => e.GetType().GetProperty("Thickness", BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true, PropertyType: var t } p && t == typeof(double) ? p : null;

	/// <summary>일반 항목 중 선 종류 비율·선가중치·두께·투명도 (선택 있음/없음 공통).</summary>
	private void AddGeneralExtraRows(StackPanel panel, IReadOnlyCollection<Entity>? selected)
	{
		var doc = this._doc;
		bool none = selected == null || selected.Count == 0;
		// 선 종류 비율
		if (none)
		{
			this.AddSharedNumericRow(panel, "선 종류 비율", doc?.Document.Header.CurrentEntityLinetypeScale ?? 1, v =>
			{
				if (doc == null || v <= 0) return;
				var old = doc.Document.Header.CurrentEntityLinetypeScale;
				doc.Execute(new ModifyEntityPropertyAction("현재 선 종류 비율", () => doc.Document.Header.CurrentEntityLinetypeScale = v, () => doc.Document.Header.CurrentEntityLinetypeScale = old));
			});
		}
		else
		{
			var sel = selected!;
			this.AddSharedNumericRow(panel, "선 종류 비율", Common(sel, e => e.LineTypeScale), v =>
			{
				if (doc == null || v <= 0) return;
				var olds = sel.ToDictionary(e => e, e => e.LineTypeScale);
				doc.Execute(new ModifyEntityPropertyAction("선 종류 비율 변경", () => { foreach (var e in sel) e.LineTypeScale = v; }, () => { foreach (var kv in olds) kv.Key.LineTypeScale = kv.Value; }));
			});
		}
		// 기존 CAD 순서 (선택 있음): 선 종류 비율, 플롯 스타일, 선 두께, 하이퍼링크, 투명도, 두께(치수는 연관성), 가시성
		if (!none) this.AddPropertyRow(panel, "플롯 스타일", "ByLayer");
		this.AddPropertyRow(panel, "선 두께", this.CreateLineweightDropdown(selected));
		if (none)
		{
			// 선택 없음: 선 두께, 두께, 투명도
			this.AddThicknessRow(panel, selected);
			this.AddTransparencyRow(panel, selected);
			return;
		}
		{
			// 하이퍼링크 (기존 CAD와 같은 XData "PE_URL"), 값이 다르면 *다양함*
			var sel = selected!;
			var urls = sel.Select(Free2DCad.Core.Hyperlinks.Get).Distinct().ToList();
			string cur = urls.Count == 1 ? urls[0] : Varies;
			this.AddEditableTextRow(panel, "하이퍼링크", cur, nv =>
			{
				if (doc == null || nv == Varies) return;
				doc.Execute(Free2DCad.Core.Hyperlinks.SetAction(sel, nv));
			});
		}
		this.AddTransparencyRow(panel, selected);
		if (selected!.All(e => e is Dimension or Leader))
		{
			this.AddPropertyRow(panel, "연관성", "No");
		}
		else if (selected!.All(e => ThicknessProp(e) != null && e is not AttributeDefinition)) // 기존 CAD: 속성 정의는 두께 행 없음
		{
			this.AddThicknessRow(panel, selected);
		}
		if (!none)
		{
			var sel = selected!;
			var vis = sel.Select(e => !e.IsInvisible).Distinct().ToList();
			string cur = vis.Count == 1 ? (vis[0] ? "예" : "아니오") : Varies;
			var items = new List<string> { "예", "아니오" };
			if (cur == Varies) items.Insert(0, Varies);
			this.AddPropertyRow(panel, "가시성", this.CreateSimpleDropdown(items, cur, name =>
			{
				if (doc == null || name == Varies) return;
				bool invisible = name == "아니오";
				var olds = sel.ToDictionary(e => e, e => e.IsInvisible);
				doc.Execute(new ModifyEntityPropertyAction("가시성 변경", () => { foreach (var e in sel) e.IsInvisible = invisible; }, () => { foreach (var kv in olds) kv.Key.IsInvisible = kv.Value; }));
			}));
		}
	}

	/// <summary>두께 (선·원·호·폴리선·문자 등 Thickness가 있는 객체).</summary>
	private void AddThicknessRow(StackPanel panel, IReadOnlyCollection<Entity>? selected)
	{
		var doc = this._doc;
		bool none = selected == null || selected.Count == 0;
		if (none)
		{
			this.AddPropertyRow(panel, "두께", "0");
		}
		else
		{
			var sel = selected!.Where(e => ThicknessProp(e) != null).ToList();
			if (sel.Count == 0)
			{
				this.AddPropertyRow(panel, "두께", "0");
			}
			else
			{
				this.AddSharedNumericRow(panel, "두께", Common(sel, e => (double?)ThicknessProp(e)!.GetValue(e)), v =>
				{
					if (doc == null) return;
					var olds = sel.ToDictionary(e => e, e => (double)ThicknessProp(e)!.GetValue(e)!);
					doc.Execute(new ModifyEntityPropertyAction("두께 변경", () => { foreach (var e in sel) ThicknessProp(e)!.SetValue(e, v); }, () => { foreach (var kv in olds) ThicknessProp(kv.Key)!.SetValue(kv.Key, kv.Value); }));
				});
			}
		}

	}

	/// <summary>투명도: 도면층별 / 블록별 / 0~90.</summary>
	private void AddTransparencyRow(StackPanel panel, IReadOnlyCollection<Entity>? selected)
	{
		var doc = this._doc;
		bool none = selected == null || selected.Count == 0;
		var transNames = new List<string> { "도면층별", "블록별" };
		transNames.AddRange(Enumerable.Range(0, 10).Select(i => (i * 10).ToString(CultureInfo.InvariantCulture)));
		static string TName(Transparency t) => t.IsByLayer ? "도면층별" : t.IsByBlock ? "블록별" : t.Value.ToString(CultureInfo.InvariantCulture);
		if (none)
		{
			this.AddPropertyRow(panel, "투명도", "도면층별");
		}
		else
		{
			var sel = selected!;
			var names = sel.Select(e => TName(e.Transparency)).Distinct().ToList();
			string cur = names.Count == 1 ? names[0] : Varies;
			if (!transNames.Contains(cur)) transNames.Insert(0, cur);
			this.AddPropertyRow(panel, "투명도", this.CreateSimpleDropdown(transNames, cur, name =>
			{
				if (doc == null) return;
				Transparency t = name switch
				{
					"도면층별" => Transparency.ByLayer,
					"블록별" => Transparency.ByBlock,
					_ => short.TryParse(name, out short v) ? new Transparency(Math.Clamp(v, (short)0, (short)90)) : Transparency.ByLayer,
				};
				var olds = sel.ToDictionary(e => e, e => e.Transparency);
				doc.Execute(new ModifyEntityPropertyAction("투명도 변경", () => { foreach (var e in sel) e.Transparency = t; }, () => { foreach (var kv in olds) kv.Key.Transparency = kv.Value; }));
			}));
		}
	}

	private void Exec(string name, Action apply, Action revert) => this._doc?.Execute(new ModifyEntityPropertyAction(name, apply, revert));

	/// <summary>각도(도) 편집 행.</summary>
	private void AddAngleRow(StackPanel panel, string label, double radians, Action<double> onApplyRadians) =>
		this.AddSharedNumericRow(panel, label, Math.Round(radians * 180 / Math.PI, 6), deg => onApplyRadians(deg * Math.PI / 180));

	/// <summary>형상 항목 중 기본 표시만 하던 객체들의 편집 행 (호 각도, 폴리선 닫힘·폭, 블록 축척·회전, 해치, 타원, 스플라인, 점, 구성선, 다중선).</summary>
	private bool BuildExtraGeometry(StackPanel panel, Entity entity)
	{
		switch (entity)
		{
			case Arc arc:
				this.AddEditableNumericRow(panel, "중심 X", arc.Center.X, v => { var o = arc.Center; this.Exec("중심 X 변경", () => arc.Center = new XYZ(v, arc.Center.Y, arc.Center.Z), () => arc.Center = o); });
				this.AddEditableNumericRow(panel, "중심 Y", arc.Center.Y, v => { var o = arc.Center; this.Exec("중심 Y 변경", () => arc.Center = new XYZ(arc.Center.X, v, arc.Center.Z), () => arc.Center = o); });
				this.AddPropertyRow(panel, "중심 Z", $"{arc.Center.Z:F4}");
				this.AddEditableNumericRow(panel, "반지름", arc.Radius, v => { if (v <= 1e-9) return; var o = arc.Radius; this.Exec("반지름 변경", () => arc.Radius = v, () => arc.Radius = o); });
				this.AddAngleRow(panel, "시작 각도", arc.StartAngle, r => { var o = arc.StartAngle; this.Exec("시작 각도 변경", () => arc.StartAngle = r, () => arc.StartAngle = o); });
				this.AddAngleRow(panel, "끝 각도", arc.EndAngle, r => { var o = arc.EndAngle; this.Exec("끝 각도 변경", () => arc.EndAngle = r, () => arc.EndAngle = o); });
				{
					double span = arc.EndAngle - arc.StartAngle;
					if (span <= 0) span += 2 * Math.PI;
					this.AddPropertyRow(panel, "전체 각도", $"{span * 180 / Math.PI:F2}°");
					this.AddPropertyRow(panel, "호 길이", $"{arc.Radius * span:F4}");
					this.AddPropertyRow(panel, "면적", $"{0.5 * arc.Radius * arc.Radius * (span - Math.Sin(span)):F4}");
				}
				return true;
			case LwPolyline poly:
				{
					this.AddPropertyRow(panel, "정점 수", $"{poly.Vertices.Count}");
					this.AddPropertyRow(panel, "닫힘", this.CreateSimpleDropdown(new[] { "예", "아니오" }, poly.IsClosed ? "예" : "아니오", name =>
					{
						bool c = name == "예", o = poly.IsClosed;
						this.Exec("닫힘 변경", () => poly.IsClosed = c, () => poly.IsClosed = o);
					}));
					this.AddEditableNumericRow(panel, "전체 폭", poly.ConstantWidth, v =>
					{
						if (v < 0) return;
						var o = poly.ConstantWidth;
						this.Exec("전체 폭 변경", () => poly.ConstantWidth = v, () => poly.ConstantWidth = o);
					});
					this.AddEditableNumericRow(panel, "고도", poly.Elevation, v => { var o = poly.Elevation; this.Exec("고도 변경", () => poly.Elevation = v, () => poly.Elevation = o); });
					var segs = Free2DCad.Core.Geometry.Geom.SegmentsOf(poly);
					this.AddPropertyRow(panel, "길이", $"{Free2DCad.Core.Geometry.Geom.Length(segs):F4}");
					if (poly.IsClosed)
					{
						double area = 0;
						var pts = poly.Vertices.Select(v => v.Location).ToList();
						for (int i = 0; i < pts.Count; i++)
						{
							var a = pts[i];
							var b = pts[(i + 1) % pts.Count];
							area += a.X * b.Y - b.X * a.Y;
						}
						this.AddPropertyRow(panel, "면적", $"{Math.Abs(area) / 2:F4}");
					}
					return true;
				}
			case Insert insert when insert is not TableEntity:
				this.AddPropertyRow(panel, "이름", insert.Block?.Name ?? "(없음)");
				this.AddEditableNumericRow(panel, "위치 X", insert.InsertPoint.X, v => { var o = insert.InsertPoint; this.Exec("위치 X 변경", () => insert.InsertPoint = new XYZ(v, insert.InsertPoint.Y, insert.InsertPoint.Z), () => insert.InsertPoint = o); });
				this.AddEditableNumericRow(panel, "위치 Y", insert.InsertPoint.Y, v => { var o = insert.InsertPoint; this.Exec("위치 Y 변경", () => insert.InsertPoint = new XYZ(insert.InsertPoint.X, v, insert.InsertPoint.Z), () => insert.InsertPoint = o); });
				this.AddEditableNumericRow(panel, "축척 X", insert.XScale, v => { if (Math.Abs(v) < 1e-12) return; var o = insert.XScale; this.Exec("축척 X 변경", () => insert.XScale = v, () => insert.XScale = o); });
				this.AddEditableNumericRow(panel, "축척 Y", insert.YScale, v => { if (Math.Abs(v) < 1e-12) return; var o = insert.YScale; this.Exec("축척 Y 변경", () => insert.YScale = v, () => insert.YScale = o); });
				this.AddEditableNumericRow(panel, "축척 Z", insert.ZScale, v => { if (Math.Abs(v) < 1e-12) return; var o = insert.ZScale; this.Exec("축척 Z 변경", () => insert.ZScale = v, () => insert.ZScale = o); });
				this.AddAngleRow(panel, "회전", insert.Rotation, r => { var o = insert.Rotation; this.Exec("회전 변경", () => insert.Rotation = r, () => insert.Rotation = o); });
				foreach (var att in insert.Attributes)
				{
					var a = att;
					this.AddEditableTextRow(panel, a.Tag, a.Value ?? "", nv => { string o = a.Value ?? ""; this.Exec($"속성 {a.Tag} 변경", () => a.Value = nv, () => a.Value = o); });
				}
				return true;
			case Hatch hatch:
				{
					var names = Free2DCad.Core.Hatching.HatchPatternLibrary.All.Select(p => p.Name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
					string cur = hatch.Pattern?.Name ?? "SOLID";
					if (!names.Contains(cur)) names.Insert(0, cur);
					if (!names.Contains("SOLID")) names.Insert(0, "SOLID");
					this.AddPropertyRow(panel, "패턴 이름", this.CreateSimpleDropdown(names, cur, name =>
					{
						var oldPat = hatch.Pattern;
						bool oldSolid = hatch.IsSolid;
						var pat = name == "SOLID" ? null : Free2DCad.Core.Hatching.HatchPatternLibrary.Get(name);
						this.Exec("해치 패턴 변경", () =>
						{
							if (name == "SOLID") { hatch.IsSolid = true; hatch.Pattern = new HatchPattern("SOLID"); }
							else if (pat != null) { var np = pat.Clone(); np.Update(XY.Zero, hatch.PatternAngle, hatch.PatternScale); hatch.IsSolid = false; hatch.Pattern = np; }
						}, () => { hatch.Pattern = oldPat; hatch.IsSolid = oldSolid; });
					}));
					this.AddEditableNumericRow(panel, "축척", hatch.PatternScale, v => { if (v <= 0) return; var o = hatch.PatternScale; this.Exec("해치 축척 변경", () => hatch.PatternScale = v, () => hatch.PatternScale = o); });
					this.AddAngleRow(panel, "각도", hatch.PatternAngle, r => { var o = hatch.PatternAngle; this.Exec("해치 각도 변경", () => hatch.PatternAngle = r, () => hatch.PatternAngle = o); });
					this.AddPropertyRow(panel, "연관", hatch.IsAssociative ? "예" : "아니오");
					this.AddPropertyRow(panel, "경계 루프 수", $"{hatch.Paths.Count}");
					return true;
				}
			case Ellipse el:
				{
					double major = Math.Sqrt(el.MajorAxisEndPoint.X * el.MajorAxisEndPoint.X + el.MajorAxisEndPoint.Y * el.MajorAxisEndPoint.Y);
					this.AddEditableNumericRow(panel, "중심 X", el.Center.X, v => { var o = el.Center; this.Exec("중심 X 변경", () => el.Center = new XYZ(v, el.Center.Y, 0), () => el.Center = o); });
					this.AddEditableNumericRow(panel, "중심 Y", el.Center.Y, v => { var o = el.Center; this.Exec("중심 Y 변경", () => el.Center = new XYZ(el.Center.X, v, 0), () => el.Center = o); });
					this.AddEditableNumericRow(panel, "장축 반지름", major, v =>
					{
						if (v <= 0 || major <= 0) return;
						var o = el.MajorAxisEndPoint;
						this.Exec("장축 변경", () => el.MajorAxisEndPoint = new XYZ(o.X / major * v, o.Y / major * v, 0), () => el.MajorAxisEndPoint = o);
					});
					this.AddEditableNumericRow(panel, "단축 반지름", major * el.RadiusRatio, v =>
					{
						if (v <= 0 || major <= 0 || v > major) return;
						var o = el.RadiusRatio;
						this.Exec("단축 변경", () => el.RadiusRatio = v / major, () => el.RadiusRatio = o);
					});
					this.AddEditableNumericRow(panel, "반지름 비율", el.RadiusRatio, v => { if (v <= 0 || v > 1) return; var o = el.RadiusRatio; this.Exec("반지름 비율 변경", () => el.RadiusRatio = v, () => el.RadiusRatio = o); });
					this.AddAngleRow(panel, "시작 각도", el.StartParameter, r => { var o = el.StartParameter; this.Exec("시작 각도 변경", () => el.StartParameter = r, () => el.StartParameter = o); });
					this.AddAngleRow(panel, "끝 각도", el.EndParameter, r => { var o = el.EndParameter; this.Exec("끝 각도 변경", () => el.EndParameter = r, () => el.EndParameter = o); });
					return true;
				}
			case Spline sp:
				this.AddPropertyRow(panel, "차수", $"{sp.Degree}");
				this.AddPropertyRow(panel, "조정점 수", $"{sp.ControlPoints.Count}");
				this.AddPropertyRow(panel, "맞춤점 수", $"{sp.FitPoints.Count}");
				this.AddPropertyRow(panel, "닫힘", sp.Flags.HasFlag(SplineFlags.Closed) ? "예" : "아니오");
				this.AddPropertyRow(panel, "길이", $"{Free2DCad.Core.Geometry.Geom.Length(Free2DCad.Core.Geometry.Geom.SegmentsOf(sp)):F4}");
				return true;
			case ACadSharp.Entities.Point pt:
				this.AddEditableNumericRow(panel, "위치 X", pt.Location.X, v => { var o = pt.Location; this.Exec("위치 X 변경", () => pt.Location = new XYZ(v, pt.Location.Y, pt.Location.Z), () => pt.Location = o); });
				this.AddEditableNumericRow(panel, "위치 Y", pt.Location.Y, v => { var o = pt.Location; this.Exec("위치 Y 변경", () => pt.Location = new XYZ(pt.Location.X, v, pt.Location.Z), () => pt.Location = o); });
				this.AddPropertyRow(panel, "위치 Z", $"{pt.Location.Z:F4}");
				return true;
			case XLine xl:
				this.AddEditableNumericRow(panel, "기준점 X", xl.FirstPoint.X, v => { var o = xl.FirstPoint; this.Exec("기준점 X 변경", () => xl.FirstPoint = new XYZ(v, xl.FirstPoint.Y, 0), () => xl.FirstPoint = o); });
				this.AddEditableNumericRow(panel, "기준점 Y", xl.FirstPoint.Y, v => { var o = xl.FirstPoint; this.Exec("기준점 Y 변경", () => xl.FirstPoint = new XYZ(xl.FirstPoint.X, v, 0), () => xl.FirstPoint = o); });
				this.AddAngleRow(panel, "방향 각도", Math.Atan2(xl.Direction.Y, xl.Direction.X), r => { var o = xl.Direction; this.Exec("방향 변경", () => xl.Direction = new XYZ(Math.Cos(r), Math.Sin(r), 0), () => xl.Direction = o); });
				return true;
			case Ray ray:
				this.AddEditableNumericRow(panel, "시작점 X", ray.StartPoint.X, v => { var o = ray.StartPoint; this.Exec("시작점 X 변경", () => ray.StartPoint = new XYZ(v, ray.StartPoint.Y, 0), () => ray.StartPoint = o); });
				this.AddEditableNumericRow(panel, "시작점 Y", ray.StartPoint.Y, v => { var o = ray.StartPoint; this.Exec("시작점 Y 변경", () => ray.StartPoint = new XYZ(ray.StartPoint.X, v, 0), () => ray.StartPoint = o); });
				this.AddAngleRow(panel, "방향 각도", Math.Atan2(ray.Direction.Y, ray.Direction.X), r => { var o = ray.Direction; this.Exec("방향 변경", () => ray.Direction = new XYZ(Math.Cos(r), Math.Sin(r), 0), () => ray.Direction = o); });
				return true;
			case MLine ml:
				this.AddPropertyRow(panel, "스타일", ml.Style?.Name ?? "STANDARD");
				this.AddPropertyRow(panel, "자리맞추기", ml.Justification switch { MLineJustification.Top => "위", MLineJustification.Zero => "0", _ => "아래" });
				this.AddPropertyRow(panel, "축척", $"{ml.ScaleFactor:0.####}");
				this.AddPropertyRow(panel, "정점 수", $"{ml.Vertices.Count}");
				return true;
		}
		return false;
	}
}

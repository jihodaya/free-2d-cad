using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using CSMath.Extensions;
using Free2DCad.Core.Actions;

namespace Free2DCad.App;

/// <summary>
/// 객체 하나를 골랐을 때 기존 CAD와 같은 분류·항목: 기하학(시작·끝·델타·노멀 …), 텍스트, 기타(역방향·뒤집기, 블록 이름·회전 …).
/// 숫자는 기존 CAD처럼 소수 넷째 자리 (0은 "0"), 각도는 도면 각도 정밀도(AUPREC).
/// 편집 칸에 값을 넣고 Enter → 되돌리기 가능한 변경.
/// </summary>
public sealed partial class PropertyPanel
{
	private Entity? _vertexEntity;
	private int _vertexIndex;

	/// <summary>기존 CAD 배치로 분류를 만들었으면 true (선·원·호·점·타원·폴리선·문자·여러 줄 문자·블록·해치·스플라인).</summary>
	private bool BuildStdEntity(Entity entity)
	{
		switch (entity)
		{
			case Line line:
				{
					var g = this.Section("기하학");
					this.XyzRows(g, "시작", line.StartPoint, p => this.Set("시작점 변경", () => line.StartPoint, v => line.StartPoint = v, p));
					this.XyzRows(g, "끝", line.EndPoint, p => this.Set("끝점 변경", () => line.EndPoint, v => line.EndPoint = v, p));
					var d = line.EndPoint - line.StartPoint;
					this.XyzRows(g, "델타", d, null);
					this.NumRow(g, "길이", Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z), null);
					this.AngRow(g, "각도", Math.Atan2(d.Y, d.X), null);
					return true;
				}
			case Arc arc:
				{
					var g = this.Section("기하학");
					XYZ At(double a) => new XYZ(arc.Center.X + arc.Radius * Math.Cos(a), arc.Center.Y + arc.Radius * Math.Sin(a), arc.Center.Z);
					double span = arc.EndAngle - arc.StartAngle;
					if (span <= 0) span += 2 * Math.PI;
					this.XyzRows(g, "시작", At(arc.StartAngle), null);
					this.XyzRows(g, "중심", arc.Center, p => this.Set("중심 변경", () => arc.Center, v => arc.Center = v, p));
					this.XyzRows(g, "끝", At(arc.EndAngle), null);
					this.NumRow(g, "반지름", arc.Radius, v => { if (v > 1e-9) this.Set("반지름 변경", () => arc.Radius, x => arc.Radius = x, v); });
					this.AngRow(g, "시작 각도", arc.StartAngle, r => this.Set("시작 각도 변경", () => arc.StartAngle, x => arc.StartAngle = x, r));
					this.AngRow(g, "끝 각도", arc.EndAngle, r => this.Set("끝 각도 변경", () => arc.EndAngle, x => arc.EndAngle = x, r));
					this.AngRow(g, "전체 각도", span, null);
					this.NumRow(g, "호 길이", arc.Radius * span, null);
					this.NumRow(g, "면적", 0.5 * arc.Radius * arc.Radius * (span - Math.Sin(span)), null);
					this.XyzRows(g, "노멀", arc.Normal, null);
					return true;
				}
			case Circle circle:
				{
					var g = this.Section("기하학");
					this.XyzRows(g, "중심", circle.Center, p => this.Set("중심 변경", () => circle.Center, v => circle.Center = v, p));
					void R(double r, string name) { if (r > 1e-9) this.Set(name, () => circle.Radius, x => circle.Radius = x, r); }
					this.NumRow(g, "반지름", circle.Radius, v => R(v, "반지름 변경"));
					this.NumRow(g, "지름", circle.Radius * 2, v => R(v / 2, "지름 변경"));
					this.NumRow(g, "둘레", 2 * Math.PI * circle.Radius, v => R(v / (2 * Math.PI), "둘레 변경"));
					this.NumRow(g, "면적", Math.PI * circle.Radius * circle.Radius, v => { if (v > 0) R(Math.Sqrt(v / Math.PI), "면적 변경"); });
					this.XyzRows(g, "노멀", circle.Normal, null);
					return true;
				}
			case ACadSharp.Entities.Point pt:
				{
					var g = this.Section("기하학");
					this.XyzRows(g, "위치", pt.Location, p => this.Set("위치 변경", () => pt.Location, v => pt.Location = v, p));
					return true;
				}
			case Ellipse el:
				this.BuildEllipse(el);
				return true;
			case LwPolyline poly:
				this.BuildPolyline(poly);
				return true;
			case AttributeDefinition ad:
				this.BuildAttDef(ad);
				return true;
			case TextEntity text when text is not AttributeBase:
				this.BuildText(text);
				return true;
			case MText mtext:
				this.BuildMText(mtext);
				return true;
			case Insert insert when insert is not TableEntity:
				this.BuildInsert(insert);
				return true;
			case Hatch hatch:
				this.BuildHatch(hatch);
				return true;
			case Spline sp:
				this.BuildSpline(sp);
				return true;
			case XLine xl:
				this.BuildInfiniteLine(() => xl.FirstPoint, v => xl.FirstPoint = v, () => xl.Direction, v => xl.Direction = v);
				return true;
			case Ray ray:
				this.BuildInfiniteLine(() => ray.StartPoint, v => ray.StartPoint = v, () => ray.Direction, v => ray.Direction = v);
				return true;
			case MLine ml:
				this.BuildMLine(ml);
				return true;
			case Leader leader:
				this.BuildLeader(leader);
				return true;
			case MultiLeader mld:
				this.BuildMultiLeader(mld);
				return true;
			case TableEntity table:
				this.BuildTable(table);
				return true;
			case Solid so:
				this.BuildSolid(so);
				return true;
			case Wipeout wo:
				this.BuildWipeout(wo);
				return true;
		}
		return false;
	}

	private void BuildEllipse(Ellipse el)
	{
		var g = this.Section("기하학");
		var m = el.MajorAxisEndPoint;
		double major = Math.Sqrt(m.X * m.X + m.Y * m.Y + m.Z * m.Z);
		var minorVec = new XYZ(-m.Y * el.RadiusRatio, m.X * el.RadiusRatio, 0);
		XYZ At(double t) => new XYZ(el.Center.X + m.X * Math.Cos(t) + minorVec.X * Math.Sin(t), el.Center.Y + m.Y * Math.Cos(t) + minorVec.Y * Math.Sin(t), el.Center.Z);
		this.XyzRows(g, "시작", At(el.StartParameter), null);
		this.XyzRows(g, "중심", el.Center, p => this.Set("중심 변경", () => el.Center, v => el.Center = v, p));
		this.XyzRows(g, "끝", At(el.EndParameter), null);
		this.NumRow(g, "주 반지름", major, v =>
		{
			if (v <= 0 || major <= 0) return;
			this.Set("주 반지름 변경", () => el.MajorAxisEndPoint, x => el.MajorAxisEndPoint = x, new XYZ(m.X / major * v, m.Y / major * v, m.Z / major * v));
		});
		this.NumRow(g, "부 반지름", major * el.RadiusRatio, v =>
		{
			if (v <= 0 || major <= 0 || v > major) return;
			this.Set("부 반지름 변경", () => el.RadiusRatio, x => el.RadiusRatio = x, v / major);
		});
		this.NumRow(g, "반지름 비율", el.RadiusRatio, v => { if (v > 0 && v <= 1) this.Set("반지름 비율 변경", () => el.RadiusRatio, x => el.RadiusRatio = x, v); });
		this.AngRow(g, "시작 각도", el.StartParameter, r => this.Set("시작 각도 변경", () => el.StartParameter, x => el.StartParameter = x, r));
		this.AngRow(g, "끝 각도", el.EndParameter, r => this.Set("끝 각도 변경", () => el.EndParameter, x => el.EndParameter = x, r));
		this.XyzRows(g, "주 축 벡터", m, null);
		this.XyzRows(g, "부 축 벡터", minorVec, null);
		double sweep = el.EndParameter - el.StartParameter;
		if (sweep <= 0) sweep += 2 * Math.PI;
		this.NumRow(g, "면적", Math.PI * major * major * el.RadiusRatio * Math.Min(1, sweep / (2 * Math.PI)), null);
	}

	private void BuildPolyline(LwPolyline poly)
	{
		var g = this.Section("기하학");
		int n = poly.Vertices.Count;
		if (!ReferenceEquals(this._vertexEntity, poly) || this._vertexIndex >= n)
		{
			this._vertexEntity = poly;
			this._vertexIndex = 0;
		}
		if (n > 0)
		{
			var names = Enumerable.Range(1, n).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
			this.AddPropertyRow(g, "꼭짓점", this.CreateSimpleDropdown(names, names[this._vertexIndex], name =>
			{
				this._vertexIndex = int.Parse(name, CultureInfo.InvariantCulture) - 1;
				Avalonia.Threading.Dispatcher.UIThread.Post(() => this.UpdateSelection(this._selected));
			}));
			var vx = poly.Vertices[this._vertexIndex];
			this.NumRow(g, "꼭짓점 X", vx.Location.X, v => this.Set("꼭짓점 변경", () => vx.Location, x => vx.Location = x, new XY(v, vx.Location.Y)));
			this.NumRow(g, "꼭짓점 Y", vx.Location.Y, v => this.Set("꼭짓점 변경", () => vx.Location, x => vx.Location = x, new XY(vx.Location.X, v)));
			this.NumRow(g, "시작 세그먼트 폭", vx.StartWidth, v => { if (v >= 0) this.Set("시작 세그먼트 폭 변경", () => vx.StartWidth, x => vx.StartWidth = x, v); });
			this.NumRow(g, "끝 세그먼트 폭", vx.EndWidth, v => { if (v >= 0) this.Set("끝 세그먼트 폭 변경", () => vx.EndWidth, x => vx.EndWidth = x, v); });
		}
		this.NumRow(g, "전체 너비(폭)", poly.ConstantWidth, v =>
		{
			if (v < 0) return;
			// 전체 폭은 정점별 폭을 모두 같은 값으로 (기존 CAD와 같음)
			var olds = poly.Vertices.Select(x => (x.StartWidth, x.EndWidth)).ToList();
			double oldC = poly.ConstantWidth;
			this.Exec("전체 폭 변경", () =>
			{
				poly.ConstantWidth = v;
				foreach (var x in poly.Vertices) { x.StartWidth = v; x.EndWidth = v; }
			}, () =>
			{
				poly.ConstantWidth = oldC;
				for (int i = 0; i < olds.Count && i < poly.Vertices.Count; i++) { poly.Vertices[i].StartWidth = olds[i].StartWidth; poly.Vertices[i].EndWidth = olds[i].EndWidth; }
			});
		});
		this.NumRow(g, "고도", poly.Elevation, v => this.Set("고도 변경", () => poly.Elevation, x => poly.Elevation = x, v));
		this.NumRow(g, "면적", PolylineArea(poly), null);
		this.NumRow(g, "길이", Free2DCad.Core.Geometry.Geom.Length(Free2DCad.Core.Geometry.Geom.SegmentsOf(poly)), null);

		var misc = this.Section("기타");
		this.AddPropertyRow(misc, "닫힘", this.CreateSimpleDropdown(new[] { "예", "No" }, poly.IsClosed ? "예" : "No", name =>
			this.Set("닫힘 변경", () => poly.IsClosed, x => poly.IsClosed = x, name == "예")));
		bool gen = poly.Flags.HasFlag(LwPolylineFlags.Plinegen);
		this.AddPropertyRow(misc, "선종류 생성", this.CreateSimpleDropdown(new[] { "예", "No" }, gen ? "예" : "No", name =>
		{
			var flags = name == "예" ? poly.Flags | LwPolylineFlags.Plinegen : poly.Flags & ~LwPolylineFlags.Plinegen;
			this.Set("선종류 생성 변경", () => poly.Flags, x => poly.Flags = x, flags);
		}));

		if (RectangleOf(poly) is { } rc)
		{
			this.BuildRectangleShape(poly, rc);
		}
	}

	/// <summary>직사각형 폴리선 (닫힘, 꼭짓점 4개, 호 없음, 직각): 중심, 첫 변 방향, 길이(첫 변), 너비(둘째 변).</summary>
	private static (XY Center, double Angle, double Length, double Width)? RectangleOf(LwPolyline poly)
	{
		if (!poly.IsClosed || poly.Vertices.Count != 4 || poly.Vertices.Any(v => Math.Abs(v.Bulge) > 1e-12))
		{
			return null;
		}
		var p = poly.Vertices.Select(v => v.Location).ToArray();
		XY e1 = p[1] - p[0], e2 = p[2] - p[1], e3 = p[3] - p[2], e4 = p[0] - p[3];
		double l1 = e1.GetLength(), l2 = e2.GetLength();
		if (l1 < 1e-9 || l2 < 1e-9) return null;
		double tol = 1e-6 * Math.Max(l1, l2);
		if (Math.Abs(e1.Dot(e2)) > tol * Math.Max(l1, l2) || (e1 + e3).GetLength() > tol || (e2 + e4).GetLength() > tol)
		{
			return null;
		}
		var c = new XY((p[0].X + p[2].X) / 2, (p[0].Y + p[2].Y) / 2);
		// 기존 CAD 표기: 첫 변 방향 − 90° (예: 오른쪽으로 그린 사각형 = 270)
		double ang = Math.Atan2(e1.Y, e1.X) - Math.PI / 2;
		return (c, ang, l1, l2);
	}

	/// <summary>기존 CAD "Rectangle shape" 분류: 중심·각도·길이·너비를 바꾸면 사각형을 다시 만든다.</summary>
	private void BuildRectangleShape(LwPolyline poly, (XY Center, double Angle, double Length, double Width) rc)
	{
		var r = this.Section("Rectangle shape");
		void Rebuild(string name, XY center, double angle, double length, double width)
		{
			var old = poly.Vertices.Select(v => v.Location).ToList();
			double dir = angle + Math.PI / 2;
			var u = new XY(Math.Cos(dir), Math.Sin(dir));
			var w = new XY(-u.Y, u.X);
			// 원래 둘째 변이 첫 변의 왼쪽인지 오른쪽인지 유지
			var p = old;
			double side = Math.Sign(XY.Cross(p[1] - p[0], p[2] - p[1]));
			if (side == 0) side = 1;
			w = w * side;
			var p0 = center - u * (length / 2) - w * (width / 2);
			var pts = new[] { p0, p0 + u * length, p0 + u * length + w * width, p0 + w * width };
			this.Exec(name, () =>
			{
				for (int i = 0; i < 4; i++) poly.Vertices[i].Location = pts[i];
			}, () =>
			{
				for (int i = 0; i < 4; i++) poly.Vertices[i].Location = old[i];
			});
		}
		this.NumRow(r, "중심 X", rc.Center.X, v => Rebuild("사각형 중심 변경", new XY(v, rc.Center.Y), rc.Angle, rc.Length, rc.Width));
		this.NumRow(r, "중심 Y", rc.Center.Y, v => Rebuild("사각형 중심 변경", new XY(rc.Center.X, v), rc.Angle, rc.Length, rc.Width));
		this.AngRow(r, "각도", rc.Angle, a => Rebuild("사각형 각도 변경", rc.Center, a, rc.Length, rc.Width));
		this.NumRow(r, "길이", rc.Length, v => { if (v > 1e-9) Rebuild("사각형 길이 변경", rc.Center, rc.Angle, v, rc.Width); });
		this.NumRow(r, "너비(폭)", rc.Width, v => { if (v > 1e-9) Rebuild("사각형 너비 변경", rc.Center, rc.Angle, rc.Length, v); });
	}

	/// <summary>폴리선 면적 (호 구간 포함, 열린 폴리선은 끝점끼리 이은 면적).</summary>
	private static double PolylineArea(LwPolyline poly)
	{
		var v = poly.Vertices;
		int n = v.Count;
		if (n < 2) return 0;
		double a = 0;
		for (int i = 0; i < n; i++)
		{
			var p = v[i].Location;
			var q = v[(i + 1) % n].Location;
			a += p.X * q.Y - q.X * p.Y;
			bool lastOpen = !poly.IsClosed && i == n - 1;
			double b = v[i].Bulge;
			if (!lastOpen && Math.Abs(b) > 1e-12)
			{
				double c = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
				double th = 4 * Math.Atan(Math.Abs(b));
				double r = c / (2 * Math.Sin(th / 2));
				a += Math.Sign(b) * r * r * (th - Math.Sin(th)); // ×2 (아래에서 반으로 나눔)
			}
		}
		return Math.Abs(a) / 2;
	}

	private static readonly (string Name, TextHorizontalAlignment H, TextVerticalAlignmentType V)[] TextJustifications =
	{
		("왼쪽", TextHorizontalAlignment.Left, TextVerticalAlignmentType.Baseline),
		("가운데", TextHorizontalAlignment.Center, TextVerticalAlignmentType.Baseline),
		("오른쪽", TextHorizontalAlignment.Right, TextVerticalAlignmentType.Baseline),
		("정렬됨", TextHorizontalAlignment.Aligned, TextVerticalAlignmentType.Baseline),
		("중간", TextHorizontalAlignment.Middle, TextVerticalAlignmentType.Baseline),
		("맞춤", TextHorizontalAlignment.Fit, TextVerticalAlignmentType.Baseline),
		("상단 왼쪽", TextHorizontalAlignment.Left, TextVerticalAlignmentType.Top),
		("상단 중앙", TextHorizontalAlignment.Center, TextVerticalAlignmentType.Top),
		("상단 오른쪽", TextHorizontalAlignment.Right, TextVerticalAlignmentType.Top),
		("중간 왼쪽", TextHorizontalAlignment.Left, TextVerticalAlignmentType.Middle),
		("중간 중앙", TextHorizontalAlignment.Center, TextVerticalAlignmentType.Middle),
		("중간 오른쪽", TextHorizontalAlignment.Right, TextVerticalAlignmentType.Middle),
		("하단 왼쪽", TextHorizontalAlignment.Left, TextVerticalAlignmentType.Bottom),
		("하단 중앙", TextHorizontalAlignment.Center, TextVerticalAlignmentType.Bottom),
		("하단 오른쪽", TextHorizontalAlignment.Right, TextVerticalAlignmentType.Bottom),
	};

	private void BuildText(TextEntity text)
	{
		var t = this.Section("텍스트");
		this.AddEditableTextRow(t, "내용", text.Value ?? "", nv => this._doc?.ChangeText(text, nv));
		this.AddPropertyRow(t, "스타일", this.CreateTextStyleDropdown(text.Style, st => this.Set("문자 스타일 변경", () => text.Style, x => text.Style = x, st)));
		this.AddPropertyRow(t, "주석", "No");
		var cur = TextJustifications.FirstOrDefault(j => j.H == text.HorizontalAlignment && (j.V == text.VerticalAlignment
			|| (j.V == TextVerticalAlignmentType.Baseline && text.HorizontalAlignment is TextHorizontalAlignment.Aligned or TextHorizontalAlignment.Middle or TextHorizontalAlignment.Fit)));
		string curName = cur.Name ?? TextJustifyName(text);
		this.AddPropertyRow(t, "정렬", this.CreateSimpleDropdown(TextJustifications.Select(j => j.Name).ToList(), curName, name =>
		{
			var j = TextJustifications.First(x => x.Name == name);
			bool wasLeft = text.HorizontalAlignment == TextHorizontalAlignment.Left && text.VerticalAlignment == TextVerticalAlignmentType.Baseline;
			var anchor = wasLeft ? text.InsertPoint : text.AlignmentPoint;
			var old = (text.HorizontalAlignment, text.VerticalAlignment, text.InsertPoint, text.AlignmentPoint);
			// 기준점은 그대로 두고 자리맞추기만 바꾼다 (새 기준점 = 이전 기준점)
			this.Exec("자리맞추기 변경", () =>
			{
				text.HorizontalAlignment = j.H;
				text.VerticalAlignment = j.V;
				text.InsertPoint = anchor;
				text.AlignmentPoint = anchor;
			}, () =>
			{
				text.HorizontalAlignment = old.Item1;
				text.VerticalAlignment = old.Item2;
				text.InsertPoint = old.Item3;
				text.AlignmentPoint = old.Item4;
			});
		}));
		this.NumRow(t, "높이", text.Height, v => { if (v > 1e-9) this.Set("문자 높이 변경", () => text.Height, x => text.Height = x, v); });
		this.AngRow(t, "회전", text.Rotation, r => this.Set("문자 회전 변경", () => text.Rotation, x => text.Rotation = x, r));
		this.NumRow(t, "너비(폭) 인자", text.WidthFactor, v => { if (v > 0) this.Set("폭 비율 변경", () => text.WidthFactor, x => text.WidthFactor = x, v); });
		this.AngRow(t, "경사각", text.ObliqueAngle, r => this.Set("기울기 변경", () => text.ObliqueAngle, x => text.ObliqueAngle = x, r));
		bool left = text.HorizontalAlignment == TextHorizontalAlignment.Left && text.VerticalAlignment == TextVerticalAlignmentType.Baseline;
		this.XyzRows(t, "텍스트 정렬", left ? XYZ.Zero : text.AlignmentPoint, left ? null : p => this.Set("정렬점 변경", () => text.AlignmentPoint, x => text.AlignmentPoint = x, p));

		var g = this.Section("기하학");
		this.XyzRows(g, "위치", text.InsertPoint, p => this.Set("위치 변경", () => text.InsertPoint, x => text.InsertPoint = x, p));

		var misc = this.Section("기타");
		void Mirror(string label, TextMirrorFlag flag)
		{
			bool on = text.Mirror.HasFlag(flag);
			this.AddPropertyRow(misc, label, this.CreateSimpleDropdown(new[] { "예", "No" }, on ? "예" : "No", name =>
			{
				var nf = name == "예" ? text.Mirror | flag : text.Mirror & ~flag;
				this.Set(label + " 변경", () => text.Mirror, x => text.Mirror = x, nf);
			}));
		}
		Mirror("역방향", TextMirrorFlag.Backward);
		Mirror("뒤집기", TextMirrorFlag.UpsideDown);
	}

	private void BuildMText(MText mtext)
	{
		var t = this.Section("텍스트");
		this.AddEditableTextRow(t, "내용", mtext.Value ?? "", nv => this._doc?.ChangeText(mtext, nv));
		this.AddPropertyRow(t, "스타일", this.CreateTextStyleDropdown(mtext.Style, st => this.Set("문자 스타일 변경", () => mtext.Style, x => mtext.Style = x, st)));
		this.AddPropertyRow(t, "주석", "No");
		int ap = Math.Clamp((int)mtext.AttachmentPoint, 1, 9);
		this.AddPropertyRow(t, "정렬", this.CreateSimpleDropdown(AttachNames, AttachNames[ap - 1], name =>
		{
			int idx = Array.IndexOf(AttachNames, name);
			if (idx >= 0) this.Set("자리맞추기 변경", () => mtext.AttachmentPoint, x => mtext.AttachmentPoint = x, (AttachmentPointType)(idx + 1));
		}));
		this.AddPropertyRow(t, "방향", mtext.DrawingDirection == DrawingDirectionType.TopToBottom ? "수직" : "수평");
		this.NumRow(t, "텍스트 높이", mtext.Height, v => { if (v > 1e-9) this.Set("문자 높이 변경", () => mtext.Height, x => mtext.Height = x, v); });
		this.AngRow(t, "회전", Math.Atan2(mtext.AlignmentPoint.Y, mtext.AlignmentPoint.X), r =>
			this.Set("문자 회전 변경", () => mtext.AlignmentPoint, x => mtext.AlignmentPoint = x, new XYZ(Math.Cos(r), Math.Sin(r), 0)));
		double spacingFactor = mtext.LineSpacing > 0 ? mtext.LineSpacing : 1;
		this.NumRow(t, "줄 간격 인자", spacingFactor, v => { if (v > 0) this.Set("선 간격 변경", () => mtext.LineSpacing, x => mtext.LineSpacing = x, v); });
		this.NumRow(t, "줄 간격 거리", spacingFactor * mtext.Height * 5.0 / 3.0, v =>
		{
			if (v > 0 && mtext.Height > 0) this.Set("선 간격 변경", () => mtext.LineSpacing, x => mtext.LineSpacing = x, v / (mtext.Height * 5.0 / 3.0));
		});
		this.AddPropertyRow(t, "줄 간격 스타일", this.CreateSimpleDropdown(new[] { "최소", "정확히" }, mtext.LineSpacingStyle == LineSpacingStyleType.Exact ? "정확히" : "최소", name =>
			this.Set("선 간격 스타일 변경", () => mtext.LineSpacingStyle, x => mtext.LineSpacingStyle = x, name == "정확히" ? LineSpacingStyleType.Exact : LineSpacingStyleType.AtLeast)));
		bool mask = mtext.BackgroundFillFlags != BackgroundFillFlags.None;
		this.AddPropertyRow(t, "배경 마스크", this.CreateSimpleDropdown(new[] { "예", "No" }, mask ? "예" : "No", name =>
		{
			var nf = name == "예" ? BackgroundFillFlags.UseDrawingWindowColor : BackgroundFillFlags.None;
			this.Set("배경 마스크 변경", () => mtext.BackgroundFillFlags, x => mtext.BackgroundFillFlags = x, nf);
		}));
		this.NumRow(t, "정의된 너비", mtext.RectangleWidth, v => { if (v >= 0) this.Set("문자 폭 변경", () => mtext.RectangleWidth, x => mtext.RectangleWidth = x, v); });
		this.NumRow(t, "정의된 높이", mtext.RectangleHeight, v => { if (v >= 0) this.Set("문자 높이 칸 변경", () => mtext.RectangleHeight, x => mtext.RectangleHeight = x, v); });
		this.AddPropertyRow(t, "열", "아니오");

		var g = this.Section("기하학");
		this.XyzRows(g, "위치", mtext.InsertPoint, p => this.Set("위치 변경", () => mtext.InsertPoint, x => mtext.InsertPoint = x, p));
	}

	private void BuildInsert(Insert insert)
	{
		var g = this.Section("기하학");
		this.XyzRows(g, "위치", insert.InsertPoint, p => this.SetInsert(insert, "위치 변경", () => insert.InsertPoint, x => insert.InsertPoint = x, p));
		this.NumRow(g, "축척 X", insert.XScale, v => { if (Math.Abs(v) > 1e-12) this.SetInsert(insert, "축척 X 변경", () => insert.XScale, x => insert.XScale = x, v); });
		this.NumRow(g, "축척 Y", insert.YScale, v => { if (Math.Abs(v) > 1e-12) this.SetInsert(insert, "축척 Y 변경", () => insert.YScale, x => insert.YScale = x, v); });
		this.NumRow(g, "축척 Z", insert.ZScale, v => { if (Math.Abs(v) > 1e-12) this.SetInsert(insert, "축척 Z 변경", () => insert.ZScale, x => insert.ZScale = x, v); });

		var misc = this.Section("기타");
		this.AddPropertyRow(misc, "주석", "No");
		var blocks = this._doc?.Document.BlockRecords
			.Where(b => !b.IsAnonymous && !b.Name.StartsWith('*') && b.Layout == null && !b.Name.StartsWith('_'))
			.Select(b => b.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
		string curBlock = insert.Block?.Name ?? "";
		if (!blocks.Contains(curBlock)) blocks.Insert(0, curBlock);
		this.AddPropertyRow(misc, "이름", this.CreateSimpleDropdown(blocks, curBlock, name =>
		{
			if (this._doc != null && this._doc.Document.BlockRecords.TryGetValue(name, out var br) && insert.Block != null)
			{
				var old = insert.Block;
				this.Exec("블록 이름 변경", () => insert.ReplaceBlock(br), () => insert.ReplaceBlock(old));
			}
		}));
		this.AngRow(misc, "회전", insert.Rotation, r => this.SetInsert(insert, "회전 변경", () => insert.Rotation, x => insert.Rotation = x, r));

		if (insert.Attributes.Count > 0)
		{
			var atts = this.Section("속성");
			foreach (var att in insert.Attributes)
			{
				var a = att;
				this.AddEditableTextRow(atts, a.Tag, a.Value ?? "", nv => this.Set($"속성 {a.Tag} 변경", () => a.Value ?? "", x => a.Value = x, nv));
			}
		}
	}

	private void BuildHatch(Hatch hatch)
	{
		var misc = this.Section("기타");
		this.AddPropertyRow(misc, "주석", "No");

		var p = this.Section("패턴");
		// DXF 76: 0 = 사용자 정의, 1 = 미리 정의, 2 = 사용자
		string type = hatch.IsSolid ? "미리 정의" : (int)hatch.PatternType switch { 0 => "사용자 정의", 2 => "사용자", _ => "미리 정의" };
		this.AddPropertyRow(p, "유형", type);
		var names = Free2DCad.Core.Hatching.HatchPatternLibrary.All.Select(x => x.Name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
		string cur = hatch.IsSolid ? "SOLID" : hatch.Pattern?.Name ?? "SOLID";
		if (!names.Contains(cur)) names.Insert(0, cur);
		if (!names.Contains("SOLID")) names.Insert(0, "SOLID");
		this.AddPropertyRow(p, "패턴 이름", this.CreateSimpleDropdown(names, cur, name =>
		{
			var oldPat = hatch.Pattern;
			bool oldSolid = hatch.IsSolid;
			var oldType = hatch.PatternType;
			var pat = name == "SOLID" ? null : Free2DCad.Core.Hatching.HatchPatternLibrary.Get(name);
			this.Exec("해치 패턴 변경", () =>
			{
				if (name == "SOLID") { hatch.IsSolid = true; hatch.Pattern = new HatchPattern("SOLID"); }
				else if (pat != null) { var np = pat.Clone(); np.Update(XY.Zero, hatch.PatternAngle, hatch.PatternScale); hatch.IsSolid = false; hatch.Pattern = np; }
				hatch.PatternType = (HatchPatternType)1; // 미리 정의
			}, () => { hatch.Pattern = oldPat; hatch.IsSolid = oldSolid; hatch.PatternType = oldType; });
		}));
		this.AngRow(p, "각도", hatch.PatternAngle, r => this.Set("해치 각도 변경", () => hatch.PatternAngle, x => hatch.PatternAngle = x, r));
		this.NumRow(p, "축척", hatch.PatternScale, v => { if (v > 0) this.Set("해치 축척 변경", () => hatch.PatternScale, x => hatch.PatternScale = x, v); });
		var line0 = hatch.Pattern?.Lines.FirstOrDefault();
		this.NumRow(p, "원점 X", line0?.BasePoint.X ?? 0, null);
		this.NumRow(p, "원점 Y", line0?.BasePoint.Y ?? 0, null);
		// 사용자 정의(_USER) 패턴만 선 간격, 그 밖은 기존 CAD처럼 1
		bool userPattern = string.Equals(hatch.Pattern?.Name, "_USER", StringComparison.OrdinalIgnoreCase) || string.Equals(hatch.Pattern?.Name, "_U", StringComparison.OrdinalIgnoreCase);
		double spacing = userPattern && line0 != null ? line0.Offset.GetLength() : 1.0;
		this.NumRow(p, "간격", spacing, null);
		this.AddPropertyRow(p, "이중", hatch.IsDouble ? "예" : "No");
		this.AddPropertyRow(p, "연관성", hatch.IsAssociative ? "예" : "No");
		var styles = new[] { "일반", "외부", "무시" };
		int si = Math.Clamp((int)hatch.Style, 0, 2);
		this.AddPropertyRow(p, "섬 감지 스타일", this.CreateSimpleDropdown(styles, styles[si], name =>
		{
			int ni = Array.IndexOf(styles, name);
			if (ni >= 0) this.Set("섬 감지 스타일 변경", () => hatch.Style, x => hatch.Style = x, (HatchStyleType)ni);
		}));
		{
			// 배경 색상 (XData HATCHBACKGROUNDCOLOR, 1071 = 0xC3000000 | 색상 번호)
			var bg = Free2DCad.Rendering.SceneBuilder.HatchBackground(hatch);
			var bgNames = new List<string> { "없음" };
			bgNames.AddRange(ColorOptions.Skip(2).Select(o => o.Name));
			string bgCur = bg == null ? "없음" : ColorOptions.FirstOrDefault(o => o.Color.Index == bg.Value.Index)?.Name ?? $"색상 {bg.Value.Index}";
			if (!bgNames.Contains(bgCur)) bgNames.Insert(1, bgCur);
			this.AddPropertyRow(p, "배경 색상", this.CreateSimpleDropdown(bgNames, bgCur, name =>
			{
				var opt = ColorOptions.FirstOrDefault(o => o.Name == name);
				if (name != "없음" && opt == null) return;
				var old = hatch.ExtendedData.TryGet("HATCHBACKGROUNDCOLOR", out var d0) ? d0.Records.ToList() : null;
				void Apply(List<ACadSharp.XData.ExtendedDataRecord>? recs)
				{
					hatch.ExtendedData.Remove("HATCHBACKGROUNDCOLOR");
					if (recs != null)
					{
						var d = hatch.ExtendedData.TryAdd("HATCHBACKGROUNDCOLOR", new ACadSharp.XData.ExtendedData());
						d.Records.AddRange(recs);
					}
				}
				List<ACadSharp.XData.ExtendedDataRecord>? next = name == "없음" ? null
					: new() { new ACadSharp.XData.ExtendedDataInteger32(unchecked((int)(0xC3000000u | (uint)opt!.Color.Index))) };
				this.Exec("배경 색상 변경", () => Apply(next), () => Apply(old));
			}));
		}

		var g = this.Section("기하학");
		this.NumRow(g, "고도", hatch.Elevation, v => this.Set("고도 변경", () => hatch.Elevation, x => hatch.Elevation = x, v));
		this.NumRow(g, "면적", HatchArea(hatch), null);
	}

	/// <summary>해치 면적: 바깥 경계 면적 − 안쪽 섬 면적 (경계를 잘게 나눈 다각형으로 계산).</summary>
	private static double HatchArea(Hatch hatch)
	{
		var loops = new List<(double Area, bool Outer)>();
		foreach (var path in hatch.Paths)
		{
			var pts = new List<XY>();
			foreach (var edge in path.Edges)
			{
				switch (edge)
				{
					case Hatch.BoundaryPath.Polyline pl:
						{
							var v = pl.Vertices.ToList();
							for (int i = 0; i < v.Count; i++)
							{
								var a = new XY(v[i].X, v[i].Y);
								pts.Add(a);
								double bulge = v[i].Z;
								bool last = i == v.Count - 1;
								if (Math.Abs(bulge) > 1e-12 && (!last || pl.IsClosed))
								{
									var bpt = new XY(v[(i + 1) % v.Count].X, v[(i + 1) % v.Count].Y);
									AddBulge(pts, a, bpt, bulge);
								}
							}
							break;
						}
					case Hatch.BoundaryPath.Line ln:
						pts.Add(ln.Start);
						break;
					case Hatch.BoundaryPath.Arc arc:
						{
							double s0 = arc.StartAngle, e0 = arc.EndAngle;
							if (!arc.CounterClockWise) { s0 = -s0; e0 = -e0; }
							double sweep = e0 - s0;
							if (arc.CounterClockWise) { while (sweep <= 0) sweep += 2 * Math.PI; }
							else { while (sweep >= 0) sweep -= 2 * Math.PI; }
							for (int k = 0; k < 32; k++)
							{
								double t = s0 + sweep * k / 32;
								pts.Add(new XY(arc.Center.X + arc.Radius * Math.Cos(t), arc.Center.Y + arc.Radius * Math.Sin(t)));
							}
							break;
						}
					case Hatch.BoundaryPath.Ellipse el:
						{
							var m = el.MajorAxisEndPoint;
							var w = new XY(-m.Y * el.RadiusRatio, m.X * el.RadiusRatio);
							double sweep = el.EndAngle - el.StartAngle;
							if (sweep <= 0) sweep += 2 * Math.PI;
							for (int k = 0; k < 32; k++)
							{
								double t = el.StartAngle + sweep * k / 32;
								if (!el.CounterClockWise) t = -t;
								pts.Add(new XY(el.Center.X + m.X * Math.Cos(t) + w.X * Math.Sin(t), el.Center.Y + m.Y * Math.Cos(t) + w.Y * Math.Sin(t)));
							}
							break;
						}
					case Hatch.BoundaryPath.Spline sp:
						foreach (var c in sp.ControlPoints) pts.Add(new XY(c.X, c.Y));
						break;
				}
			}
			if (pts.Count < 3) continue;
			double area = 0;
			for (int i = 0; i < pts.Count; i++)
			{
				var a = pts[i];
				var b = pts[(i + 1) % pts.Count];
				area += a.X * b.Y - b.X * a.Y;
			}
			loops.Add((Math.Abs(area) / 2, path.Flags.HasFlag(BoundaryPathFlags.External) || path.Flags.HasFlag(BoundaryPathFlags.Outermost)));
		}
		if (loops.Count == 0) return 0;
		if (!loops.Any(l => l.Outer))
		{
			var max = loops.Max(l => l.Area);
			return max - (loops.Sum(l => l.Area) - max);
		}
		return Math.Max(0, loops.Where(l => l.Outer).Sum(l => l.Area) - loops.Where(l => !l.Outer).Sum(l => l.Area));
	}

	private static void AddBulge(List<XY> pts, XY a, XY b, double bulge)
	{
		double th = 4 * Math.Atan(bulge);
		var chord = b - a;
		double c = chord.GetLength();
		if (c < 1e-12) return;
		double r = c / (2 * Math.Sin(Math.Abs(th) / 2));
		var mid = new XY((a.X + b.X) / 2, (a.Y + b.Y) / 2);
		double h = r * Math.Cos(Math.Abs(th) / 2);
		var nrm = new XY(-chord.Y / c, chord.X / c);
		var center = mid + nrm * (Math.Sign(bulge) * h);
		double s0 = Math.Atan2(a.Y - center.Y, a.X - center.X);
		for (int k = 1; k < 16; k++)
		{
			double t = s0 + th * k / 16;
			pts.Add(new XY(center.X + r * Math.Cos(t), center.Y + r * Math.Sin(t)));
		}
	}

	// ── 행 도우미 ──

	private StackPanel Section(string title)
	{
		if (this._recording != null)
		{
			this._recSection = title;
			return new StackPanel();
		}
		var panel = new StackPanel { Spacing = 0 };
		this._categoriesPanel.Children.Add(this.CreateCategorySection(title, panel));
		return panel;
	}

	/// <summary>값 바꾸기 (되돌리기 가능).</summary>
	private void Set<T>(string name, Func<T> get, Action<T> set, T value)
	{
		T old = get();
		this.Exec(name, () => set(value), () => set(old));
	}

	/// <summary>블록 삽입 위치·축척·회전 바꾸기: 속성(ATTRIB)도 같이 옮긴다 (기존 CAD처럼).</summary>
	private void SetInsert<T>(Insert insert, string name, Func<T> get, Action<T> set, T value)
	{
		T old = get();
		var oldM = insert.GetTransform().Matrix;
		set(value);
		var newM = insert.GetTransform().Matrix;
		set(old);
		if (insert.Attributes.Count == 0 || !Matrix4.Inverse(oldM, out var invOld) || !Matrix4.Inverse(newM, out var invNew))
		{
			this.Set(name, get, set, value);
			return;
		}
		var forward = new CSMath.Transform(newM * invOld);
		var back = new CSMath.Transform(oldM * invNew);
		this.Exec(name, () =>
		{
			set(value);
			foreach (var a in insert.Attributes) a.ApplyTransform(forward);
		}, () =>
		{
			set(old);
			foreach (var a in insert.Attributes) a.ApplyTransform(back);
		});
	}

	/// <summary>기존 CAD 숫자 표기: 0은 "0", 그 밖은 소수 넷째 자리.</summary>
	private static string Fmt(double v) => Math.Abs(v) < 5e-5 ? "0" : v.ToString("F4", CultureInfo.InvariantCulture);

	private static string AreaFmt(double v) => Math.Abs(v) < 5e-5 ? "0" : v.ToString("0.####", CultureInfo.InvariantCulture);

	/// <summary>각도(도) 표기: 도면 각도 정밀도(AUPREC)만큼.</summary>
	private string AngFmt(double radians)
	{
		double deg = radians * 180 / Math.PI;
		deg %= 360;
		if (deg < 0) deg += 360;
		int prec = Math.Clamp((int)(this._doc?.Document.Header.AngularUnitPrecision ?? 0), 0, 8);
		string s = deg.ToString("F" + prec, CultureInfo.InvariantCulture);
		return s == (360.0).ToString("F" + prec, CultureInfo.InvariantCulture) ? (0.0).ToString("F" + prec, CultureInfo.InvariantCulture) : s;
	}

	/// <summary>숫자 행 (apply가 없으면 읽기 전용).</summary>
	private void NumRow(StackPanel panel, string label, double value, Action<double>? apply)
	{
		if (apply == null)
		{
			// 기존 CAD는 면적만 끝자리 0을 지운다 (1200, 314.1593)
			this.AddPropertyRow(panel, label, label == "면적" ? AreaFmt(value) : Fmt(value));
			return;
		}
		this.AddPropertyRow(panel, label, this.EditBox(Fmt(value), s =>
		{
			if (TryNum(s, out double v)) apply(v);
		}));
	}

	/// <summary>각도 행 (도 입력 → 라디안).</summary>
	private void AngRow(StackPanel panel, string label, double radians, Action<double>? applyRadians)
	{
		if (applyRadians == null)
		{
			this.AddPropertyRow(panel, label, this.AngFmt(radians));
			return;
		}
		this.AddPropertyRow(panel, label, this.EditBox(this.AngFmt(radians), s =>
		{
			if (TryNum(s, out double deg)) applyRadians(deg * Math.PI / 180);
		}));
	}

	/// <summary>"이름 X/Y/Z" 세 행.</summary>
	private void XyzRows(StackPanel panel, string prefix, XYZ p, Action<XYZ>? apply)
	{
		this.NumRow(panel, prefix + " X", p.X, apply == null ? null : v => apply(new XYZ(v, p.Y, p.Z)));
		this.NumRow(panel, prefix + " Y", p.Y, apply == null ? null : v => apply(new XYZ(p.X, v, p.Z)));
		this.NumRow(panel, prefix + " Z", p.Z, apply == null ? null : v => apply(new XYZ(p.X, p.Y, v)));
	}

	private static bool TryNum(string? s, out double v) =>
		double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.TryParse(s, out v);

	/// <summary>Enter로 적용하는 편집 칸 (포커스를 잃어도 값이 바뀌었으면 적용).</summary>
	private Control EditBox(string text, Action<string> apply)
	{
		if (this._recording != null)
		{
			return new RecordedValue(new RecordedRow { Kind = 1, Text = text, Apply = apply });
		}
		var box = new TextBox
		{
			Text = text,
			FontSize = 13,
			Height = 20,
			MinHeight = 20,
			Padding = new Thickness(4, 0),
			Background = Brushes.Transparent,
			Foreground = CadTheme.PropertyValueFg,
			BorderThickness = new Thickness(0),
			VerticalAlignment = VerticalAlignment.Center,
		};
		bool applied = false;
		void Commit()
		{
			if (applied || box.Text == text) return;
			applied = true;
			apply(box.Text ?? "");
		}
		box.KeyDown += (_, ke) =>
		{
			if (ke.Key == Key.Enter)
			{
				Commit();
				ke.Handled = true;
			}
		};
		box.LostFocus += (_, _) => Commit();
		return box;
	}

	// ===== 같은 종류 여러 개 (기존 CAD: 값이 같으면 그 값, 다르면 *다양함*, 넣으면 모두에) =====

	private sealed class RecordedRow
	{
		public string Section = "";
		public string Label = "";
		public string Text = "";
		/// <summary>0 = 읽기 전용, 1 = 편집 칸, 2 = 고르기, -1 = 여러 개에서 지원하지 않는 칸.</summary>
		public int Kind;
		public IReadOnlyList<string>? Items;
		public Action<string>? Apply;
	}

	/// <summary>기록 중 만든 값 칸 (화면에 넣지 않고 행 정보만 담는다).</summary>
	private sealed class RecordedValue : Control
	{
		public RecordedValue(RecordedRow row) => this.Row = row;

		public RecordedRow Row { get; }
	}

	private List<RecordedRow>? _recording;
	private string _recSection = "";

	private void RecordRow(string label, object value)
	{
		var row = value switch
		{
			RecordedValue rv => rv.Row,
			Control => new RecordedRow { Kind = -1 },
			_ => new RecordedRow { Kind = 0, Text = value?.ToString() ?? "" },
		};
		row.Section = this._recSection;
		row.Label = label;
		this._recording!.Add(row);
	}

	/// <summary>같은 종류 객체 여러 개: 객체마다 행을 기록해 같은 항목끼리 합친다.</summary>
	private void BuildStdMulti(IReadOnlyList<Entity> ents)
	{
		var lists = new List<List<RecordedRow>>();
		foreach (var e in ents)
		{
			this._recording = new List<RecordedRow>();
			this._recSection = "";
			try
			{
				if (!this.BuildStdEntity(e))
				{
					return;
				}
				lists.Add(this._recording);
			}
			finally
			{
				this._recording = null;
			}
		}
		if (lists.Count == 0)
		{
			return;
		}
		static string KeyOf(RecordedRow r, int n) => r.Section + "\u0001" + r.Label + "\u0001" + n;
		Dictionary<string, RecordedRow> Index(List<RecordedRow> rows)
		{
			var d = new Dictionary<string, RecordedRow>();
			var counts = new Dictionary<string, int>();
			foreach (var r in rows)
			{
				string k0 = r.Section + "\u0001" + r.Label;
				int n = counts.TryGetValue(k0, out int c) ? c + 1 : 0;
				counts[k0] = n;
				d[KeyOf(r, n)] = r;
			}
			return d;
		}
		var indexes = lists.Select(Index).ToList();
		var firstCounts = new Dictionary<string, int>();
		string? curSection = null;
		StackPanel? panel = null;
		foreach (var row in lists[0])
		{
			string k0 = row.Section + "\u0001" + row.Label;
			int n = firstCounts.TryGetValue(k0, out int c) ? c + 1 : 0;
			firstCounts[k0] = n;
			string key = KeyOf(row, n);
			var group = indexes.Select(ix => ix.TryGetValue(key, out var r) ? r : null).ToList();
			if (group.Any(r => r == null || r.Kind != row.Kind) || row.Kind < 0)
			{
				continue; // 모든 객체에 같은 종류로 있는 항목만 (기존 CAD와 같음)
			}
			if (row.Section != curSection || panel == null)
			{
				curSection = row.Section;
				panel = this.Section(row.Section);
			}
			bool same = group.All(r => r!.Text == row.Text);
			string text = same ? row.Text : Varies;
			var rows = group.Select(r => r!).ToList();
			void ApplyAll(string v)
			{
				if (this._doc == null) return;
				this._doc.BeginTransaction();
				try
				{
					foreach (var r in rows) r.Apply?.Invoke(v);
				}
				finally
				{
					this._doc.CommitTransaction(row.Label + " 변경");
				}
			}
			switch (row.Kind)
			{
				case 0:
					this.AddPropertyRow(panel, row.Label, text);
					break;
				case 1:
					this.AddPropertyRow(panel, row.Label, this.EditBox(text, v => { if (v != Varies) ApplyAll(v); }));
					break;
				case 2:
					{
						var items = (row.Items ?? Array.Empty<string>()).ToList();
						if (!same) items.Insert(0, Varies);
						this.AddPropertyRow(panel, row.Label, this.CreateSimpleDropdown(items, text, v => { if (v != Varies) ApplyAll(v); }));
						break;
					}
			}
		}
	}

	// ===== 스플라인 (기존 CAD: 데이터 포인트, 기타) =====

	private void BuildSpline(Spline sp)
	{
		var g = this.Section("데이터 포인트");
		bool fit = sp.FitPoints.Count >= 2;
		var pts = fit ? sp.FitPoints : sp.ControlPoints;
		string word = fit ? "맞춤점" : "조정점";
		this.AddPropertyRow(g, word + " 수", pts.Count.ToString(CultureInfo.InvariantCulture));
		if (!ReferenceEquals(this._vertexEntity, sp) || this._vertexIndex >= pts.Count)
		{
			this._vertexEntity = sp;
			this._vertexIndex = 0;
		}
		if (pts.Count > 0)
		{
			var names = Enumerable.Range(1, pts.Count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
			this.AddPropertyRow(g, word, this.CreateSimpleDropdown(names, names[this._vertexIndex], name =>
			{
				this._vertexIndex = int.Parse(name, CultureInfo.InvariantCulture) - 1;
				Avalonia.Threading.Dispatcher.UIThread.Post(() => this.UpdateSelection(this._selected));
			}));
			int i = this._vertexIndex;
			this.XyzRows(g, word, pts[i], v => this.EditSpline(sp, fit, i, v));
		}
		this.AddPropertyRow(g, "노트 매개변수", "코드");
		this.AddPropertyRow(g, "CV 프레임", "숨기기");

		var misc = this.Section("기타");
		this.AddPropertyRow(misc, "방법", fit ? "맞춤" : "CV");
		this.AddPropertyRow(misc, "차수", sp.Degree.ToString(CultureInfo.InvariantCulture));
		this.AddPropertyRow(misc, "닫힘", sp.Flags.HasFlag(SplineFlags.Closed) ? "예" : "No");
		this.AddPropertyRow(misc, "주기적", sp.Flags.HasFlag(SplineFlags.Periodic) ? "예" : "No");
		this.AddPropertyRow(misc, "평면적", "예"); // 2D 도면의 스플라인은 평면
		this.XyzRows(misc, "시작 접선 벡터", sp.StartTangent, null);
		this.XyzRows(misc, "끝 접선 벡터", sp.EndTangent, null);
		this.NumRow(misc, "맞춤 정도", sp.FitTolerance, null);
	}

	/// <summary>스플라인 점 하나 옮기기 (맞춤점이면 조정점을 다시 계산).</summary>
	private void EditSpline(Spline sp, bool fit, int index, XYZ p)
	{
		var oldCtrl = sp.ControlPoints.ToList();
		var oldFit = sp.FitPoints.ToList();
		var oldKnots = sp.Knots.ToList();
		int oldDeg = sp.Degree;
		this.Exec("스플라인 점 변경", () =>
		{
			if (fit)
			{
				sp.FitPoints[index] = p;
				var (ctrl, knots, deg) = Free2DCad.Core.Commands.SplineCommand.Interpolate(sp.FitPoints.Select(f => new XY(f.X, f.Y)).ToList());
				sp.Degree = deg;
				sp.ControlPoints.Clear();
				foreach (var c in ctrl) sp.ControlPoints.Add(new XYZ(c.X, c.Y, 0));
				sp.Knots.Clear();
				foreach (double k in knots) sp.Knots.Add(k);
			}
			else
			{
				sp.ControlPoints[index] = p;
			}
		}, () =>
		{
			sp.ControlPoints.Clear(); sp.ControlPoints.AddRange(oldCtrl);
			sp.FitPoints.Clear(); sp.FitPoints.AddRange(oldFit);
			sp.Knots.Clear(); sp.Knots.AddRange(oldKnots);
			sp.Degree = oldDeg;
		});
	}

	// ===== 구성선·광선 (기존 CAD: 기준점, 두 번째 점, 방향 벡터) =====

	private void BuildInfiniteLine(Func<XYZ> getBase, Action<XYZ> setBase, Func<XYZ> getDir, Action<XYZ> setDir)
	{
		var g = this.Section("기하학");
		XYZ b = getBase();
		XYZ d = getDir();
		double dl = Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
		XYZ u = dl > 1e-12 ? new XYZ(d.X / dl, d.Y / dl, d.Z / dl) : XYZ.AxisX;
		this.XyzRows(g, "기준점", b, v => this.Set("기준점 변경", getBase, setBase, v));
		this.XyzRows(g, "두 번째 점", b + u, v =>
		{
			var nd = v - getBase();
			double l = Math.Sqrt(nd.X * nd.X + nd.Y * nd.Y + nd.Z * nd.Z);
			if (l > 1e-12) this.Set("방향 변경", getDir, setDir, new XYZ(nd.X / l, nd.Y / l, nd.Z / l));
		});
		this.XyzRows(g, "방향 벡터", u, v =>
		{
			double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
			if (l > 1e-12) this.Set("방향 변경", getDir, setDir, new XYZ(v.X / l, v.Y / l, v.Z / l));
		});
	}

	// ===== 다중선 (기존 CAD: 기타 — 스타일, 정렬, 축척) =====

	private void BuildMLine(MLine ml)
	{
		var misc = this.Section("기타");
		this.AddPropertyRow(misc, "Mline 스타일", ml.Style?.Name ?? "STANDARD");
		var just = new[] { "상단", "영점", "하단" };
		string cur = ml.Justification switch { MLineJustification.Zero => "영점", MLineJustification.Bottom => "하단", _ => "상단" };
		this.AddPropertyRow(misc, "Mline 정렬", this.CreateSimpleDropdown(just, cur, name =>
		{
			var j = name == "영점" ? MLineJustification.Zero : name == "하단" ? MLineJustification.Bottom : MLineJustification.Top;
			this.RebuildMLine(ml, ml.ScaleFactor, j, "다중선 정렬 변경");
		}));
		this.NumRow(misc, "Mline 축척", ml.ScaleFactor, v => { if (Math.Abs(v) > 1e-9) this.RebuildMLine(ml, v, ml.Justification, "다중선 축척 변경"); });
	}

	/// <summary>꼭짓점은 그대로 두고 축척·정렬로 요소 선 간격을 다시 계산 (MLINE 명령과 같은 계산).</summary>
	private void RebuildMLine(MLine ml, double scale, MLineJustification just, string name)
	{
		if (ml.Style == null || ml.Vertices.Count < 2)
		{
			return;
		}
		var pts = ml.Vertices.Select(v => new XY(v.Position.X, v.Position.Y)).ToList();
		bool closed = ml.Flags.HasFlag(MLineFlags.Closed);
		var built = Free2DCad.Core.Commands.MLineCommand.Build(pts, closed, ml.Style, scale, just);
		if (built == null)
		{
			return;
		}
		var oldV = ml.Vertices.ToList();
		var newV = built.Vertices.ToList();
		double oldS = ml.ScaleFactor;
		var oldJ = ml.Justification;
		this.Exec(name, () =>
		{
			ml.Vertices.Clear(); ml.Vertices.AddRange(newV);
			ml.ScaleFactor = scale; ml.Justification = just;
		}, () =>
		{
			ml.Vertices.Clear(); ml.Vertices.AddRange(oldV);
			ml.ScaleFactor = oldS; ml.Justification = oldJ;
		});
	}

	// ===== 지시선 LEADER (기존 CAD: 기하학 꼭짓점, 기타, 선 화살촉, 텍스트, 맞춤 — 값은 DSTYLE 재정의) =====

	private void BuildLeader(Leader leader)
	{
		var doc = this._doc!;
		var list = new Entity[] { leader };
		var g = this.Section("기하학");
		int n = leader.Vertices.Count;
		if (!ReferenceEquals(this._vertexEntity, leader) || this._vertexIndex >= n)
		{
			this._vertexEntity = leader;
			this._vertexIndex = 0;
		}
		if (n > 0)
		{
			var names = Enumerable.Range(1, n).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
			this.AddPropertyRow(g, "꼭짓점", this.CreateSimpleDropdown(names, names[this._vertexIndex], name =>
			{
				this._vertexIndex = int.Parse(name, CultureInfo.InvariantCulture) - 1;
				Avalonia.Threading.Dispatcher.UIThread.Post(() => this.UpdateSelection(this._selected));
			}));
			int i = this._vertexIndex;
			this.XyzRows(g, "꼭짓점", leader.Vertices[i], v => this.Set("꼭짓점 변경", () => leader.Vertices[i], x => leader.Vertices[i] = x, v));
		}

		var misc = this.Section("기타");
		var styleNames = doc.Document.DimensionStyles.Select(st => st.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
		string curStyle = leader.Style?.Name ?? "Standard";
		this.AddPropertyRow(misc, "치수 스타일", this.CreateSimpleDropdown(styleNames, curStyle, name =>
		{
			if (doc.Document.DimensionStyles.TryGetValue(name, out var st)) this.Set("치수 스타일 변경", () => leader.Style, x => leader.Style = x, st);
		}));
		// 기존 CAD와 같은 이름·순서
		var types = new[] { "화살촉 없는 선", "화살촉 없는 스플라인", "화살촉 있는 선", "화살촉 있는 스플라인" };
		bool spline = leader.PathType == LeaderPathType.Spline;
		string curType = types[(leader.ArrowHeadEnabled ? 2 : 0) + (spline ? 1 : 0)];
		this.AddPropertyRow(misc, "유형", this.CreateSimpleDropdown(types, curType, name =>
		{
			int k = Array.IndexOf(types, name);
			if (k < 0) return;
			var oldP = leader.PathType;
			bool oldA = leader.ArrowHeadEnabled;
			this.Exec("지시선 유형 변경", () => { leader.PathType = k % 2 == 1 ? LeaderPathType.Spline : LeaderPathType.StraightLineSegments; leader.ArrowHeadEnabled = k >= 2; },
				() => { leader.PathType = oldP; leader.ArrowHeadEnabled = oldA; });
		}));
		this.AddPropertyRow(misc, "주석", "No");

		var lines = this.Section("선 화살촉");
		var st0 = leader.GetActiveDimensionStyle();
		string arrowName = Free2DCad.Core.Styles.DimensionOverrides.ArrowName(st0?.LeaderArrow);
		var arrows = Free2DCad.Core.Styles.DimensionOverrides.Arrows.Select(a => a.Name).ToList();
		if (!arrows.Contains(arrowName)) arrows.Insert(0, arrowName);
		this.AddPropertyRow(lines, "화살촉", this.CreateSimpleDropdown(arrows, arrowName, name =>
		{
			var a = Free2DCad.Core.Styles.DimensionOverrides.Arrows.FirstOrDefault(x => x.Name == name);
			if (a.Name == null) return;
			var block = Free2DCad.Core.Styles.DimensionOverrides.GetOrCreateArrow(doc.Document, a.Block);
			doc.Execute(Free2DCad.Core.Styles.DimensionOverrides.SetAction(list, 341, block?.Handle ?? 0UL, "화살촉 변경"));
		}));
		this.DimNumberRow(lines, list, "화살촉 크기", Free2DCad.Core.Styles.DimensionOverrides.ArrowSize, v => v >= 0);
		this.DimWeightRow(lines, list, "치수선 선 두께", Free2DCad.Core.Styles.DimensionOverrides.DimLineWeight);
		this.DimColorRow(lines, list, "치수선 색상", Free2DCad.Core.Styles.DimensionOverrides.DimLineColor);

		var text = this.Section("텍스트");
		this.DimNumberRow(text, list, "텍스트 간격띄우기", Free2DCad.Core.Styles.DimensionOverrides.TextGap, _ => true);
		this.DimChoiceRow(text, list, "텍스트 위치 수직", Free2DCad.Core.Styles.DimensionOverrides.TextVertical,
			Enumerated(Free2DCad.Core.Styles.DimensionOverrides.TextVertical, "중앙에 배치됨", "위", "밖", "JIS", "아래"));
	}

	// ===== 다중 지시선 MLEADER (기존 CAD: 기타, 지시선, 텍스트) =====

	private void BuildMultiLeader(MultiLeader ml)
	{
		var data = ml.ContextData;
		var misc = this.Section("기타");
		this.NumRow(misc, "전체 축척", ml.ScaleFactor, v =>
		{
			if (v <= 0) return;
			this.Set("전체 축척 변경", () => (ml.ScaleFactor, ml.PropertyOverrideFlags), x => { ml.ScaleFactor = x.Item1; ml.PropertyOverrideFlags = x.Item2; },
				(v, ml.PropertyOverrideFlags | MultiLeaderPropertyOverrideFlags.ScaleFactor));
		});
		// 기존 CAD처럼 모두 고를 수 있는 목록 (지시선 색상·선종류·선두께·화살촉은 객체 재정의 플래그와 함께 저장 — 기존 CAD 파일과 같음)
		var cad = this._doc!.Document;
		var mlStyles = cad.MLeaderStyles.ToList();
		this.AddPropertyRow(misc, "다중지시선 스타일", this.CreateSimpleDropdown(mlStyles.Select(x => x.Name).ToList(), ml.Style?.Name ?? "Standard", name =>
		{
			var st = mlStyles.FirstOrDefault(x => x.Name == name);
			if (st != null) this.Set("다중지시선 스타일 변경", () => ml.Style, x => ml.Style = x, st);
		}));
		this.AddPropertyRow(misc, "주석", this.CreateSimpleDropdown(new[] { "Yes", "No" }, ml.EnableAnnotationScale ? "Yes" : "No",
			v => this.Set("주석 변경", () => ml.EnableAnnotationScale, x => ml.EnableAnnotationScale = x, v == "Yes")));

		var ld = this.Section("지시선");
		// 재정의 플래그를 같이 켜는 값 바꾸기 (되돌리면 값과 플래그 모두 원래대로)
		void SetOv<T>(string name, Func<T> get, Action<T> set, T value, MultiLeaderPropertyOverrideFlags flag) =>
			this.Set(name, () => (get(), ml.PropertyOverrideFlags), x => { set(x.Item1); ml.PropertyOverrideFlags = x.Item2; }, (value, ml.PropertyOverrideFlags | flag));

		string pathName = ml.PathType == MultiLeaderPathType.Spline ? "스플라인" : ml.PathType == MultiLeaderPathType.Invisible ? "없음" : "직선";
		this.AddPropertyRow(ld, "지시선 유형", this.CreateSimpleDropdown(new[] { "직선", "스플라인", "없음" }, pathName, v =>
			SetOv("지시선 유형 변경", () => ml.PathType, x => ml.PathType = x,
				v == "스플라인" ? MultiLeaderPathType.Spline : v == "없음" ? MultiLeaderPathType.Invisible : MultiLeaderPathType.StraightLineSegments,
				MultiLeaderPropertyOverrideFlags.PathType)));

		// 지시선 색상: BYLAYER·BYBLOCK·1~7 + 색상 선택...
		var colorNames = ColorOptions.Select(o => o.Name).ToList();
		string curColor = ml.LineColor.IsByBlock ? "BYBLOCK" : ml.LineColor.IsByLayer ? "BYLAYER"
			: ColorOptions.FirstOrDefault(o => !ml.LineColor.IsTrueColor && o.Color.Index == ml.LineColor.Index)?.Name
			?? (ml.LineColor.IsTrueColor ? $"{ml.LineColor.R},{ml.LineColor.G},{ml.LineColor.B}" : $"색상 {ml.LineColor.Index}");
		if (!colorNames.Contains(curColor)) colorNames.Insert(0, curColor);
		colorNames.Add("색상 선택...");
		this.AddPropertyRow(ld, "지시선 색상", this.CreateSimpleDropdown(colorNames, curColor, async v =>
		{
			ACadSharp.Color c;
			if (v == "색상 선택...")
			{
				if (TopLevel.GetTopLevel(this) is not Window owner) return;
				var picked = await new ColorPickerDialog(ml.LineColor).ShowDialog<ACadSharp.Color?>(owner);
				if (picked is not { } pc) return;
				c = pc;
			}
			else if (ColorOptions.FirstOrDefault(o => o.Name == v) is { } opt) c = opt.Color;
			else return;
			SetOv("지시선 색상 변경", () => ml.LineColor, x => ml.LineColor = x, c, MultiLeaderPropertyOverrideFlags.LineColor);
		}));

		// 지시선 선종류: ByLayer·ByBlock 다음 도면의 선종류 (이름순)
		var lts = cad.LineTypes.OrderBy(l => l.Name.Equals(LineType.ByLayerName, StringComparison.OrdinalIgnoreCase) ? 0 : l.Name.Equals(LineType.ByBlockName, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
			.ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
		this.AddPropertyRow(ld, "지시선 선종류", this.CreateSimpleDropdown(lts.Select(l => l.Name).ToList(), ml.LeaderLineType?.Name ?? LineType.ByBlockName, v =>
		{
			var lt = lts.FirstOrDefault(l => l.Name == v);
			if (lt != null) SetOv("지시선 선종류 변경", () => ml.LeaderLineType, x => ml.LeaderLineType = x, lt, MultiLeaderPropertyOverrideFlags.LeaderLineType);
		}));

		var weights = Weights.ToList();
		this.AddPropertyRow(ld, "지시선 선두께", this.CreateSimpleDropdown(weights.Select(WeightName).ToList(), WeightName(ml.LeaderLineWeight), v =>
		{
			var w = weights.FirstOrDefault(x => WeightName(x) == v);
			SetOv("지시선 선두께 변경", () => ml.LeaderLineWeight, x => ml.LeaderLineWeight = x, w, MultiLeaderPropertyOverrideFlags.LeaderLineWeight);
		}));

		var arrows = Free2DCad.Core.Styles.DimensionOverrides.Arrows;
		this.AddPropertyRow(ld, "화살촉 머리", this.CreateSimpleDropdown(arrows.Select(a => a.Name).ToList(), Free2DCad.Core.Styles.DimensionOverrides.ArrowName(ml.Arrowhead), v =>
		{
			var a = arrows.FirstOrDefault(x => x.Name == v);
			if (a.Name == null) return;
			// 화살촉 블록이 없으면 만든다 (기존 CAD도 _ArchTick 같은 블록을 도면에 넣음). 닫힌 채움 = 블록 없음
			var block = Free2DCad.Core.Styles.DimensionOverrides.GetOrCreateArrow(cad, a.Block);
			SetOv("화살촉 머리 변경", () => ml.Arrowhead, x => ml.Arrowhead = x, block, MultiLeaderPropertyOverrideFlags.Arrowhead);
		}));
		this.NumRow(ld, "화살촉 크기", ml.ArrowheadSize, v =>
		{
			if (v < 0) return;
			double oldC = data?.ArrowheadSize ?? 0;
			this.Set("화살촉 크기 변경", () => (ml.ArrowheadSize, ml.PropertyOverrideFlags), x => { ml.ArrowheadSize = x.Item1; ml.PropertyOverrideFlags = x.Item2; if (data != null) data.ArrowheadSize = x.Item1; },
				(v, ml.PropertyOverrideFlags | MultiLeaderPropertyOverrideFlags.ArrowheadSize));
		});
		this.AddPropertyRow(ld, "수평 착륙", this.CreateSimpleDropdown(new[] { "예", "No" }, ml.EnableLanding ? "예" : "No", v =>
		{
			bool on = v == "예";
			this.Set("수평 착륙 변경", () => (ml.EnableLanding, ml.EnableDogleg, ml.PropertyOverrideFlags),
				x => { ml.EnableLanding = x.Item1; ml.EnableDogleg = x.Item2; ml.PropertyOverrideFlags = x.Item3; },
				(on, on, ml.PropertyOverrideFlags | MultiLeaderPropertyOverrideFlags.EnableLanding | MultiLeaderPropertyOverrideFlags.EnableDogleg));
		}));
		double landing = data?.LeaderRoots.Count > 0 ? data.LeaderRoots[0].LandingDistance : ml.LandingDistance;
		this.NumRow(ld, "랜딩 거리", landing, v =>
		{
			if (v < 0 || data == null) return;
			var olds = data.LeaderRoots.Select(r => r.LandingDistance).ToList();
			double oldD = ml.LandingDistance;
			var oldF = ml.PropertyOverrideFlags;
			this.Exec("랜딩 거리 변경", () =>
			{
				foreach (var r in data.LeaderRoots) r.LandingDistance = v;
				ml.LandingDistance = v;
				ml.PropertyOverrideFlags |= MultiLeaderPropertyOverrideFlags.LandingDistance;
			}, () =>
			{
				for (int i = 0; i < olds.Count; i++) data.LeaderRoots[i].LandingDistance = olds[i];
				ml.LandingDistance = oldD;
				ml.PropertyOverrideFlags = oldF;
			});
		});

		if (data == null)
		{
			return;
		}
		var t = this.Section("텍스트");
		this.AddEditableTextRow(t, "내용", data.TextLabel ?? "", nv => this.Set("내용 변경", () => data.TextLabel, x => data.TextLabel = x, nv));
		this.AddPropertyRow(t, "스타일", this.CreateTextStyleDropdown(data.TextStyle, st =>
			this.Set("문자 스타일 변경", () => (data.TextStyle, ml.TextStyle, ml.PropertyOverrideFlags),
				x => { data.TextStyle = x.Item1; ml.TextStyle = x.Item2; ml.PropertyOverrideFlags = x.Item3; Free2DCad.Core.Commands.MLeaderBuilder.SetText(ml, data.TextLabel ?? string.Empty); },
				(st, st, ml.PropertyOverrideFlags | MultiLeaderPropertyOverrideFlags.TextStyle))));
		var aligns = new[] { "왼쪽", "중심", "오른쪽" };
		string curA = data.TextAlignment switch { TextAlignmentType.Center => "중심", TextAlignmentType.Right => "오른쪽", _ => "왼쪽" };
		this.AddPropertyRow(t, "정렬", this.CreateSimpleDropdown(aligns, curA, name =>
		{
			var a = name == "중심" ? TextAlignmentType.Center : name == "오른쪽" ? TextAlignmentType.Right : TextAlignmentType.Left;
			this.Set("정렬 변경", () => (data.TextAlignment, ml.TextAlignment), x => { data.TextAlignment = x.Item1; ml.TextAlignment = x.Item2; }, (a, a));
		}));
		string dirName = data.FlowDirection == FlowDirectionType.Vertical ? "수직" : data.FlowDirection is FlowDirectionType.ByStyle or FlowDirectionType.ByStyleBadDoc ? "스타일별" : "수평";
		this.AddPropertyRow(t, "방향", this.CreateSimpleDropdown(new[] { "수평", "수직", "스타일별" }, dirName, v =>
			this.Set("문자 방향 변경", () => data.FlowDirection, x => data.FlowDirection = x,
				v == "수직" ? FlowDirectionType.Vertical : v == "스타일별" ? FlowDirectionType.ByStyle : FlowDirectionType.Horizontal)));
		this.NumRow(t, "너비(폭)", data.BoundaryWidth, v => { if (v >= 0) this.Set("문자 폭 변경", () => data.BoundaryWidth, x => data.BoundaryWidth = x, v); });
		this.NumRow(t, "높이", data.TextHeight, v =>
		{
			if (v <= 0) return;
			this.Set("문자 높이 변경", () => (data.TextHeight, ml.PropertyOverrideFlags), x => { data.TextHeight = x.Item1; ml.PropertyOverrideFlags = x.Item2; },
				(v, ml.PropertyOverrideFlags | MultiLeaderPropertyOverrideFlags.TextHeight));
		});
		this.AngRow(t, "회전", data.TextRotation, null);
		double lsf = data.LineSpacingFactor > 0 ? data.LineSpacingFactor : 1;
		this.NumRow(t, "줄 간격 인자", lsf, v => { if (v > 0) this.Set("줄 간격 변경", () => data.LineSpacingFactor, x => data.LineSpacingFactor = x, v); });
		this.NumRow(t, "줄 간격 거리", lsf * data.TextHeight * 5.0 / 3.0, null);
		this.AddPropertyRow(t, "줄 간격 스타일", "최소");
		this.AddPropertyRow(t, "배경 마스크", data.BackgroundFillEnabled ? "예" : "No");
		this.AddPropertyRow(t, "첨부 유형", "수평");
		this.AddPropertyRow(t, "착륙 간격", Fmt(data.LandingGap));
		this.AddPropertyRow(t, "텍스트 프레임", ml.TextFrame ? "예" : "No");
	}

	// ===== 표 (기존 CAD: 테이블, 기하학, 테이블분할) =====

	private void BuildTable(TableEntity table)
	{
		var doc = this._doc!;
		void Change(string name, Action<TableEntity> change)
		{
			var before = new Free2DCad.Core.Tables.TableEditor.Snapshot(table);
			change(table);
			Free2DCad.Core.Tables.TableEditor.Regenerate(doc.Document, table, Free2DCad.Core.Tables.TableEditor.GuessTextHeight(table));
			var after = new Free2DCad.Core.Tables.TableEditor.Snapshot(table);
			before.Restore(table);
			doc.Execute(new EntityEditAction(name, table, () => after.Restore(table), () => before.Restore(table)));
		}
		double width = table.Columns.Sum(c => c.Width), height = table.Rows.Sum(r => r.Height);
		var t = this.Section("테이블");
		this.AddPropertyRow(t, "테이블 스타일", table.Style?.Name ?? "Standard");
		this.AddPropertyRow(t, "행", table.Rows.Count.ToString(CultureInfo.InvariantCulture));
		this.AddPropertyRow(t, "열", table.Columns.Count.ToString(CultureInfo.InvariantCulture));
		this.AddPropertyRow(t, "방향", "아래로");
		this.NumRow(t, "테이블 높이", height, v =>
		{
			if (v <= 0 || height <= 0) return;
			Change("표 높이 변경", tb => { double k = v / height; foreach (var r in tb.Rows) r.Height *= k; });
		});
		this.NumRow(t, "테이블 너비(폭)", width, v =>
		{
			if (v <= 0 || width <= 0) return;
			Change("표 너비 변경", tb => { double k = v / width; foreach (var c in tb.Columns) c.Width *= k; });
		});
		this.NumRow(t, "수평 여백", Free2DCad.Core.Tables.TableEditor.DefaultMargin, null);
		this.NumRow(t, "수직 여백", Free2DCad.Core.Tables.TableEditor.DefaultMargin, null);

		var g = this.Section("기하학");
		this.XyzRows(g, "위치", table.InsertPoint, p => this.Set("위치 변경", () => table.InsertPoint, x => table.InsertPoint = x, p));

		var br = this.Section("테이블분할");
		this.AddPropertyRow(br, "활성화", "No");

		// F2D: 행·열 추가/삭제, 열 너비·행 높이(모두), CSV (기존 CAD에서는 표 편집 도구)
		var edit = this.Section("표 편집");
		this.BuildTableProperties(edit, table);
	}

	// ===== 솔리드 (기존 CAD: 꼭짓점, 꼭짓점 X/Y, 고도) =====

	private void BuildSolid(Solid so)
	{
		var g = this.Section("기하학");
		if (!ReferenceEquals(this._vertexEntity, so) || this._vertexIndex > 3)
		{
			this._vertexEntity = so;
			this._vertexIndex = 0;
		}
		var names = new[] { "1", "2", "3", "4" };
		this.AddPropertyRow(g, "꼭짓점", this.CreateSimpleDropdown(names, names[this._vertexIndex], name =>
		{
			this._vertexIndex = int.Parse(name, CultureInfo.InvariantCulture) - 1;
			Avalonia.Threading.Dispatcher.UIThread.Post(() => this.UpdateSelection(this._selected));
		}));
		int i = this._vertexIndex;
		Func<XYZ> get = i switch { 0 => () => so.FirstCorner, 1 => () => so.SecondCorner, 2 => () => so.ThirdCorner, _ => () => so.FourthCorner };
		Action<XYZ> set = i switch { 0 => v => so.FirstCorner = v, 1 => v => so.SecondCorner = v, 2 => v => so.ThirdCorner = v, _ => v => so.FourthCorner = v };
		var c = get();
		this.NumRow(g, "꼭짓점 X", c.X, v => this.Set("꼭짓점 변경", get, set, new XYZ(v, get().Y, get().Z)));
		this.NumRow(g, "꼭짓점 Y", c.Y, v => this.Set("꼭짓점 변경", get, set, new XYZ(get().X, v, get().Z)));
		this.NumRow(g, "고도", so.FirstCorner.Z, v =>
		{
			var old = (so.FirstCorner, so.SecondCorner, so.ThirdCorner, so.FourthCorner);
			XYZ Z(XYZ p) => new XYZ(p.X, p.Y, v);
			this.Exec("고도 변경", () => { so.FirstCorner = Z(old.Item1); so.SecondCorner = Z(old.Item2); so.ThirdCorner = Z(old.Item3); so.FourthCorner = Z(old.Item4); },
				() => { so.FirstCorner = old.Item1; so.SecondCorner = old.Item2; so.ThirdCorner = old.Item3; so.FourthCorner = old.Item4; });
		});
	}

	// ===== 속성 정의 ATTDEF (기존 CAD: 텍스트, 기타 — 모드) =====

	private void BuildAttDef(AttributeDefinition ad)
	{
		var t = this.Section("텍스트");
		this.AddEditableTextRow(t, "태그", ad.Tag ?? "", nv => { if (!string.IsNullOrWhiteSpace(nv)) this.Set("태그 변경", () => ad.Tag, x => ad.Tag = x, nv.Trim().ToUpperInvariant()); });
		this.AddPropertyRow(t, "주석", "No");
		this.AddEditableTextRow(t, "프롬프트", ad.Prompt ?? "", nv => this.Set("프롬프트 변경", () => ad.Prompt, x => ad.Prompt = x, nv));
		this.AddEditableTextRow(t, "값", ad.Value ?? "", nv => this.Set("값 변경", () => ad.Value, x => ad.Value = x, nv));
		this.AddPropertyRow(t, "스타일", this.CreateTextStyleDropdown(ad.Style, st => this.Set("문자 스타일 변경", () => ad.Style, x => ad.Style = x, st)));
		this.TextJustifyRow(t, ad);
		this.NumRow(t, "높이", ad.Height, v => { if (v > 1e-9) this.Set("문자 높이 변경", () => ad.Height, x => ad.Height = x, v); });
		this.AngRow(t, "회전", ad.Rotation, r => this.Set("문자 회전 변경", () => ad.Rotation, x => ad.Rotation = x, r));
		this.NumRow(t, "너비(폭) 인자", ad.WidthFactor, v => { if (v > 0) this.Set("폭 비율 변경", () => ad.WidthFactor, x => ad.WidthFactor = x, v); });
		this.AngRow(t, "경사각", ad.ObliqueAngle, r => this.Set("기울기 변경", () => ad.ObliqueAngle, x => ad.ObliqueAngle = x, r));
		bool left = ad.HorizontalAlignment == TextHorizontalAlignment.Left && ad.VerticalAlignment == TextVerticalAlignmentType.Baseline;
		this.XyzRows(t, "텍스트 정렬", left ? XYZ.Zero : ad.AlignmentPoint, left ? null : p => this.Set("정렬점 변경", () => ad.AlignmentPoint, x => ad.AlignmentPoint = x, p));

		var g = this.Section("기하학");
		this.XyzRows(g, "위치", ad.InsertPoint, p => this.Set("위치 변경", () => ad.InsertPoint, x => ad.InsertPoint = x, p));

		var misc = this.Section("기타");
		void Mirror(string label, TextMirrorFlag flag)
		{
			bool on = ad.Mirror.HasFlag(flag);
			this.AddPropertyRow(misc, label, this.CreateSimpleDropdown(new[] { "예", "No" }, on ? "예" : "No", name =>
				this.Set(label + " 변경", () => ad.Mirror, x => ad.Mirror = x, name == "예" ? ad.Mirror | flag : ad.Mirror & ~flag)));
		}
		Mirror("역방향", TextMirrorFlag.Backward);
		Mirror("뒤집기", TextMirrorFlag.UpsideDown);
		void Mode(string label, AttributeFlags flag)
		{
			bool on = ad.Flags.HasFlag(flag);
			this.AddPropertyRow(misc, label, this.CreateSimpleDropdown(new[] { "예", "No" }, on ? "예" : "No", name =>
				this.Set(label + " 변경", () => ad.Flags, x => ad.Flags = x, name == "예" ? ad.Flags | flag : ad.Flags & ~flag)));
		}
		Mode("보이지 않음", AttributeFlags.Hidden);
		Mode("상수", AttributeFlags.Constant);
		Mode("검증", AttributeFlags.Verify);
		Mode("사전 설정", AttributeFlags.Preset);
		this.AddPropertyRow(misc, "위치 잠금", this.CreateSimpleDropdown(new[] { "예", "No" }, ad.IsLocked ? "예" : "No", name =>
			this.Set("위치 잠금 변경", () => ad.IsLocked, x => ad.IsLocked = x, name == "예")));
	}

	/// <summary>단일 행 문자 정렬 고르기 (기준점은 그대로).</summary>
	private void TextJustifyRow(StackPanel t, TextEntity text)
	{
		var cur = TextJustifications.FirstOrDefault(j => j.H == text.HorizontalAlignment && (j.V == text.VerticalAlignment
			|| (j.V == TextVerticalAlignmentType.Baseline && text.HorizontalAlignment is TextHorizontalAlignment.Aligned or TextHorizontalAlignment.Middle or TextHorizontalAlignment.Fit)));
		string curName = cur.Name ?? TextJustifyName(text);
		this.AddPropertyRow(t, "정렬", this.CreateSimpleDropdown(TextJustifications.Select(j => j.Name).ToList(), curName, name =>
		{
			var j = TextJustifications.First(x => x.Name == name);
			bool wasLeft = text.HorizontalAlignment == TextHorizontalAlignment.Left && text.VerticalAlignment == TextVerticalAlignmentType.Baseline;
			var anchor = wasLeft ? text.InsertPoint : text.AlignmentPoint;
			var old = (text.HorizontalAlignment, text.VerticalAlignment, text.InsertPoint, text.AlignmentPoint);
			this.Exec("자리맞추기 변경", () =>
			{
				text.HorizontalAlignment = j.H;
				text.VerticalAlignment = j.V;
				text.InsertPoint = anchor;
				text.AlignmentPoint = anchor;
			}, () =>
			{
				text.HorizontalAlignment = old.Item1;
				text.VerticalAlignment = old.Item2;
				text.InsertPoint = old.Item3;
				text.AlignmentPoint = old.Item4;
			});
		}));
	}

	// ===== 와이프아웃 (기존 CAD: 이미지 조정, 기하학, 기타) =====

	private void BuildWipeout(Wipeout wo)
	{
		var adj = this.Section("이미지 조정");
		this.AddPropertyRow(adj, "밝기", wo.Brightness.ToString(CultureInfo.InvariantCulture));
		this.AddPropertyRow(adj, "대비", wo.Contrast.ToString(CultureInfo.InvariantCulture));
		this.AddPropertyRow(adj, "페이드", wo.Fade.ToString(CultureInfo.InvariantCulture));

		var g = this.Section("기하학");
		this.XyzRows(g, "위치", wo.InsertPoint, p => this.Set("위치 변경", () => wo.InsertPoint, x => wo.InsertPoint = x, p));
		var u = wo.UVector;
		var vv = wo.VVector;
		double ul = Math.Sqrt(u.X * u.X + u.Y * u.Y), vl = Math.Sqrt(vv.X * vv.X + vv.Y * vv.Y);
		double w = ul * wo.Size.X, h = vl * wo.Size.Y;
		this.AngRow(g, "회전", Math.Atan2(u.Y, u.X), r =>
		{
			double cur = Math.Atan2(u.Y, u.X), d = r - cur, cs = Math.Cos(d), sn = Math.Sin(d);
			XYZ Rot(XYZ a) => new XYZ(a.X * cs - a.Y * sn, a.X * sn + a.Y * cs, a.Z);
			this.Set("회전 변경", () => (wo.UVector, wo.VVector), x => { wo.UVector = x.Item1; wo.VVector = x.Item2; }, (Rot(u), Rot(vv)));
		});
		this.NumRow(g, "너비(폭)", w, v => { if (v > 0 && w > 0) this.Set("너비 변경", () => wo.UVector, x => wo.UVector = x, u * (v / w)); });
		this.NumRow(g, "높이", h, v => { if (v > 0 && h > 0) this.Set("높이 변경", () => wo.VVector, x => wo.VVector = x, vv * (v / h)); });
		this.NumRow(g, "축척", 1, null);

		var misc = this.Section("기타");
		void Flag(string label, ImageDisplayFlags flag)
		{
			bool on = wo.Flags.HasFlag(flag);
			this.AddPropertyRow(misc, label, this.CreateSimpleDropdown(new[] { "예", "No" }, on ? "예" : "No", name =>
				this.Set(label + " 변경", () => wo.Flags, x => wo.Flags = x, name == "예" ? wo.Flags | flag : wo.Flags & ~flag)));
		}
		Flag("이미지 표시", ImageDisplayFlags.ShowImage);
		this.AddPropertyRow(misc, "클리핑 표시", this.CreateSimpleDropdown(new[] { "예", "No" }, wo.ClippingState ? "예" : "No", name =>
			this.Set("클리핑 표시 변경", () => wo.ClippingState, x => wo.ClippingState = x, name == "예")));
		Flag("투명도", ImageDisplayFlags.TransparencyIsOn);
	}
}

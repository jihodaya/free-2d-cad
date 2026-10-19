using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Editing;

/// <summary>그립 종류: 점 하나만 옮김(Stretch) 또는 객체 전체 이동(Move), 원 반지름 조정(Radius).</summary>
public enum GripKind
{
	Stretch,
	Move,
	Radius,
}

/// <summary>객체의 편집점 (파란 네모). Index는 객체 안에서의 번호.</summary>
public readonly record struct Grip(double X, double Y, GripKind Kind, int Index);

/// <summary>
/// 기존 CAD 그립 편집: 객체를 선택하면 나오는 파란 점을 끌어 끝점·꼭짓점·중심·치수 위치 등을 바꾼다.
/// </summary>
public static class GripEditor
{
	/// <summary>객체의 그립 목록. 지원하지 않는 객체는 빈 목록.</summary>
	public static IReadOnlyList<Grip> GetGrips(Entity e)
	{
		var g = new List<Grip>();
		switch (e)
		{
			case Line l:
				g.Add(new Grip(l.StartPoint.X, l.StartPoint.Y, GripKind.Stretch, 0));
				g.Add(new Grip((l.StartPoint.X + l.EndPoint.X) / 2, (l.StartPoint.Y + l.EndPoint.Y) / 2, GripKind.Move, 1));
				g.Add(new Grip(l.EndPoint.X, l.EndPoint.Y, GripKind.Stretch, 2));
				break;
			case Arc a:
			{
				g.Add(new Grip(a.Center.X, a.Center.Y, GripKind.Move, 0));
				var (s, m, en) = ArcPoints(a);
				g.Add(new Grip(s.X, s.Y, GripKind.Stretch, 1));
				g.Add(new Grip(m.X, m.Y, GripKind.Stretch, 2));
				g.Add(new Grip(en.X, en.Y, GripKind.Stretch, 3));
				break;
			}
			case Circle c:
				g.Add(new Grip(c.Center.X, c.Center.Y, GripKind.Move, 0));
				g.Add(new Grip(c.Center.X + c.Radius, c.Center.Y, GripKind.Radius, 1));
				g.Add(new Grip(c.Center.X, c.Center.Y + c.Radius, GripKind.Radius, 2));
				g.Add(new Grip(c.Center.X - c.Radius, c.Center.Y, GripKind.Radius, 3));
				g.Add(new Grip(c.Center.X, c.Center.Y - c.Radius, GripKind.Radius, 4));
				break;
			case LwPolyline pl:
			{
				var v = pl.Vertices;
				for (int i = 0; i < v.Count; i++)
				{
					g.Add(new Grip(v[i].Location.X, v[i].Location.Y, GripKind.Stretch, i));
				}
				// 선분 중간점: 끌면 그 선분의 두 꼭짓점을 함께 옮김 (번호는 1000 + 선분 번호)
				int segs = pl.IsClosed ? v.Count : v.Count - 1;
				for (int i = 0; i < segs; i++)
				{
					var p1 = v[i].Location;
					var p2 = v[(i + 1) % v.Count].Location;
					if (Math.Abs(v[i].Bulge) < 1e-12)
					{
						g.Add(new Grip((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2, GripKind.Move, 1000 + i));
					}
				}
				break;
			}
			case DimensionAligned d: // DimensionLinear 포함
				g.Add(new Grip(d.FirstPoint.X, d.FirstPoint.Y, GripKind.Stretch, 0));
				g.Add(new Grip(d.SecondPoint.X, d.SecondPoint.Y, GripKind.Stretch, 1));
				g.Add(new Grip(d.DefinitionPoint.X, d.DefinitionPoint.Y, GripKind.Stretch, 2));
				g.Add(new Grip(d.TextMiddlePoint.X, d.TextMiddlePoint.Y, GripKind.Stretch, 3));
				break;
			case DimensionRadius r:
				g.Add(new Grip(r.DefinitionPoint.X, r.DefinitionPoint.Y, GripKind.Stretch, 0));
				g.Add(new Grip(r.TextMiddlePoint.X, r.TextMiddlePoint.Y, GripKind.Stretch, 3));
				break;
			case DimensionDiameter dd:
				g.Add(new Grip(dd.TextMiddlePoint.X, dd.TextMiddlePoint.Y, GripKind.Stretch, 3));
				break;
			case MultiLeader ml when ml.ContextData.LeaderRoots.Count > 0:
			{
				var root = ml.ContextData.LeaderRoots[0];
				if (root.Lines.Count > 0)
				{
					var pts = root.Lines[0].Points;
					for (int i = 0; i < pts.Count; i++) g.Add(new Grip(pts[i].X, pts[i].Y, GripKind.Stretch, i));
				}
				g.Add(new Grip(root.ConnectionPoint.X, root.ConnectionPoint.Y, GripKind.Stretch, 100));
				break;
			}
			case DimensionLargeRadial lr:
				g.Add(new Grip(lr.OverrideCenter.X, lr.OverrideCenter.Y, GripKind.Stretch, 0));
				g.Add(new Grip(lr.JogPoint.X, lr.JogPoint.Y, GripKind.Stretch, 1));
				g.Add(new Grip(lr.TextMiddlePoint.X, lr.TextMiddlePoint.Y, GripKind.Stretch, 3));
				break;
			case TextEntity t: // 속성 정의/속성 포함
				g.Add(new Grip(t.InsertPoint.X, t.InsertPoint.Y, GripKind.Move, 0));
				break;
			case MText mt:
				g.Add(new Grip(mt.InsertPoint.X, mt.InsertPoint.Y, GripKind.Move, 0));
				break;
			case TableEntity tb when TableFrame(tb) != null:
				AddTableGrips(tb, g);
				break;
			case Insert ins:
				g.Add(new Grip(ins.InsertPoint.X, ins.InsertPoint.Y, GripKind.Move, 0));
				break;
			case Ellipse el:
			{
				g.Add(new Grip(el.Center.X, el.Center.Y, GripKind.Move, 0));
				// 장축 양 끝(1,2), 단축 양 끝(3,4): 끌면 축 길이를 바꾼다
				var mj = el.MajorAxisEndPoint;
				double minorLen = Math.Sqrt(mj.X * mj.X + mj.Y * mj.Y) * el.RadiusRatio;
				double mlen = Math.Sqrt(mj.X * mj.X + mj.Y * mj.Y);
				double nx = mlen > 1e-12 ? -mj.Y / mlen * minorLen : 0, ny = mlen > 1e-12 ? mj.X / mlen * minorLen : 0;
				g.Add(new Grip(el.Center.X + mj.X, el.Center.Y + mj.Y, GripKind.Radius, 1));
				g.Add(new Grip(el.Center.X - mj.X, el.Center.Y - mj.Y, GripKind.Radius, 2));
				g.Add(new Grip(el.Center.X + nx, el.Center.Y + ny, GripKind.Radius, 3));
				g.Add(new Grip(el.Center.X - nx, el.Center.Y - ny, GripKind.Radius, 4));
				break;
			}
			case Spline sp:
			{
				// 맞춤점이 있으면 맞춤점, 없으면 제어점 (기존 CAD와 같음)
				bool fit = sp.FitPoints.Count >= 2;
				var pts = fit ? sp.FitPoints : sp.ControlPoints;
				for (int i = 0; i < pts.Count && i < 500; i++)
				{
					g.Add(new Grip(pts[i].X, pts[i].Y, GripKind.Stretch, (fit ? 10000 : 0) + i));
				}
				break;
			}
			case Solid so:
				g.Add(new Grip(so.FirstCorner.X, so.FirstCorner.Y, GripKind.Stretch, 0));
				g.Add(new Grip(so.SecondCorner.X, so.SecondCorner.Y, GripKind.Stretch, 1));
				g.Add(new Grip(so.ThirdCorner.X, so.ThirdCorner.Y, GripKind.Stretch, 2));
				g.Add(new Grip(so.FourthCorner.X, so.FourthCorner.Y, GripKind.Stretch, 3));
				break;
			case Point pt:
				g.Add(new Grip(pt.Location.X, pt.Location.Y, GripKind.Move, 0));
				break;
		}
		return g;
	}

	/// <summary>
	/// 그립을 (x, y)로 옮긴 결과를 실행 취소 가능한 동작으로 만든다 (아직 실행하지 않음). 바뀌는 것이 없으면 null.
	/// </summary>
	public static IUndoableAction? CreateEdit(Entity e, Grip grip, double x, double y)
	{
		if (e is TableEntity table && grip.Index >= 100)
		{
			var tBefore = new Free2DCad.Core.Tables.TableEditor.Snapshot(table);
			ApplyTableGrip(table, grip, x, y);
			var tAfter = new Free2DCad.Core.Tables.TableEditor.Snapshot(table);
			tBefore.Restore(table);
			return new EntityEditAction("표 크기 변경", table, () => tAfter.Restore(table), () => tBefore.Restore(table));
		}
		var before = GeometryState.Capture(e);
		if (before == null)
		{
			return null;
		}
		Apply(e, grip, x, y);
		var after = GeometryState.Capture(e)!;
		before.Restore(e); // 실행은 doc.Execute가 한다
		return new GripEditAction(e, before, after);
	}

	/// <summary>미리보기용 복사본 (원본은 바꾸지 않음).</summary>
	public static Entity? Preview(Entity e, Grip grip, double x, double y)
	{
		if (e is TableEntity table && grip.Index >= 100)
		{
			// 표 복사본은 행·열 목록을 원본과 같이 쓰므로, 새 크기의 바깥선과 열 경계만 보여 준다
			return TablePreview(table, grip, x, y);
		}
		if (e.Clone() is not Entity copy)
		{
			return null;
		}
		try
		{
			Apply(copy, grip, x, y);
			return copy;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static void Apply(Entity e, Grip grip, double x, double y)
	{
		double dx = x - grip.X, dy = y - grip.Y;
		switch (e)
		{
			case Line l:
				if (grip.Index == 0) l.StartPoint = new XYZ(x, y, l.StartPoint.Z);
				else if (grip.Index == 2) l.EndPoint = new XYZ(x, y, l.EndPoint.Z);
				else
				{
					l.StartPoint = new XYZ(l.StartPoint.X + dx, l.StartPoint.Y + dy, l.StartPoint.Z);
					l.EndPoint = new XYZ(l.EndPoint.X + dx, l.EndPoint.Y + dy, l.EndPoint.Z);
				}
				break;
			case Arc a:
				if (grip.Index == 0)
				{
					a.Center = new XYZ(a.Center.X + dx, a.Center.Y + dy, a.Center.Z);
				}
				else
				{
					// 시작·중간·끝 중 하나를 옮긴 세 점을 지나는 호로 다시 만든다
					var (s, m, en) = ArcPoints(a);
					if (grip.Index == 1) s = new XY(x, y);
					else if (grip.Index == 2) m = new XY(x, y);
					else en = new XY(x, y);
					SetArcThrough(a, s, m, en);
				}
				break;
			case Circle c:
				if (grip.Kind == GripKind.Move)
				{
					c.Center = new XYZ(c.Center.X + dx, c.Center.Y + dy, c.Center.Z);
				}
				else
				{
					double r = Math.Sqrt((x - c.Center.X) * (x - c.Center.X) + (y - c.Center.Y) * (y - c.Center.Y));
					if (r > 1e-9) c.Radius = r;
				}
				break;
			case LwPolyline pl:
			{
				var v = pl.Vertices;
				if (grip.Index < 1000)
				{
					v[grip.Index].Location = new XY(x, y);
				}
				else
				{
					int i = grip.Index - 1000;
					int j = (i + 1) % v.Count;
					v[i].Location = new XY(v[i].Location.X + dx, v[i].Location.Y + dy);
					v[j].Location = new XY(v[j].Location.X + dx, v[j].Location.Y + dy);
				}
				break;
			}
			case DimensionAligned d:
				switch (grip.Index)
				{
					// 측정점을 옮겨도 치수선 위치는 그대로 (정의점을 새 두 번째 점 기준으로 다시 계산)
					case 0: { var keep = d.DefinitionPoint; d.FirstPoint = new XYZ(x, y, 0); DimensionGeometry.PlaceDimensionLine(d, keep); break; }
					case 1: { var keep = d.DefinitionPoint; d.SecondPoint = new XYZ(x, y, 0); DimensionGeometry.PlaceDimensionLine(d, keep); break; }
					case 2: DimensionGeometry.PlaceDimensionLine(d, new XYZ(x, y, 0)); break;
					case 3: DimensionGeometry.MoveText(d, new XYZ(x, y, 0)); break;
				}
				TryUpdateBlock(d);
				break;
			case DimensionRadius r:
				if (grip.Index == 0) r.DefinitionPoint = new XYZ(x, y, 0);
				else { r.TextMiddlePoint = new XYZ(x, y, 0); r.IsTextUserDefinedLocation = true; }
				TryUpdateBlock(r);
				break;
			case DimensionDiameter dd:
				dd.TextMiddlePoint = new XYZ(x, y, 0);
				dd.IsTextUserDefinedLocation = true;
				TryUpdateBlock(dd);
				break;
			case MultiLeader ml when ml.ContextData.LeaderRoots.Count > 0:
			{
				var root = ml.ContextData.LeaderRoots[0];
				if (grip.Index == 100)
				{
					// 연결점을 옮기면 착륙선·문자도 같이, 방향은 마지막 지시선 점 기준
					root.ConnectionPoint = new XYZ(x, y, 0);
					if (root.Lines.Count > 0 && root.Lines[0].Points.Count > 0)
					{
						root.Direction = new XYZ(x >= root.Lines[0].Points[^1].X ? 1 : -1, 0, 0);
					}
				}
				else if (root.Lines.Count > 0 && grip.Index < root.Lines[0].Points.Count)
				{
					root.Lines[0].Points[grip.Index] = new XYZ(x, y, 0);
				}
				Free2DCad.Core.Commands.MLeaderBuilder.SetText(ml, ml.ContextData.TextLabel ?? string.Empty);
				break;
			}
			case DimensionLargeRadial lr:
				switch (grip.Index)
				{
					case 0: lr.OverrideCenter = new XYZ(x, y, 0); break;
					case 1:
					{
						// 꺾임 점은 치수선(중심-원 위 점) 위로
						var c = lr.DefinitionPoint;
						double r = lr.Measurement;
						if (r > 1e-12)
						{
							double ux = (lr.ChordPoint.X - c.X) / r, uy = (lr.ChordPoint.Y - c.Y) / r;
							double t = Math.Clamp((x - c.X) * ux + (y - c.Y) * uy, 0, r);
							lr.JogPoint = new XYZ(c.X + ux * t, c.Y + uy * t, 0);
						}
						break;
					}
					default:
					{
						// 문자를 옮기면 원 위 점도 그 방향으로 (기존 CAD와 같음)
						var c = lr.DefinitionPoint;
						double r = lr.Measurement, d = Math.Sqrt((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y));
						if (d > 1e-12 && r > 1e-12)
						{
							double ux = (x - c.X) / d, uy = (y - c.Y) / d;
							double jt = (lr.JogPoint.X - c.X) * (lr.ChordPoint.X - c.X) / r + (lr.JogPoint.Y - c.Y) * (lr.ChordPoint.Y - c.Y) / r;
							lr.ChordPoint = new XYZ(c.X + ux * r, c.Y + uy * r, 0);
							lr.JogPoint = new XYZ(c.X + ux * jt, c.Y + uy * jt, 0);
						}
						lr.TextMiddlePoint = new XYZ(x, y, 0);
						lr.IsTextUserDefinedLocation = d > r;
						break;
					}
				}
				TryUpdateBlock(lr);
				break;
			case TextEntity t:
				t.InsertPoint = new XYZ(t.InsertPoint.X + dx, t.InsertPoint.Y + dy, t.InsertPoint.Z);
				t.AlignmentPoint = new XYZ(t.AlignmentPoint.X + dx, t.AlignmentPoint.Y + dy, t.AlignmentPoint.Z);
				break;
			case MText mt:
				mt.InsertPoint = new XYZ(mt.InsertPoint.X + dx, mt.InsertPoint.Y + dy, mt.InsertPoint.Z);
				break;
			case TableEntity tb when grip.Index >= 100:
				ApplyTableGrip(tb, grip, x, y);
				break;
			case Insert ins:
				ins.InsertPoint = new XYZ(ins.InsertPoint.X + dx, ins.InsertPoint.Y + dy, ins.InsertPoint.Z);
				break;
			case Ellipse el:
				if (grip.Index == 0)
				{
					el.Center = new XYZ(el.Center.X + dx, el.Center.Y + dy, el.Center.Z);
				}
				else if (grip.Index <= 2)
				{
					// 장축 끝: 방향과 길이를 새 점으로 (단축 길이는 유지)
					var mj = el.MajorAxisEndPoint;
					double oldMinor = Math.Sqrt(mj.X * mj.X + mj.Y * mj.Y) * el.RadiusRatio;
					double sign = grip.Index == 1 ? 1 : -1;
					var nm = new XYZ((x - el.Center.X) * sign, (y - el.Center.Y) * sign, 0);
					double nlen = Math.Sqrt(nm.X * nm.X + nm.Y * nm.Y);
					if (nlen > 1e-9 && oldMinor <= nlen)
					{
						el.MajorAxisEndPoint = nm;
						el.RadiusRatio = oldMinor / nlen;
					}
				}
				else
				{
					// 단축 끝: 단축 길이만 (장축보다 길어지면 무시)
					var mj = el.MajorAxisEndPoint;
					double mlen = Math.Sqrt(mj.X * mj.X + mj.Y * mj.Y);
					double minor = Math.Abs((x - el.Center.X) * -mj.Y / mlen + (y - el.Center.Y) * mj.X / mlen);
					if (mlen > 1e-9 && minor > 1e-9 && minor <= mlen)
					{
						el.RadiusRatio = minor / mlen;
					}
				}
				break;
			case Spline sp:
			{
				bool fit = grip.Index >= 10000;
				int i = fit ? grip.Index - 10000 : grip.Index;
				var pts = fit ? sp.FitPoints : sp.ControlPoints;
				if (i >= 0 && i < pts.Count)
				{
					pts[i] = new XYZ(x, y, pts[i].Z);
				}
				break;
			}
			case Solid so:
				switch (grip.Index)
				{
					case 0: so.FirstCorner = new XYZ(x, y, so.FirstCorner.Z); break;
					case 1: so.SecondCorner = new XYZ(x, y, so.SecondCorner.Z); break;
					case 2: so.ThirdCorner = new XYZ(x, y, so.ThirdCorner.Z); break;
					case 3: so.FourthCorner = new XYZ(x, y, so.FourthCorner.Z); break;
				}
				break;
			case Point pt:
				pt.Location = new XYZ(pt.Location.X + dx, pt.Location.Y + dy, pt.Location.Z);
				break;
		}
	}

	// ===== 표 그립 (기존 CAD와 같음) =====
	// 0 = 왼쪽 위(이동), 100+i = i번째 열 오른쪽 경계(그 열 너비), 200 = 오른쪽 위(전체 너비),
	// 201 = 왼쪽 아래(전체 높이), 202 = 오른쪽 아래(너비·높이 함께). 회전한 표는 이동만.

	private static readonly Lazy<ACadSharp.CadDocument> TableScratchDoc = new(() => new ACadSharp.CadDocument());

	/// <summary>회전하지 않은 표의 (왼쪽 위 x, y, 전체 너비, 전체 높이). 회전한 표면 null.</summary>
	private static (double X0, double Y0, double W, double H)? TableFrame(TableEntity t)
	{
		var d = t.HorizontalDirection;
		if (t.Columns.Count == 0 || t.Rows.Count == 0 || Math.Abs(d.Y) > 1e-9 || d.X < 0)
		{
			return null;
		}
		return (t.InsertPoint.X, t.InsertPoint.Y, t.Columns.Sum(c => c.Width), t.Rows.Sum(r => r.Height));
	}

	private static void AddTableGrips(TableEntity t, List<Grip> g)
	{
		var (x0, y0, w, h) = TableFrame(t)!.Value;
		g.Add(new Grip(x0, y0, GripKind.Move, 0));
		double yCol = t.Rows.Count > 1 ? y0 - t.Rows[0].Height : y0;
		double cum = 0;
		for (int i = 0; i < t.Columns.Count; i++)
		{
			cum += t.Columns[i].Width;
			g.Add(new Grip(x0 + cum, yCol, GripKind.Stretch, 100 + i));
		}
		g.Add(new Grip(x0 + w, y0, GripKind.Stretch, 200));
		g.Add(new Grip(x0, y0 - h, GripKind.Stretch, 201));
		g.Add(new Grip(x0 + w, y0 - h, GripKind.Stretch, 202));
	}

	/// <summary>표 크기 그립 미리보기: 새 크기의 바깥선 + 열 경계선 (원본은 바꾸지 않음).</summary>
	private static Entity? TablePreview(TableEntity t, Grip grip, double x, double y)
	{
		if (TableFrame(t) is not { } f)
		{
			return null;
		}
		var widths = t.Columns.Select(c => c.Width).ToList();
		double hTotal = f.H;
		if (grip.Index >= 100 && grip.Index < 100 + widths.Count)
		{
			int i = grip.Index - 100;
			double nw = x - (f.X0 + widths.Take(i).Sum());
			if (nw > 1e-3) widths[i] = nw;
		}
		else
		{
			if ((grip.Index == 200 || grip.Index == 202) && x - f.X0 > 1e-3 && f.W > 1e-3)
			{
				double k = (x - f.X0) / f.W;
				for (int i = 0; i < widths.Count; i++) widths[i] *= k;
			}
			if ((grip.Index == 201 || grip.Index == 202) && f.Y0 - y > 1e-3)
			{
				hTotal = f.Y0 - y;
			}
		}
		double wTotal = widths.Sum();
		var pl = new LwPolyline();
		void V(double px, double py) => pl.Vertices.Add(new LwPolyline.Vertex(new XY(px, py)));
		// 바깥선을 한 번 돌고, 열 경계마다 위아래로 오가는 한 줄짜리 선
		V(f.X0, f.Y0);
		double cum = 0;
		for (int i = 0; i < widths.Count; i++)
		{
			cum += widths[i];
			bool down = i % 2 == 0;
			V(f.X0 + cum, down ? f.Y0 : f.Y0 - hTotal);
			V(f.X0 + cum, down ? f.Y0 - hTotal : f.Y0);
		}
		V(f.X0 + wTotal, f.Y0);
		V(f.X0, f.Y0);
		V(f.X0, f.Y0 - hTotal);
		V(f.X0 + wTotal, f.Y0 - hTotal);
		pl.Color = t.Color;
		pl.Layer = t.Layer;
		return pl;
	}

	private static void ApplyTableGrip(TableEntity t, Grip grip, double x, double y)
	{
		if (TableFrame(t) is not { } f)
		{
			return;
		}
		var (x0, y0, w, h) = f;
		double th = Free2DCad.Core.Tables.TableEditor.GuessTextHeight(t);
		const double Min = 1e-3;
		if (grip.Index >= 100 && grip.Index < 100 + t.Columns.Count)
		{
			int i = grip.Index - 100;
			double left = x0 + t.Columns.Take(i).Sum(c => c.Width);
			double nw = x - left;
			if (nw > Min) t.Columns[i].Width = nw;
		}
		else
		{
			if ((grip.Index == 200 || grip.Index == 202) && x - x0 > Min && w > Min)
			{
				double k = (x - x0) / w;
				foreach (var c in t.Columns) c.Width *= k;
			}
			if ((grip.Index == 201 || grip.Index == 202) && y0 - y > Min && h > Min)
			{
				double k = (y0 - y) / h;
				foreach (var r in t.Rows) r.Height *= k;
			}
		}
		Free2DCad.Core.Tables.TableEditor.Regenerate(t.Document ?? TableScratchDoc.Value, t, th);
	}

	/// <summary>치수 값·문자를 바꾼 뒤 치수 블록을 다시 만든다.</summary>
	public static void RefreshDimension(Dimension d) => TryUpdateBlock(d);

	internal static void TryUpdateBlock(Dimension d)
	{
		try
		{
			d.UpdateBlock();
		}
		catch (Exception)
		{
			// 치수 블록 다시 만들기 실패는 무시 (다음 저장 때 다시 시도)
		}
	}

	private static (XY S, XY M, XY E) ArcPoints(Arc a)
	{
		double sa = a.StartAngle, ea = a.EndAngle;
		if (ea < sa) ea += 2 * Math.PI;
		double ma = (sa + ea) / 2;
		XY P(double ang) => new(a.Center.X + a.Radius * Math.Cos(ang), a.Center.Y + a.Radius * Math.Sin(ang));
		return (P(sa), P(ma), P(ea));
	}

	/// <summary>세 점을 지나는 호 (시계 반대 방향 s→m→e). 세 점이 한 직선 위면 바꾸지 않음.</summary>
	private static void SetArcThrough(Arc a, XY s, XY m, XY e)
	{
		double d = 2 * (s.X * (m.Y - e.Y) + m.X * (e.Y - s.Y) + e.X * (s.Y - m.Y));
		if (Math.Abs(d) < 1e-12)
		{
			return;
		}
		double s2 = s.X * s.X + s.Y * s.Y, m2 = m.X * m.X + m.Y * m.Y, e2 = e.X * e.X + e.Y * e.Y;
		double cx = (s2 * (m.Y - e.Y) + m2 * (e.Y - s.Y) + e2 * (s.Y - m.Y)) / d;
		double cy = (s2 * (e.X - m.X) + m2 * (s.X - e.X) + e2 * (m.X - s.X)) / d;
		double r = Math.Sqrt((s.X - cx) * (s.X - cx) + (s.Y - cy) * (s.Y - cy));
		double angS = Math.Atan2(s.Y - cy, s.X - cx);
		double angE = Math.Atan2(e.Y - cy, e.X - cx);
		// d > 0 이면 s→m→e가 시계 반대 방향
		if (d < 0)
		{
			(angS, angE) = (angE, angS);
		}
		a.Center = new XYZ(cx, cy, a.Center.Z);
		a.Radius = r;
		a.StartAngle = Normalize(angS);
		a.EndAngle = Normalize(angE);
	}

	private static double Normalize(double ang)
	{
		ang %= 2 * Math.PI;
		return ang < 0 ? ang + 2 * Math.PI : ang;
	}
}

/// <summary>그립 편집 전후 모양. 실행 취소는 이전 모양으로 되돌린다.</summary>
public sealed class GripEditAction : IUndoableAction, IEntityChange
{
	private readonly Entity _entity;
	private readonly GeometryState _before;
	private readonly GeometryState _after;

	public GripEditAction(Entity entity, GeometryState before, GeometryState after)
	{
		this._entity = entity;
		this._before = before;
		this._after = after;
	}

	public string Name => "그립 편집";

	public IEnumerable<Entity> AffectedEntities => new[] { this._entity };

	public void Do() => this._after.Restore(this._entity);

	public void Undo() => this._before.Restore(this._entity);
}

/// <summary>엔티티의 모양(좌표) 값 모음. 그립 편집이 바꾸는 값만 담는다.</summary>
public sealed class GeometryState
{
	private readonly XYZ[] _points;
	private readonly double[] _numbers;
	private readonly bool _flag;

	private GeometryState(XYZ[] points, double[] numbers, bool flag = false)
	{
		this._points = points;
		this._numbers = numbers;
		this._flag = flag;
	}

	public static GeometryState? Capture(Entity e) => e switch
	{
		Line l => new(new[] { l.StartPoint, l.EndPoint }, Array.Empty<double>()),
		Arc a => new(new[] { a.Center }, new[] { a.Radius, a.StartAngle, a.EndAngle }),
		Circle c => new(new[] { c.Center }, new[] { c.Radius }),
		LwPolyline pl => new(pl.Vertices.Select(v => new XYZ(v.Location.X, v.Location.Y, v.Bulge)).ToArray(), Array.Empty<double>()),
		DimensionAligned d => new(new[] { d.FirstPoint, d.SecondPoint, d.DefinitionPoint, d.TextMiddlePoint }, Array.Empty<double>(), d.IsTextUserDefinedLocation),
		DimensionLargeRadial lr => new(new[] { lr.DefinitionPoint, lr.TextMiddlePoint, lr.ChordPoint, lr.OverrideCenter, lr.JogPoint }, Array.Empty<double>(), lr.IsTextUserDefinedLocation),
		MultiLeader ml when ml.ContextData.LeaderRoots.Count > 0 => new(
			new[] { ml.ContextData.LeaderRoots[0].ConnectionPoint, ml.ContextData.LeaderRoots[0].Direction, ml.ContextData.TextLocation, ml.ContextData.ContentBasePoint }
				.Concat(ml.ContextData.LeaderRoots[0].Lines.Count > 0 ? ml.ContextData.LeaderRoots[0].Lines[0].Points : Enumerable.Empty<XYZ>()).ToArray(),
			new double[] { (double)ml.ContextData.TextAlignment, (double)ml.ContextData.TextAttachmentPoint }),
		Dimension d => new(new[] { d.DefinitionPoint, d.TextMiddlePoint }, Array.Empty<double>(), d.IsTextUserDefinedLocation),
		TextEntity t => new(new[] { t.InsertPoint, t.AlignmentPoint }, Array.Empty<double>()),
		MText mt => new(new[] { mt.InsertPoint }, Array.Empty<double>()),
		Insert ins => new(new[] { ins.InsertPoint }, Array.Empty<double>()),
		Ellipse el => new(new[] { el.Center, el.MajorAxisEndPoint }, new[] { el.RadiusRatio }),
		Spline sp => new(sp.ControlPoints.Concat(sp.FitPoints).ToArray(), new double[] { sp.ControlPoints.Count, sp.FitPoints.Count }),
		Solid so => new(new[] { so.FirstCorner, so.SecondCorner, so.ThirdCorner, so.FourthCorner }, Array.Empty<double>()),
		Point pt => new(new[] { pt.Location }, Array.Empty<double>()),
		_ => null,
	};

	public void Restore(Entity e)
	{
		switch (e)
		{
			case Line l:
				l.StartPoint = this._points[0];
				l.EndPoint = this._points[1];
				break;
			case Arc a:
				a.Center = this._points[0];
				a.Radius = this._numbers[0];
				a.StartAngle = this._numbers[1];
				a.EndAngle = this._numbers[2];
				break;
			case Circle c:
				c.Center = this._points[0];
				c.Radius = this._numbers[0];
				break;
			case LwPolyline pl:
				for (int i = 0; i < pl.Vertices.Count && i < this._points.Length; i++)
				{
					pl.Vertices[i].Location = new XY(this._points[i].X, this._points[i].Y);
					pl.Vertices[i].Bulge = this._points[i].Z;
				}
				break;
			case DimensionLargeRadial lr:
				lr.DefinitionPoint = this._points[0];
				lr.TextMiddlePoint = this._points[1];
				lr.ChordPoint = this._points[2];
				lr.OverrideCenter = this._points[3];
				lr.JogPoint = this._points[4];
				lr.IsTextUserDefinedLocation = this._flag;
				GripEditor.TryUpdateBlock(lr);
				break;
			case MultiLeader ml when ml.ContextData.LeaderRoots.Count > 0:
			{
				var root = ml.ContextData.LeaderRoots[0];
				root.ConnectionPoint = this._points[0];
				root.Direction = this._points[1];
				ml.ContextData.TextLocation = this._points[2];
				ml.ContextData.ContentBasePoint = this._points[3];
				if (root.Lines.Count > 0)
				{
					var pts = root.Lines[0].Points;
					for (int i = 0; i < pts.Count && 4 + i < this._points.Length; i++) pts[i] = this._points[4 + i];
				}
				ml.ContextData.TextAlignment = (ACadSharp.TextAlignmentType)(short)this._numbers[0];
				ml.TextAlignment = ml.ContextData.TextAlignment;
				ml.ContextData.TextAttachmentPoint = (ACadSharp.TextAttachmentPointType)(short)this._numbers[1];
				break;
			}
			case DimensionAligned d:
				d.FirstPoint = this._points[0];
				d.SecondPoint = this._points[1];
				d.DefinitionPoint = this._points[2];
				d.TextMiddlePoint = this._points[3];
				d.IsTextUserDefinedLocation = this._flag;
				GripEditor.TryUpdateBlock(d);
				break;
			case Dimension d:
				d.DefinitionPoint = this._points[0];
				d.TextMiddlePoint = this._points[1];
				d.IsTextUserDefinedLocation = this._flag;
				GripEditor.TryUpdateBlock(d);
				break;
			case TextEntity t:
				t.InsertPoint = this._points[0];
				t.AlignmentPoint = this._points[1];
				break;
			case MText mt:
				mt.InsertPoint = this._points[0];
				break;
			case Insert ins:
				ins.InsertPoint = this._points[0];
				break;
			case Ellipse el:
				el.Center = this._points[0];
				el.MajorAxisEndPoint = this._points[1];
				el.RadiusRatio = this._numbers[0];
				break;
			case Spline sp:
			{
				int nc = (int)this._numbers[0], nf = (int)this._numbers[1];
				for (int i = 0; i < nc && i < sp.ControlPoints.Count; i++) sp.ControlPoints[i] = this._points[i];
				for (int i = 0; i < nf && i < sp.FitPoints.Count; i++) sp.FitPoints[i] = this._points[nc + i];
				break;
			}
			case Solid so:
				so.FirstCorner = this._points[0];
				so.SecondCorner = this._points[1];
				so.ThirdCorner = this._points[2];
				so.FourthCorner = this._points[3];
				break;
			case Point pt:
				pt.Location = this._points[0];
				break;
		}
	}
}

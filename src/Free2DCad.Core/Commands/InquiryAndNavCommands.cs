using ACadSharp;
using ACadSharp.Entities;
using Free2DCad.Core.Actions;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Commands;

/// <summary>
/// MATCHPROP 명령: 원본 객체의 특성(도면층, 색상, 선종류)을 대상 객체에 일치시킨다.
/// </summary>
public sealed class MatchPropCommand : EditorCommand
{
	/// <summary>대상 객체 선택 단계</summary>
	public override bool WantsSelection => this._step == 1;

	private int _step;
	private Entity? _source;

	public override string Name => "MATCHPROP";
	public override string[] Aliases => new[] { "MA" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._source = null;
		this.Prompt = "원본 객체 선택:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;

		var scene = SceneBuilder.Build(this.Ctx.Doc.Document);
		var index = new SceneIndex(scene);
		var picked = index.Pick(x, y, 5.0);

		if (this._step == 0 && picked != null)
		{
			this._source = picked;
			this._step = 1;
			this.Prompt = "대상 객체 선택 또는 [설정(S)]:";
		}
		else if (this._step == 1 && picked != null && this._source != null)
		{
			if (!ReferenceEquals(picked, this._source))
			{
				this.Ctx.Doc.Execute(new MatchPropertiesAction(new[] { picked }, this._source));
			}
		}
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this.Ctx != null && this._step == 1 && this._source != null)
		{
			var targets = entities.Where(e => !ReferenceEquals(e, this._source)).ToList();
			if (targets.Count > 0)
			{
				this.Ctx.Doc.Execute(new MatchPropertiesAction(targets, this._source));
			}
		}
	}

	public override void OnEnter()
	{
		this.Finish();
	}
}

/// <summary>
/// LIST 명령: 선택한 객체의 상세 기하 및 속성 정보를 출력한다.
/// </summary>
public sealed class ListCommand : EditorCommand
{
	/// <summary>객체 선택</summary>
	public override bool WantsSelection => !this.IsFinished;

	private readonly List<Entity> _selected = new();

	public override string Name => "LIST";
	public override string[] Aliases => new[] { "LI" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._selected.Clear();
		if (ctx.Selection.Count > 0)
		{
			this._selected.AddRange(ctx.Selection);
			foreach (var entity in this._selected)
			{
				this.PrintEntityInfo(this.Ctx!, entity);
			}
			this.Finish();
			return;
		}
		this.Prompt = "[스크롤(SC)/정렬(SO)/추적(T)]/<나열할 객체 선택>:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var scene = SceneBuilder.Build(this.Ctx.Doc.Document);
		var index = new SceneIndex(scene);
		var picked = index.Pick(x, y, 5.0);
		if (picked != null && !this._selected.Contains(picked))
		{
			this._selected.Add(picked);
		}
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		foreach (var e in entities)
		{
			if (!this._selected.Contains(e))
			{
				this._selected.Add(e);
			}
		}
	}

	public override void OnEnter()
	{
		if (this.Ctx == null)
		{
			this.Finish();
			return;
		}

		foreach (var entity in this._selected)
		{
			this.PrintEntityInfo(this.Ctx, entity);
		}

		this.Finish();
	}

	/// <summary>기존 CAD LIST와 같은 모양: 객체 이름 줄 → 오른쪽 정렬한 "이름:  값" 줄들.</summary>
	private void PrintEntityInfo(CommandContext ctx, Entity e)
	{
		static string F4(double v) => v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
		static string Pt(CSMath.XYZ p) => $"X = {F4(p.X)}  Y = {F4(p.Y)}  Z = {F4(p.Z)}";
		void Row(string label, string value) => ctx.Print($"{label.PadLeft(12)}:  {value}");
		static string ColorText(ACadSharp.Color c) => c.IsByLayer ? "BYLAYER" : c.IsByBlock ? "BYBLOCK" : c.IsTrueColor ? $"{c.R},{c.G},{c.B}" : c.Index switch
		{
			1 => "1 (빨강)", 2 => "2 (노랑)", 3 => "3 (초록)", 4 => "4 (하늘)", 5 => "5 (파랑)", 6 => "6 (자홍)", 7 => "7 (흰색)", _ => c.Index.ToString(),
		};
		static string LineWeightText(ACadSharp.LineWeightType w) => w switch
		{
			ACadSharp.LineWeightType.ByLayer => "BYLAYER",
			ACadSharp.LineWeightType.ByBlock => "BYBLOCK",
			ACadSharp.LineWeightType.Default => "Default",
			_ => $"{(int)w / 100.0:0.00} mm",
		};

		ctx.Print($"{new string('-', 21)} {e.ObjectName} {new string('-', 28)}");
		Row("핸들", e.Handle.ToString("X"));
		bool paper = e.Owner is ACadSharp.Tables.BlockRecord br && br.Name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase);
		Row("현재 공간", paper ? "도면 공간" : "2D 모델 공간");
		Row("도면층", e.Layer?.Name ?? "0");
		Row("색상", ColorText(e.Color));
		Row("선종류", e.LineType?.Name?.ToUpperInvariant() switch { null or "BYLAYER" => "BYLAYER", "BYBLOCK" => "BYBLOCK", _ => e.LineType!.Name });
		Row("선두께", LineWeightText(e.LineWeight));

		switch (e)
		{
			case Line l:
				{
					double dx = l.EndPoint.X - l.StartPoint.X, dy = l.EndPoint.Y - l.StartPoint.Y, dz = l.EndPoint.Z - l.StartPoint.Z;
					double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI;
					if (ang < 0) ang += 360.0;
					Row("시작점", Pt(l.StartPoint));
					Row("끝점", Pt(l.EndPoint));
					Row("길이", F4(Math.Sqrt(dx * dx + dy * dy + dz * dz)));
					Row("각도", F4(ang));
					Row("델타", $"X = {F4(dx)}  Y = {F4(dy)}  Z = {F4(dz)}");
					break;
				}
			case Arc a:
				{
					double sweep = a.EndAngle - a.StartAngle;
					if (sweep <= 0) sweep += 2 * Math.PI;
					Row("중심", Pt(a.Center));
					Row("반지름", F4(a.Radius));
					Row("시작 각도", F4(a.StartAngle * 180 / Math.PI));
					Row("끝 각도", F4(a.EndAngle * 180 / Math.PI));
					Row("길이", F4(a.Radius * sweep));
					break;
				}
			case Circle c:
				Row("중심", Pt(c.Center));
				Row("반지름", F4(c.Radius));
				Row("원주", F4(2 * Math.PI * c.Radius));
				Row("면적", $"{F4(Math.PI * c.Radius * c.Radius)} sq. units");
				break;
			case LwPolyline p:
				{
					var segs = Free2DCad.Core.Geometry.Geom.SegmentsOf(p);
					double area = 0;
					if (p.IsClosed)
					{
						var v = p.Vertices;
						for (int k = 0; k < v.Count; k++)
						{
							var a0 = v[k].Location;
							var a1 = v[(k + 1) % v.Count].Location;
							area += a0.X * a1.Y - a1.X * a0.Y;
							if (Math.Abs(v[k].Bulge) > 1e-12 && segs[k] is Free2DCad.Core.Geometry.Geom.ArcSeg arc)
							{
								// 호 조각의 활꼴 면적
								double th = Math.Abs(arc.Sweep);
								area += Math.Sign(arc.Sweep) * arc.R * arc.R * (th - Math.Sin(th));
							}
						}
						area = Math.Abs(area / 2);
					}
					Row("폴리선 플래그", p.IsClosed ? "닫힘" : "열림");
					Row("폭", F4(p.ConstantWidth));
					Row("면적", $"{F4(area)} sq. units");
					Row("길이", F4(Free2DCad.Core.Geometry.Geom.Length(segs)));
					foreach (var v in p.Vertices)
					{
						Row("위치", $"X = {F4(v.Location.X)}  Y = {F4(v.Location.Y)}  Z = {F4(p.Elevation)}");
					}
					break;
				}
			case TextEntity t:
				Row("문자 스타일", t.Style?.Name ?? "Standard");
				Row("문자 시작점", Pt(t.InsertPoint));
				Row("문자 높이", F4(t.Height));
				Row("문자 회전", F4(t.Rotation * 180 / Math.PI));
				Row("문자 값", t.Value);
				break;
			case MText mt:
				Row("문자 스타일", mt.Style?.Name ?? "Standard");
				Row("삽입점", Pt(mt.InsertPoint));
				Row("문자 높이", F4(mt.Height));
				Row("너비", F4(mt.RectangleWidth));
				Row("문자 값", mt.Value);
				break;
			case Insert ins:
				Row("블록 이름", ins.Block?.Name ?? string.Empty);
				Row("삽입점", Pt(ins.InsertPoint));
				Row("X 축척 비율", F4(ins.XScale));
				Row("Y 축척 비율", F4(ins.YScale));
				Row("회전", F4(ins.Rotation * 180 / Math.PI));
				break;
			case Ellipse el:
				Row("중심", Pt(el.Center));
				Row("장축", Pt(el.MajorAxisEndPoint));
				Row("반경 비율", F4(el.RadiusRatio));
				break;
		}
	}
}

/// <summary>
/// ID 명령: 지정한 점의 X, Y, Z 좌표를 출력한다.
/// </summary>
public sealed class IdCommand : EditorCommand
{
	public override string Name => "ID";
	public override string[] Aliases => new[] { "ID" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this.Prompt = "점 지정:";
	}

	public override void OnText(string text)
	{
		if (this.Ctx != null && CoordinateParser.TryParse(text.Trim(), null, null, false, out (double X, double Y) pt))
		{
			this.OnPoint(pt.X, pt.Y);
		}
	}

	public override void OnPoint(double x, double y)
	{
		this.Ctx?.Print($"X = {x:F4}   Y = {y:F4}   Z = 0.0000");
		this.Finish();
	}
}

/// <summary>
/// AREA 명령: 점들을 연속 지정하거나 객체(O)를 선택하여 면적과 둘레를 계산한다.
/// </summary>
public sealed class AreaCommand : EditorCommand
{
	private int _step;
	private readonly List<(double X, double Y)> _pts = new();

	public override string Name => "AREA";
	public override string[] Aliases => new[] { "AA" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._pts.Clear();
		this.Prompt = "첫 번째 구석점 지정 또는 [객체(O)]:";
	}

	public override void OnText(string text)
	{
		string trimmed = text.Trim();

		if (string.Equals(trimmed, "O", StringComparison.OrdinalIgnoreCase))
		{
			this._step = 10;
			this.Prompt = "객체 선택:";
			return;
		}

		if (this.Ctx != null && CoordinateParser.TryParse(trimmed, this.Ctx.LastPoint, null, false, out (double X, double Y) pt))
		{
			this.OnPoint(pt.X, pt.Y);
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;

		if (this._step == 10)
		{
			var scene = SceneBuilder.Build(this.Ctx.Doc.Document);
			var index = new SceneIndex(scene);
			var picked = index.Pick(x, y, 5.0);

			if (picked is Circle c && picked is not Arc)
			{
				double area = Math.PI * c.Radius * c.Radius;
				double perim = 2.0 * Math.PI * c.Radius;
				this.Ctx.Print($"면적 = {area:F4}, 둘레 = {perim:F4}");
				this.Finish();
			}
			else if (picked is LwPolyline p)
			{
				var coords = p.Vertices.Select(v => (v.Location.X, v.Location.Y)).ToList();
				double area = CalcShoelace(coords);
				double perim = CalcPerimeter(coords, p.IsClosed);
				this.Ctx.Print($"면적 = {area:F4}, 둘레 = {perim:F4}");
				this.Finish();
			}
		}
		else
		{
			this._pts.Add((x, y));
			this.Ctx.LastPoint = (x, y);
			this._step = 1;
			this.Prompt = "다음 점 지정 또는 [Enter로 완료]:";
		}
	}

	public override void OnEnter()
	{
		if (this.Ctx != null && this._pts.Count >= 3)
		{
			double area = CalcShoelace(this._pts);
			double perim = CalcPerimeter(this._pts, isClosed: true);
			this.Ctx.Print($"면적 = {area:F4}, 둘레 = {perim:F4}");
		}
		this.Finish();
	}

	private static double CalcShoelace(IReadOnlyList<(double X, double Y)> pts)
	{
		if (pts.Count < 3) return 0.0;
		double sum = 0.0;
		for (int i = 0; i < pts.Count; i++)
		{
			int next = (i + 1) % pts.Count;
			sum += pts[i].X * pts[next].Y - pts[next].X * pts[i].Y;
		}
		return Math.Abs(sum) / 2.0;
	}

	private static double CalcPerimeter(IReadOnlyList<(double X, double Y)> pts, bool isClosed)
	{
		if (pts.Count < 2) return 0.0;
		double perim = 0.0;
		int count = isClosed ? pts.Count : pts.Count - 1;
		for (int i = 0; i < count; i++)
		{
			int next = (i + 1) % pts.Count;
			double dx = pts[next].X - pts[i].X;
			double dy = pts[next].Y - pts[i].Y;
			perim += Math.Sqrt(dx * dx + dy * dy);
		}
		return perim;
	}
}



/// <summary>
/// U 명령: 마지막 실행된 편집 명령을 취소한다.
/// </summary>
public sealed class UndoCommand : EditorCommand
{
	public override string Name => "U";
	public override string[] Aliases => new[] { "UNDO" };

	public override void Start(CommandContext ctx)
	{
		// 트랜잭션을 새로 열지 않고 즉시 Doc.Undo() 실행
		var doc = ctx?.Doc ?? throw new ArgumentNullException(nameof(ctx));
		doc.Undo();
		ctx.Print("U");
		this.IsFinished = true;
	}
}

/// <summary>
/// REDO 명령: 취소된 편집 명령을 다시 실행한다.
/// </summary>
public sealed class RedoCommand : EditorCommand
{
	public override string Name => "REDO";

	public override void Start(CommandContext ctx)
	{
		// 트랜잭션을 새로 열지 않고 즉시 Doc.Redo() 실행
		var doc = ctx?.Doc ?? throw new ArgumentNullException(nameof(ctx));
		doc.Redo();
		ctx.Print("REDO");
		this.IsFinished = true;
	}
}

/// <summary>
/// LAYER 명령: 도면층 목록을 표시하고 도면층 특성 팔레트를 호출한다.
/// </summary>
public sealed class LayerCommand : EditorCommand
{
	public override string Name => "LAYER";
	public override string[] Aliases => new[] { "LA" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		if (ctx != null)
		{
			var layers = ctx.Doc.Document.Layers;
			ctx.Print($"도면층 목록 ({layers.Count()}개):");
			foreach (var l in layers)
			{
				ctx.Print($"  - {l.Name} (색상: {l.Color.Index})");
			}
		}
		this.Finish();
	}
}

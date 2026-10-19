using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using CSMath;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>
/// MLINE (다중선): 현재 다중선 스타일의 평행선 묶음. 옵션 [자리맞추기(J)/축척(S)/스타일(ST)], 닫기(C), 명령 취소(U).
/// </summary>
public sealed class MLineCommand : EditorCommand
{
	private static MLineJustification LastJust = MLineJustification.Top;
	private readonly List<XY> _pts = new();
	private string? _await;
	private double _scale;

	public override string Name => "MLINE";
	public override string[] Aliases => new[] { "ML" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pts.Clear();
		this._await = null;
		this._scale = ctx.Doc.Document.Header.CurrentMultilineScale > 0 ? ctx.Doc.Document.Header.CurrentMultilineScale : 20;
		this.Info();
		this.Prompt = "시작점 지정 또는 [자리맞추기(J)/축척(S)/스타일(ST)]:";
	}

	private MLineStyle Style
	{
		get
		{
			var doc = this.Ctx!.Doc.Document;
			try
			{
				return doc.Header.CurrentMLineStyle ?? doc.MLineStyles.FirstOrDefault() ?? MLineStyle.Default;
			}
			catch (Exception)
			{
				return doc.MLineStyles.FirstOrDefault() ?? MLineStyle.Default;
			}
		}
	}

	private void Info()
	{
		string just = LastJust switch { MLineJustification.Top => "위", MLineJustification.Zero => "0", _ => "아래" };
		this.Ctx?.Print($"현재 설정: 자리맞추기 = {just}, 축척 = {this._scale:0.##}, 스타일 = {this.Style.Name}");
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		switch (this._await)
		{
			case "J":
				LastJust = t.StartsWith("Z") || t == "0" ? MLineJustification.Zero : t.StartsWith("B") ? MLineJustification.Bottom : t.StartsWith("T") ? MLineJustification.Top : LastJust;
				this._await = null;
				this.Info();
				this.Prompt = "시작점 지정 또는 [자리맞추기(J)/축척(S)/스타일(ST)]:";
				return;
			case "S":
				if (Num.TryParse(t, out double s) && s != 0)
				{
					this._scale = s;
					this.Ctx!.Doc.Document.Header.CurrentMultilineScale = s;
				}
				this._await = null;
				this.Info();
				this.Prompt = "시작점 지정 또는 [자리맞추기(J)/축척(S)/스타일(ST)]:";
				return;
			case "ST":
				{
					var doc = this.Ctx!.Doc.Document;
					var st = doc.MLineStyles.FirstOrDefault(x => x.Name.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));
					if (st != null) doc.Header.CurrentMLineStyleName = st.Name;
					else this.Ctx.Print($"다중선 스타일 \"{text.Trim()}\"을(를) 찾을 수 없습니다. ({string.Join(", ", doc.MLineStyles.Select(x => x.Name))})");
					this._await = null;
					this.Info();
					this.Prompt = "시작점 지정 또는 [자리맞추기(J)/축척(S)/스타일(ST)]:";
					return;
				}
		}
		if (this._pts.Count == 0)
		{
			switch (t)
			{
				case "J": this._await = "J"; this.Prompt = "자리맞추기 유형 입력 [위(T)/0(Z)/아래(B)]:"; return;
				case "S": this._await = "S"; this.Prompt = $"다중선의 축척 비율 <{this._scale:0.##}>:"; return;
				case "ST": this._await = "ST"; this.Prompt = "다중선 스타일 이름 입력 또는 [?]:"; return;
			}
			return;
		}
		if (t == "U")
		{
			this._pts.RemoveAt(this._pts.Count - 1);
			this.NextPrompt();
		}
		else if (t == "C" && this._pts.Count >= 3)
		{
			this.Create(closed: true);
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this._await != null) return;
		var p = new XY(x, y);
		if (this._pts.Count > 0 && this.Ctx.Ortho)
		{
			var o = CoordinateParser.ApplyOrtho((this._pts[^1].X, this._pts[^1].Y), (x, y));
			p = new XY(o.X, o.Y);
		}
		this._pts.Add(p);
		this.Ctx.LastPoint = (p.X, p.Y);
		this.NextPrompt();
	}

	private void NextPrompt()
	{
		this.Prompt = this._pts.Count switch
		{
			0 => "시작점 지정 또는 [자리맞추기(J)/축척(S)/스타일(ST)]:",
			1 or 2 => "다음 점 지정 또는 [명령 취소(U)]:",
			_ => "다음 점 지정 또는 [닫기(C)/명령 취소(U)]:",
		};
	}

	public override void OnEnter()
	{
		if (this._await != null)
		{
			this._await = null;
			this.NextPrompt();
			return;
		}
		if (this._pts.Count >= 2) this.Create(closed: false);
		this.Finish();
	}

	private void Create(bool closed)
	{
		var ml = Build(this._pts, closed, this.Style, this._scale, LastJust);
		if (ml != null) this.Ctx!.Doc.AddEntities(ml);
		this.Finish();
	}

	/// <summary>다중선 만들기: 꼭짓점마다 진행 방향·이음(마이터) 방향과 요소별 마이터 거리.</summary>
	public static MLine? Build(IReadOnlyList<XY> pts, bool closed, MLineStyle style, double scale, MLineJustification just)
	{
		int n = pts.Count;
		if (n < 2) return null;
		var offsets = style.Elements.Select(e => e.Offset).ToList();
		if (offsets.Count == 0) offsets = new List<double> { 0.5, -0.5 };
		double refOffset = just switch
		{
			MLineJustification.Top => offsets.Max(),
			MLineJustification.Bottom => offsets.Min(),
			_ => 0,
		};
		XY Dir(int i, int j)
		{
			double dx = pts[j].X - pts[i].X, dy = pts[j].Y - pts[i].Y, l = Math.Sqrt(dx * dx + dy * dy);
			return l < 1e-12 ? new XY(1, 0) : new XY(dx / l, dy / l);
		}
		var ml = new MLine
		{
			Style = style,
			ScaleFactor = scale,
			Justification = just,
			StartPoint = new XYZ(pts[0].X, pts[0].Y, 0),
			Flags = closed ? MLineFlags.HasVertices | MLineFlags.Closed : MLineFlags.HasVertices,
		};
		for (int i = 0; i < n; i++)
		{
			XY dOut = i < n - 1 ? Dir(i, i + 1) : closed ? Dir(i, 0) : Dir(i - 1, i);
			XY? dIn = i > 0 ? Dir(i - 1, i) : closed ? Dir(n - 1, 0) : null;
			var nOut = new XY(-dOut.Y, dOut.X);
			XY miter = nOut;
			if (dIn is { } di)
			{
				var nIn = new XY(-di.Y, di.X);
				double mx = nIn.X + nOut.X, my = nIn.Y + nOut.Y, ml2 = Math.Sqrt(mx * mx + my * my);
				if (ml2 > 1e-9) miter = new XY(mx / ml2, my / ml2);
			}
			double cos = miter.X * nOut.X + miter.Y * nOut.Y;
			if (Math.Abs(cos) < 1e-6) cos = 1e-6;
			var v = new MLine.Vertex
			{
				Position = new XYZ(pts[i].X, pts[i].Y, 0),
				Direction = new XYZ(dOut.X, dOut.Y, 0),
				Miter = new XYZ(miter.X, miter.Y, 0),
			};
			foreach (double off in offsets)
			{
				var seg = new MLine.Vertex.Segment();
				seg.Parameters.Add((off - refOffset) * scale / cos);
				seg.Parameters.Add(0);
				v.Segments.Add(seg);
			}
			ml.Vertices.Add(v);
		}
		return ml;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._pts.Count == 0 || this.Ctx == null || this._await != null) yield break;
		var pts = this._pts.Append(new XY(cursorX, cursorY)).ToList();
		// 미리보기는 요소 선마다 폴리선으로 (렌더러와 같은 계산)
		if (Build(pts, false, this.Style, this._scale, LastJust) is { } ml)
		{
			foreach (var pl in ToPolylines(ml)) yield return pl;
		}
	}

	/// <summary>다중선 요소마다 꼭짓점 + 마이터 × 거리로 이은 폴리선 (화면 미리보기·분해용).</summary>
	public static List<LwPolyline> ToPolylines(MLine ml)
	{
		var list = new List<LwPolyline>();
		if (ml.Vertices.Count == 0) return list;
		int lines = ml.Vertices[0].Segments.Count;
		bool closed = ml.Flags.HasFlag(MLineFlags.Closed);
		for (int k = 0; k < lines; k++)
		{
			var pl = new LwPolyline { IsClosed = closed };
			foreach (var v in ml.Vertices)
			{
				double d = k < v.Segments.Count && v.Segments[k].Parameters.Count > 0 ? v.Segments[k].Parameters[0] : 0;
				pl.Vertices.Add(new LwPolyline.Vertex(new XY(v.Position.X + v.Miter.X * d, v.Position.Y + v.Miter.Y * d)));
			}
			list.Add(pl);
		}
		return list;
	}
}

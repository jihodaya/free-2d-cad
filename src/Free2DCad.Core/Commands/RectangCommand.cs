using System.Globalization;
using ACadSharp.Entities;
using CSMath;

namespace Free2DCad.Core.Commands;

/// <summary>
/// RECTANG (기존 CAD와 같은 문구·옵션):
/// "[모따기(C)/높이(E)/모깎기(F)/회전(R)/정사각형(S)/두께(T)/너비(W)]/&lt;사각형의 첫번째 모서리 선택&gt;:" →
/// "다른 구석 지점 지정 또는 [면적(A)/치수(D)/회전(R)]:". 모따기·모깎기·높이·두께·너비는 "모든 사각형에" 적용(다음 명령에도 유지).
/// 회전(R)·정사각형(S)은 이번 명령에서만 켜지는 방식: 회전 = 다른 구석 → 회전각도, 정사각형 = 한 변의 두 번째 구석.
/// </summary>
public sealed class RectangCommand : EditorCommand
{
	/// <summary>"모든 사각형에" 적용되는 설정 (도면마다, 명령을 다시 실행해도 유지).</summary>
	private sealed class Settings
	{
		public double Chamfer1, Chamfer2, Fillet, Elevation, Thickness, Width;
		public double LastLength = 10, LastWidth = 10, LastArea = 100, LastRotation;
	}

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ACadSharp.CadDocument, Settings> AllSettings = new();

	private Settings _s = new();

	private enum Mode { Normal, Rotated, Square }

	private Mode _mode;
	private string? _await;
	private int _step; // 0 첫 구석, 1 다른 구석
	private (double X, double Y) _p1;
	private (double X, double Y) _p2;
	private double _rotationDeg;
	private double? _fixedLength, _fixedWidth;
	private bool _areaByWidth;

	public override string Name => "RECTANG";
	public override string[] Aliases => new[] { "REC" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._s = AllSettings.GetValue(ctx.Doc.Document, _ => new Settings());
		this._mode = Mode.Normal;
		this._rotationDeg = 0;
		this.FirstPrompt();
	}

	private static string F(double v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

	private void FirstPrompt()
	{
		this._step = 0;
		this._await = null;
		string modes = this._mode switch
		{
			Mode.Rotated => "정사각형(S)/",
			Mode.Square => "회전(R)/",
			_ => "회전(R)/정사각형(S)/",
		};
		this.Prompt = $"[모따기(C)/높이(E)/모깎기(F)/{modes}두께(T)/너비(W)]/<사각형의 첫번째 모서리 선택>:";
	}

	private void SecondPrompt()
	{
		this._step = 1;
		this._await = null;
		this.Prompt = this._mode switch
		{
			Mode.Square => "사각형의 두번째 구석:",
			Mode.Rotated => "직사각형의 다른 구석:",
			_ => "다른 구석 지점 지정 또는 [면적(A)/치수(D)/회전(R)]:",
		};
	}

	private void Ask(string key, string prompt)
	{
		this._await = key;
		this.Prompt = prompt;
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		bool num = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);

		if (this._await != null)
		{
			this.OnValue(t, num, v);
			return;
		}

		if (this._step == 0)
		{
			switch (t)
			{
				case "C": this.Ask("C1", $"[기본값설정(D)]/사각형에사용할첫모따기거리(F)<{F(this._s.Chamfer1)}>:"); return;
				case "F": this.Ask("F", $"모든 사각형에 사용할 모깎기 거리(F) <{F(this._s.Fillet)}>:"); return;
				case "E": this.Ask("E", "모든 사각형에 사용할 높이:"); return;
				case "T": this.Ask("T", "모든 사각형에 사용할 두께:"); return;
				case "W": this.Ask("W", "모든 사각형에 사용할 너비:"); return;
				case "R" when this._mode != Mode.Rotated: this._mode = Mode.Rotated; this.FirstPrompt(); return;
				case "S" when this._mode != Mode.Square: this._mode = Mode.Square; this.FirstPrompt(); return;
			}
			return;
		}

		if (this._step == 1 && this._mode == Mode.Normal)
		{
			switch (t)
			{
				case "A": this.Ask("A", $"현재 단위로 직사각형의 면적 입력 <{F(this._s.LastArea)}>:"); return;
				case "D": this.Ask("DL", $"직사각형 길이 입력 <{F(this._s.LastLength)}>:"); return;
				case "R": this.Ask("R", $"회전 각도 지정 또는 [점선택(P)] <{this._s.LastRotation:0.####}>:"); return;
			}
		}
	}

	private void OnValue(string t, bool num, double v)
	{
		switch (this._await)
		{
			case "C1" when t == "D":
				this._s.Chamfer1 = this._s.Chamfer2 = 0;
				this.FirstPrompt();
				return;
			case "C1" when num && v >= 0:
				this._s.Chamfer1 = v;
				this.Ask("C2", $"모든 직사각형에 사용할 두번째 모따기 거리(S) <{F(v)}>:");
				return;
			case "C2" when num && v >= 0:
				this._s.Chamfer2 = v;
				this._s.Fillet = 0;
				this.FirstPrompt();
				return;
			case "F" when num && v >= 0:
				this._s.Fillet = v;
				this._s.Chamfer1 = this._s.Chamfer2 = 0;
				this.FirstPrompt();
				return;
			case "E" when num:
				this._s.Elevation = v;
				this.FirstPrompt();
				return;
			case "T" when num:
				this._s.Thickness = v;
				this.FirstPrompt();
				return;
			case "W" when num && v >= 0:
				this._s.Width = v;
				this.FirstPrompt();
				return;
			case "A" when num && v > 0:
				this._s.LastArea = v;
				this.Ask("AB", "[길이(L)/너비(W)]를 기반으로 직사각형 치수 계산 <길이>:");
				return;
			case "AB" when t is "L" or "W":
				this._areaByWidth = t == "W";
				this.AskAreaSide();
				return;
			case "AL" when num && v > 0:
				if (this._areaByWidth) { this._s.LastWidth = v; this._fixedWidth = v; this._fixedLength = this._s.LastArea / v; }
				else { this._s.LastLength = v; this._fixedLength = v; this._fixedWidth = this._s.LastArea / v; }
				this.Ask("SIDE", "면 선택:");
				return;
			case "DL" when num && v > 0:
				this._s.LastLength = v;
				this.Ask("DW", $"직사각형 너비 입력 <{F(this._s.LastWidth)}>:");
				return;
			case "DW" when num && v > 0:
				this._s.LastWidth = v;
				this._fixedLength = this._s.LastLength;
				this._fixedWidth = v;
				this.SecondPrompt();
				return;
			case "R" when t == "P":
				this.Ask("RP1", "첫번째 점:");
				return;
			case "R" when num:
				this._rotationDeg = this._s.LastRotation = v;
				this.SecondPrompt();
				return;
			case "RA" when num:
				this._s.LastRotation = v;
				this.Create(this.RotatedCorners(this._p1, this._p2, v));
				return;
		}
	}

	private void AskAreaSide()
	{
		double def = this._areaByWidth ? this._s.LastWidth : this._s.LastLength;
		this.Ask("AL", this._areaByWidth ? $"직사각형 너비 입력 <{F(def)}>:" : $"직사각형 길이 입력 <{F(def)}>:");
	}

	public override void OnEnter()
	{
		if (this.Ctx == null) return;
		switch (this._await)
		{
			case "C1": this.OnValue(F(this._s.Chamfer1), true, this._s.Chamfer1); return;
			case "C2": this.OnValue(F(this._s.Chamfer1), true, this._s.Chamfer1); return;
			case "F": this.OnValue(F(this._s.Fillet), true, this._s.Fillet); return;
			case "A": this.OnValue(F(this._s.LastArea), true, this._s.LastArea); return;
			case "AB": this._areaByWidth = false; this.AskAreaSide(); return;
			case "AL": { double d = this._areaByWidth ? this._s.LastWidth : this._s.LastLength; this.OnValue(F(d), true, d); return; }
			case "DL": this.OnValue(F(this._s.LastLength), true, this._s.LastLength); return;
			case "DW": this.OnValue(F(this._s.LastWidth), true, this._s.LastWidth); return;
			case "R": this.OnValue("", true, this._s.LastRotation); return;
			case "RA": this.OnValue("", true, this._s.LastRotation); return;
			case "E" or "T" or "W": this.FirstPrompt(); return;
		}
		if (this._step == 0)
		{
			this.Finish();
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		switch (this._await)
		{
			case "SIDE":
				this.Create(this.CornersTo(x, y));
				return;
			case "RA":
				this.OnValue("", true, Math.Atan2(y - this._p1.Y, x - this._p1.X) * 180 / Math.PI);
				return;
			case "R":
				this.OnValue("", true, Math.Atan2(y - this._p1.Y, x - this._p1.X) * 180 / Math.PI);
				return;
			case "RP1":
				this._p2 = (x, y);
				this.Ask("RP2", "두번째 점:");
				return;
			case "RP2":
				this._await = "R";
				this.OnValue("", true, Math.Atan2(y - this._p2.Y, x - this._p2.X) * 180 / Math.PI);
				return;
			case not null:
				return;
		}
		if (this._step == 0)
		{
			this._p1 = (x, y);
			this.Ctx.LastPoint = (x, y);
			this.SecondPrompt();
			return;
		}
		switch (this._mode)
		{
			case Mode.Square:
				this.Create(SquareCorners(this._p1, (x, y)));
				return;
			case Mode.Rotated:
				this._p2 = (x, y);
				this.Ask("RA", $"직사각형의 회전각도(R) <{this._s.LastRotation:0.####}>:");
				return;
			default:
				this.Create(this.CornersTo(x, y));
				return;
		}
	}

	/// <summary>보통 사각형: 회전각만큼 돌린 축으로 첫 구석 → 커서(치수·면적을 정했으면 그 크기, 커서는 방향만).</summary>
	private (double X, double Y)[] CornersTo(double x, double y)
	{
		double a = this._rotationDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
		double dx = x - this._p1.X, dy = y - this._p1.Y;
		double u = dx * c + dy * s, w = -dx * s + dy * c;
		if (this._fixedLength is { } fl && this._fixedWidth is { } fw)
		{
			u = u < 0 ? -fl : fl;
			w = w < 0 ? -fw : fw;
		}
		var p = this._p1;
		return new[]
		{
			p,
			(p.X + u * c, p.Y + u * s),
			(p.X + u * c - w * s, p.Y + u * s + w * c),
			(p.X - w * s, p.Y + w * c),
		};
	}

	/// <summary>회전 사각형: 대각선 두 구석과 변의 각도.</summary>
	private (double X, double Y)[] RotatedCorners((double X, double Y) p1, (double X, double Y) p2, double deg)
	{
		this._rotationDeg = deg;
		this._fixedLength = this._fixedWidth = null;
		return this.CornersTo(p2.X, p2.Y);
	}

	/// <summary>정사각형: 첫 구석 → 두 번째 구석이 한 변, 나머지는 왼쪽(반시계)으로.</summary>
	private static (double X, double Y)[] SquareCorners((double X, double Y) a, (double X, double Y) b)
	{
		double dx = b.X - a.X, dy = b.Y - a.Y;
		return new[] { a, b, (b.X - dy, b.Y + dx), (a.X - dy, a.Y + dx) };
	}

	private void Create((double X, double Y)[] corners)
	{
		if (Math.Abs(corners[0].X - corners[2].X) + Math.Abs(corners[0].Y - corners[2].Y) < 1e-12) return;
		this.Ctx!.Doc.AddEntities(this.Build(corners));
		this.Ctx.LastPoint = corners[2];
		this.Finish();
	}

	/// <summary>네 구석 → 닫힌 폴리선 (모따기·모깎기·너비·높이·두께 적용). 변보다 큰 모따기·모깎기는 무시.</summary>
	private LwPolyline Build((double X, double Y)[] c)
	{
		var pl = new LwPolyline { IsClosed = true, ConstantWidth = this._s.Width, Elevation = this._s.Elevation, Thickness = this._s.Thickness };
		double side1 = Dist(c[0], c[1]), side2 = Dist(c[1], c[2]);
		double minSide = Math.Min(side1, side2);
		bool fillet = this._s.Fillet > 1e-12 && this._s.Fillet * 2 <= minSide + 1e-12;
		bool chamfer = !fillet && (this._s.Chamfer1 > 1e-12 || this._s.Chamfer2 > 1e-12) && this._s.Chamfer1 + this._s.Chamfer2 <= minSide + 1e-12;
		// 방향(반시계/시계)에 맞춘 모깎기 볼록 값
		double cross = (c[1].X - c[0].X) * (c[2].Y - c[1].Y) - (c[1].Y - c[0].Y) * (c[2].X - c[1].X);
		double bulge = Math.Tan(Math.PI / 8) * (cross >= 0 ? 1 : -1);
		for (int i = 0; i < 4; i++)
		{
			var prev = c[(i + 3) % 4];
			var cur = c[i];
			var next = c[(i + 1) % 4];
			if (fillet || chamfer)
			{
				double d1 = fillet ? this._s.Fillet : this._s.Chamfer1, d2 = fillet ? this._s.Fillet : this._s.Chamfer2;
				var a = Toward(cur, prev, i % 2 == 0 ? d2 : d1);
				var b = Toward(cur, next, i % 2 == 0 ? d1 : d2);
				pl.Vertices.Add(new LwPolyline.Vertex(new XY(a.X, a.Y)) { Bulge = fillet ? bulge : 0 });
				pl.Vertices.Add(new LwPolyline.Vertex(new XY(b.X, b.Y)));
			}
			else
			{
				pl.Vertices.Add(new LwPolyline.Vertex(new XY(cur.X, cur.Y)));
			}
		}
		return pl;
	}

	private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

	private static (double X, double Y) Toward((double X, double Y) from, (double X, double Y) to, double d)
	{
		double len = Dist(from, to);
		return len < 1e-12 ? from : (from.X + (to.X - from.X) / len * d, from.Y + (to.Y - from.Y) / len * d);
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step != 1 || this.Ctx == null) yield break;
		(double X, double Y)[]? corners = this._await switch
		{
			null when this._mode == Mode.Square => SquareCorners(this._p1, (cursorX, cursorY)),
			null when this._mode == Mode.Normal => this.CornersTo(cursorX, cursorY),
			"SIDE" => this.CornersTo(cursorX, cursorY),
			_ => null,
		};
		if (corners != null && Math.Abs(corners[0].X - corners[2].X) + Math.Abs(corners[0].Y - corners[2].Y) > 1e-12)
		{
			yield return this.Build(corners);
		}
	}
}

using ACadSharp.Entities;
using CSMath;

namespace Free2DCad.Core.Commands;

/// <summary>
/// LINE 명령 (기존 CAD와 같은 문구·옵션): "첫번째 점 지정 or Enter키로 마지막 점 지정 [따라가기(F)]:" →
/// "[각도(A)/길이(L)]/&lt;끝점&gt;:" → "[각도(A)/길이(L)/따라가기(F)/취소(U)]/&lt;끝점&gt;:" →
/// 두 선분부터 "[각도(A)/길이(L)/따라가기(F)/닫기(C)/취소(U)]/&lt;다음점 지정&gt;:".
/// 각도(A): "선의 각도:" → "선 길이:", 길이(L): "선 길이:" → "선의 각도:", 따라가기(F): 앞 선의 방향으로 "선 길이:".
/// </summary>
public sealed class LineCommand : EditorCommand
{
	private enum Ask { Point, AngleThenLength, Length, LengthThenAngle, Angle, FollowLength }

	private int _step;
	private Ask _ask;
	private double _pendingAngle, _pendingLength;
	private (double X, double Y) _firstPt;
	private (double X, double Y) _currentStart;
	private readonly List<Line> _addedLines = new();

	public override string Name => "LINE";
	public override string[] Aliases => new[] { "L" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._ask = Ask.Point;
		this._addedLines.Clear();
		this.Prompt = "첫번째 점 지정 or Enter키로 마지막 점 지정 [따라가기(F)]:";
	}

	private void UpdatePrompt()
	{
		this.Prompt = this._ask switch
		{
			Ask.AngleThenLength or Ask.Angle => "선의 각도:",
			Ask.Length or Ask.LengthThenAngle or Ask.FollowLength => "선 길이:",
			_ => this._addedLines.Count switch
			{
				0 => "[각도(A)/길이(L)]/<끝점>:",
				1 => "[각도(A)/길이(L)/따라가기(F)/취소(U)]/<끝점>:",
				_ => "[각도(A)/길이(L)/따라가기(F)/닫기(C)/취소(U)]/<다음점 지정>:",
			},
		};
	}

	private void BeginAt((double X, double Y) p)
	{
		this._firstPt = p;
		this._currentStart = p;
		this.Ctx!.LastPoint = p;
		this._step = 1;
	}

	/// <summary>따라가기 방향: 이 명령의 마지막 선, 없으면 도면에 마지막으로 그린 선(끝점 방향).</summary>
	private double? FollowAngle()
	{
		Line? last = this._addedLines.Count > 0 ? this._addedLines[^1] : this.Ctx?.Doc.CurrentSpace.Entities.OfType<Line>().LastOrDefault();
		if (last == null)
		{
			return null;
		}
		double dx = last.EndPoint.X - last.StartPoint.X, dy = last.EndPoint.Y - last.StartPoint.Y;
		return Math.Abs(dx) + Math.Abs(dy) < 1e-12 ? null : Math.Atan2(dy, dx);
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;

		if (this._step == 0)
		{
			this.BeginAt((x, y));
			this.UpdatePrompt();
			return;
		}

		// 각도·길이를 묻는 중에 점을 찍으면 그 점까지의 각도·거리
		double dxp = x - this._currentStart.X, dyp = y - this._currentStart.Y;
		switch (this._ask)
		{
			case Ask.AngleThenLength:
			case Ask.Angle:
			case Ask.Length:
			case Ask.LengthThenAngle:
			case Ask.FollowLength:
				this.OnText(this._ask is Ask.AngleThenLength or Ask.Angle
					? (Math.Atan2(dyp, dxp) * 180 / Math.PI).ToString(System.Globalization.CultureInfo.InvariantCulture)
					: Math.Sqrt(dxp * dxp + dyp * dyp).ToString(System.Globalization.CultureInfo.InvariantCulture));
				return;
		}

		(double X, double Y) endPt = (x, y);
		if (this.Ctx.Ortho)
		{
			endPt = CoordinateParser.ApplyOrtho(this._currentStart, endPt);
		}
		this.AddSegment(endPt);
	}

	private void AddSegment((double X, double Y) endPt)
	{
		var line = new Line(new XYZ(this._currentStart.X, this._currentStart.Y, 0), new XYZ(endPt.X, endPt.Y, 0));
		this.Ctx!.Doc.AddEntities(line);
		this._addedLines.Add(line);
		this._currentStart = endPt;
		this.Ctx.LastPoint = endPt;
		this._ask = Ask.Point;
		this.UpdatePrompt();
	}

	private void AddPolar(double angleRad, double length) =>
		this.AddSegment((this._currentStart.X + length * Math.Cos(angleRad), this._currentStart.Y + length * Math.Sin(angleRad)));

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();

		if (this._step == 0)
		{
			if (t == "F" && this.Ctx.LastPoint is { } lp && this.FollowAngle() != null)
			{
				this.BeginAt(lp);
				this._ask = Ask.FollowLength;
				this.UpdatePrompt();
			}
			return;
		}

		bool isNum = double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v);
		switch (this._ask)
		{
			case Ask.AngleThenLength:
			case Ask.Angle:
				if (!isNum)
				{
					this.Ctx.Print("유효한 각도를 입력해 주세요.");
					return;
				}
				if (this._ask == Ask.Angle)
				{
					this.AddPolar(v * Math.PI / 180, this._pendingLength);
				}
				else
				{
					this._pendingAngle = v * Math.PI / 180;
					this._ask = Ask.Length;
					this.UpdatePrompt();
				}
				return;
			case Ask.Length:
			case Ask.LengthThenAngle:
			case Ask.FollowLength:
				if (!isNum)
				{
					this.Ctx.Print("유효한 거리를 입력해 주세요.");
					return;
				}
				if (this._ask == Ask.Length)
				{
					this.AddPolar(this._pendingAngle, v);
				}
				else if (this._ask == Ask.FollowLength)
				{
					this.AddPolar(this.FollowAngle() ?? 0, v);
				}
				else
				{
					this._pendingLength = v;
					this._ask = Ask.Angle;
					this.UpdatePrompt();
				}
				return;
		}

		switch (t)
		{
			case "A":
				this._ask = Ask.AngleThenLength;
				this.UpdatePrompt();
				break;
			case "L":
				this._ask = Ask.LengthThenAngle;
				this.UpdatePrompt();
				break;
			case "F" when this.FollowAngle() != null:
				this._ask = Ask.FollowLength;
				this.UpdatePrompt();
				break;
			case "C" when this._addedLines.Count >= 2:
				var line = new Line(new XYZ(this._currentStart.X, this._currentStart.Y, 0), new XYZ(this._firstPt.X, this._firstPt.Y, 0));
				this.Ctx.Doc.AddEntities(line);
				this._addedLines.Add(line);
				this.Ctx.LastPoint = this._firstPt;
				this.Finish();
				break;
			case "U" when this._addedLines.Count > 0:
				var lastLine = this._addedLines[^1];
				this._addedLines.RemoveAt(this._addedLines.Count - 1);
				this.Ctx.Doc.Erase(new[] { lastLine });
				this._currentStart = this._addedLines.Count > 0 ? (this._addedLines[^1].EndPoint.X, this._addedLines[^1].EndPoint.Y) : this._firstPt;
				this.Ctx.LastPoint = this._currentStart;
				this.UpdatePrompt();
				break;
		}
	}

	public override void OnEnter()
	{
		if (this.Ctx == null) return;
		if (this._step == 0)
		{
			// Enter: 마지막 점에서 시작 (마지막 점이 없으면 그대로 기다림)
			if (this.Ctx.LastPoint is { } lp)
			{
				this.BeginAt(lp);
				this.UpdatePrompt();
			}
			return;
		}
		if (this._ask != Ask.Point)
		{
			this._ask = Ask.Point;
			this.UpdatePrompt();
			return;
		}
		this.Finish();
	}

	/// <summary>Esc: 기존 CAD처럼 이미 그린 선은 남기고 끝낸다 (전에는 그린 선까지 모두 취소됐다).</summary>
	public override void OnCancel()
	{
		if (this._addedLines.Count > 0)
		{
			this.Finish();
		}
		else
		{
			base.OnCancel();
		}
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step == 1 && this.Ctx != null && this._ask == Ask.Point)
		{
			(double X, double Y) pt = (cursorX, cursorY);
			if (this.Ctx.Ortho)
			{
				pt = CoordinateParser.ApplyOrtho(this._currentStart, pt);
			}
			yield return new Line(new XYZ(this._currentStart.X, this._currentStart.Y, 0), new XYZ(pt.X, pt.Y, 0));
		}
	}
}

/// <summary>
/// TEXT (기존 CAD와 같은 문구·흐름): "텍스트의 시작점을 지정 or [정렬(J)/Style]:" → "문자의 높이 &lt;2.5000&gt;:" →
/// "문자의 회전 각도 &lt;0&gt;:" → 그 자리에서 바로 입력 (앱이 Created를 받아 화면 편집을 연다. Enter = 다음 줄, 빈 줄에서 Enter = 끝).
/// 정렬(J): "옵션을 입력하세요 [왼쪽(L)/중앙(C)/…/하단_오른쪽(BR)]:" → "문자의 맨위 왼쪽 점:" 등.
/// 맞춤(F)·Align(A): "문자 시작점:" → "문자 끝점:" (맞춤은 높이를 묻고 회전은 두 점 방향, Align은 높이도 두 점으로).
/// </summary>
public sealed class TextCommand : EditorCommand
{
	private const string JustifyPrompt = "옵션을 입력하세요 [왼쪽(L)/중앙(C)/오른쪽(R)/Align(A)/중간(M)/맞춤(F)/상단_왼쪽(TL)/상단_중앙(TC)/상단_오른쪽(TR)/중간_왼쪽(ML)/중간_중앙(MC)/중간_오른쪽(MR)/하단_왼쪽(BL)/하단_중앙(BC)/하단_오른쪽(BR)]:";

	/// <summary>정렬 코드 → (가로, 세로, 점을 묻는 문구).</summary>
	private static readonly Dictionary<string, (TextHorizontalAlignment H, TextVerticalAlignmentType V, string Ask)> Justify = new()
	{
		["L"] = (TextHorizontalAlignment.Left, TextVerticalAlignmentType.Baseline, "텍스트의 시작점을 지정 or [정렬(J)/Style]:"),
		["C"] = (TextHorizontalAlignment.Center, TextVerticalAlignmentType.Baseline, "문자의 중심점:"),
		["R"] = (TextHorizontalAlignment.Right, TextVerticalAlignmentType.Baseline, "문자의 오른쪽 점:"),
		["M"] = (TextHorizontalAlignment.Middle, TextVerticalAlignmentType.Baseline, "문자의 중간점:"),
		["TL"] = (TextHorizontalAlignment.Left, TextVerticalAlignmentType.Top, "문자의 맨위 왼쪽 점:"),
		["TC"] = (TextHorizontalAlignment.Center, TextVerticalAlignmentType.Top, "문자의 맨위 중심 점:"),
		["TR"] = (TextHorizontalAlignment.Right, TextVerticalAlignmentType.Top, "문자의 맨위 오른쪽 점:"),
		["ML"] = (TextHorizontalAlignment.Left, TextVerticalAlignmentType.Middle, "문자의 중간 왼쪽 점:"),
		["MC"] = (TextHorizontalAlignment.Center, TextVerticalAlignmentType.Middle, "문자의 중간 중심 점:"),
		["MR"] = (TextHorizontalAlignment.Right, TextVerticalAlignmentType.Middle, "문자의 중간 오른쪽 점:"),
		["BL"] = (TextHorizontalAlignment.Left, TextVerticalAlignmentType.Bottom, "문자의 맨 아래 왼쪽 점:"),
		["BC"] = (TextHorizontalAlignment.Center, TextVerticalAlignmentType.Bottom, "문자의 맨 아래 중심 점:"),
		["BR"] = (TextHorizontalAlignment.Right, TextVerticalAlignmentType.Bottom, "문자의 맨 아래 오른쪽 점:"),
	};

	private string _await = "start"; // start, justify, style, second, height, rotation
	private (double X, double Y) _insertPt, _endPt;
	private TextHorizontalAlignment _h = TextHorizontalAlignment.Left;
	private TextVerticalAlignmentType _v = TextVerticalAlignmentType.Baseline;
	private ACadSharp.Tables.TextStyle? _style;
	private double _height = 2.5;
	private double _angleDeg;
	private double _scale = 1.0;

	public override string Name => "TEXT";
	public override string[] Aliases => new[] { "DT", "T" };

	/// <summary>만든 빈 문자 (앱이 그 자리 편집을 연다).</summary>
	public TextEntity? Created { get; private set; }

	private static string F4(double v) => v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this.Created = null;
		this._await = "start";
		// 기본 높이는 출력 높이 × 주석 축척
		this._scale = Free2DCad.Core.Styles.AnnotationScale.Get(ctx.Doc.Document);
		// 직전 높이는 도면마다 (TEXTSIZE, 기존 CAD와 같음). 인치 기본값(0.2)이 남아 있으면 2.5
		double size = ctx.Doc.Document.Header.TextHeightDefault;
		this._height = (size >= 0.5 ? size : 2.5) * this._scale;
		this._angleDeg = 0.0;
		this._style = ctx.Doc.Document.Header.CurrentTextStyle;
		this.Prompt = Justify["L"].Ask;
	}

	private bool FixedHeight => this._style != null && this._style.Height > 0;

	private void AskHeightOrRotation()
	{
		if (this._h == TextHorizontalAlignment.Aligned)
		{
			this.Create(); // 높이·회전 모두 두 점으로
			return;
		}
		if (this.FixedHeight)
		{
			this._height = this._style!.Height;
			this.AskRotation();
			return;
		}
		this._await = "height";
		this.Prompt = $"문자의 높이 <{F4(this._height)}>:";
	}

	private void AskRotation()
	{
		if (this._h == TextHorizontalAlignment.Fit)
		{
			this.Create(); // 회전은 두 점 방향
			return;
		}
		this._await = "rotation";
		this.Prompt = $"문자의 회전 각도 <{this._angleDeg:0.####}>:";
	}

	private void SetHeight(double h)
	{
		if (h > 1e-9)
		{
			this._height = h;
			this.Ctx!.Doc.Document.Header.TextHeightDefault = h / this._scale;
		}
		this.AskRotation();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		double dx = x - this._insertPt.X, dy = y - this._insertPt.Y;
		switch (this._await)
		{
			case "start":
				this._insertPt = (x, y);
				this.Ctx.LastPoint = (x, y);
				if (this._h is TextHorizontalAlignment.Aligned or TextHorizontalAlignment.Fit)
				{
					this._await = "second";
					this.Prompt = "문자 끝점:";
					return;
				}
				this.AskHeightOrRotation();
				return;
			case "second":
				this._endPt = (x, y);
				this.AskHeightOrRotation();
				return;
			case "height":
				this.SetHeight(Math.Sqrt(dx * dx + dy * dy));
				return;
			case "rotation":
				this._angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
				this.Create();
				return;
		}
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim();
		string u = t.ToUpperInvariant();
		bool num = double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v);
		switch (this._await)
		{
			case "start" when u == "J":
				this._await = "justify";
				this.Prompt = JustifyPrompt;
				return;
			case "start" when u is "S" or "STYLE":
				this._await = "style";
				this.Prompt = $"사용할 문자 스타일 (또는 [?]) <{this._style?.Name ?? "Standard"}>:";
				return;
			case "start" when Justify.ContainsKey(u):
				this.SetJustify(u);
				return;
			case "justify":
				if (u is "A" or "ALIGN" or "F" or "FIT")
				{
					this._h = u.StartsWith("A") ? TextHorizontalAlignment.Aligned : TextHorizontalAlignment.Fit;
					this._v = TextVerticalAlignmentType.Baseline;
					this._await = "start";
					this.Prompt = "문자 시작점:";
				}
				else if (Justify.ContainsKey(u))
				{
					this.SetJustify(u);
				}
				else
				{
					this.Ctx.Print("항목을 인식할 수 없습니다. 다시 시도하십시오.");
				}
				return;
			case "style":
				if (u == "?")
				{
					foreach (var st in this.Ctx.Doc.Document.TextStyles.Where(s => !s.IsShapeFile))
					{
						this.Ctx.Print($"{st.Name}: {st.Filename}");
					}
					return;
				}
				var found = this.Ctx.Doc.Document.TextStyles.FirstOrDefault(s => !s.IsShapeFile && string.Equals(s.Name, t, StringComparison.OrdinalIgnoreCase));
				if (found == null)
				{
					this.Ctx.Print($"\"{t}\" 문자 스타일을 찾을 수 없습니다.");
					return;
				}
				this._style = found;
				this.ToStart();
				return;
			case "height" when num && v > 0:
				this.SetHeight(v);
				return;
			case "rotation" when num:
				this._angleDeg = v;
				this.Create();
				return;
		}
	}

	private void SetJustify(string code)
	{
		var (h, vv, ask) = Justify[code];
		this._h = h;
		this._v = vv;
		this._await = "start";
		this.Prompt = ask;
	}

	private void ToStart()
	{
		this._await = "start";
		this.Prompt = Justify["L"].Ask;
	}

	public override void OnEnter()
	{
		switch (this._await)
		{
			case "height":
				this.SetHeight(this._height);
				return;
			case "rotation":
				this.Create();
				return;
			case "justify":
			case "style":
				this.ToStart();
				return;
		}
		this.Finish();
	}

	private void Create()
	{
		var p = new XYZ(this._insertPt.X, this._insertPt.Y, 0);
		var text = new TextEntity
		{
			InsertPoint = p,
			AlignmentPoint = p,
			Height = this._height,
			Rotation = this._angleDeg * Math.PI / 180.0,
			HorizontalAlignment = this._h,
			VerticalAlignment = this._v,
			Value = string.Empty,
		};
		if (this._style != null)
		{
			text.Style = this._style;
			if (this._style.Width > 0) text.WidthFactor = this._style.Width;
			text.ObliqueAngle = this._style.ObliqueAngle;
		}
		if (this._h is TextHorizontalAlignment.Aligned or TextHorizontalAlignment.Fit)
		{
			text.AlignmentPoint = new XYZ(this._endPt.X, this._endPt.Y, 0);
			text.Rotation = Math.Atan2(this._endPt.Y - this._insertPt.Y, this._endPt.X - this._insertPt.X);
		}
		this.Ctx!.Doc.AddEntities(text);
		this.Created = text;
		this.Finish();
	}

	/// <summary>다음 줄 문자 (같은 정렬·높이·회전, 한 줄 아래). DTEXT처럼 Enter로 이어 쓸 때.</summary>
	public static TextEntity NextLine(TextEntity prev)
	{
		double step = prev.Height * 5.0 / 3.0;
		double a = prev.Rotation;
		double dx = Math.Sin(a) * step, dy = -Math.Cos(a) * step;
		var next = (TextEntity)prev.Clone();
		next.Value = string.Empty;
		next.InsertPoint = new XYZ(prev.InsertPoint.X + dx, prev.InsertPoint.Y + dy, prev.InsertPoint.Z);
		next.AlignmentPoint = new XYZ(prev.AlignmentPoint.X + dx, prev.AlignmentPoint.Y + dy, prev.AlignmentPoint.Z);
		return next;
	}
}

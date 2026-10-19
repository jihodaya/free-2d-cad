using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;

namespace Free2DCad.Core.Commands;

/// <summary>
/// MULTILEADER 객체 만들기·문자 바꾸기 (기존 CAD가 만든 객체와 같은 구조).
/// 화살촉 점 → (중간 점) → 연결점, 연결점에서 착륙선(LandingDistance) 끝에 문자. 왼쪽으로 가면 문자를 오른쪽 정렬.
/// </summary>
public static class MLeaderBuilder
{
	/// <summary>기존 CAD 미터 도면의 Standard 다중지시선 스타일 값.</summary>
	public const double MetricTextHeight = 4, MetricArrowSize = 4, MetricLanding = 8, MetricGap = 2;

	/// <summary>현재(없으면 Standard) 다중지시선 스타일. 인치 기본값(0.18)이면 기존 CAD 미터 값으로 맞춘다.</summary>
	public static MultiLeaderStyle CurrentStyle(CadDocument doc)
	{
		var style = doc.MLeaderStyles.FirstOrDefault(s => s.Name.Equals(MultiLeaderStyle.DefaultName, StringComparison.OrdinalIgnoreCase))
			?? doc.MLeaderStyles.FirstOrDefault();
		if (style == null)
		{
			style = new MultiLeaderStyle(MultiLeaderStyle.DefaultName);
			doc.MLeaderStyles.Add(style);
		}
		// 인치 기본값(문자 0.18, 착륙 0.36, 간격 0.09)이 남은 값만 미터 값으로 (원본 템플릿은 착륙 거리만 0.36이었음)
		if (style.TextHeight < 0.5) style.TextHeight = MetricTextHeight;
		if (style.ArrowheadSize < 0.5) style.ArrowheadSize = MetricArrowSize;
		if (style.LandingDistance < 0.5) style.LandingDistance = MetricLanding;
		if (style.LandingGap < 0.2) style.LandingGap = MetricGap;
		return style;
	}

	public static MultiLeader Create(CadDocument doc, IReadOnlyList<XY> pts, string text, double scale = 1)
	{
		var style = CurrentStyle(doc);
		var arrow = pts[0];
		var conn = pts[^1];
		var prev = pts[^2];
		int dir = conn.X >= prev.X ? 1 : -1;
		var textStyle = style.TextStyle ?? doc.TextStyles.FirstOrDefault() ?? TextStyle.Default;

		var ml = new MultiLeader
		{
			Style = style,
			PathType = MultiLeaderPathType.StraightLineSegments,
			ContentType = LeaderContentType.MText,
			PropertyOverrideFlags = MultiLeaderPropertyOverrideFlags.ContentType | MultiLeaderPropertyOverrideFlags.TextAlignment | MultiLeaderPropertyOverrideFlags.EnableUseDefaultMText,
			LineColor = Color.ByBlock,
			LeaderLineWeight = LineWeightType.ByBlock,
			ArrowheadSize = style.ArrowheadSize,
			LandingDistance = style.LandingDistance,
			EnableDogleg = true,
			EnableLanding = true,
			ScaleFactor = scale,
			TextStyle = textStyle,
			TextColor = Color.ByBlock,
			TextAngle = TextAngleType.Horizontal,
			TextAttachmentDirection = TextAttachmentDirectionType.Horizontal,
			TextAttachmentPoint = TextAttachmentPointType.Left,
			TextAlignment = dir > 0 ? TextAlignmentType.Left : TextAlignmentType.Right,
			TextLeftAttachment = TextAttachmentType.MiddleOfTopLine,
			TextRightAttachment = TextAttachmentType.MiddleOfTopLine,
			TextTopAttachment = TextAttachmentType.CenterOfText,
			TextBottomAttachment = TextAttachmentType.CenterOfText,
			BlockContentColor = Color.ByBlock,
		};
		if (doc.LineTypes.TryGetValue(LineType.ByBlockName, out var byBlock)) ml.LeaderLineType = byBlock;

		var cd = ml.ContextData;
		cd.ScaleFactor = scale;
		cd.TextHeight = style.TextHeight * scale;
		cd.ArrowheadSize = style.ArrowheadSize * scale;
		cd.LandingGap = style.LandingGap * scale;
		cd.HasTextContents = true;
		cd.TextStyle = textStyle;
		cd.TextColor = Color.ByBlock;
		cd.TextNormal = XYZ.AxisZ;
		cd.Direction = XYZ.AxisX;
		cd.TextLeftAttachment = TextAttachmentType.MiddleOfTopLine;
		cd.TextRightAttachment = TextAttachmentType.MiddleOfTopLine;
		cd.TextTopAttachment = TextAttachmentType.CenterOfText;
		cd.TextBottomAttachment = TextAttachmentType.CenterOfText;
		cd.FlowDirection = FlowDirectionType.Horizontal; // 0이면 기존 CAD가 객체를 그리지 못했다
		cd.LineSpacing = LineSpacingStyle.AtLeast;
		cd.LineSpacingFactor = 1;
		cd.BackgroundScaleFactor = 1.5;
		cd.BlockContentColor = Color.ByBlock;
		var docScale = doc.Scales.FirstOrDefault(s => s.Name == "1:1");
		if (docScale != null) cd.Scale = docScale;

		var root = new MultiLeaderObjectContextData.LeaderRoot
		{
			ConnectionPoint = new XYZ(conn.X, conn.Y, 0),
			Direction = new XYZ(dir, 0, 0),
			LandingDistance = style.LandingDistance,
			ContentValid = true,
			LeaderIndex = 0,
			TextAttachmentDirection = TextAttachmentDirectionType.Horizontal,
			Unknown = true,
		};
		var line = new MultiLeaderObjectContextData.LeaderLine
		{
			PathType = MultiLeaderPathType.StraightLineSegments,
			LineColor = Color.ByBlock,
			LineWeight = LineWeightType.ByBlock,
			Index = 0,
		};
		for (int i = 0; i < pts.Count - 1; i++) line.Points.Add(new XYZ(pts[i].X, pts[i].Y, 0));
		root.Lines.Add(line);
		cd.LeaderRoots.Add(root);

		SetText(ml, text);
		return ml;
	}

	/// <summary>문자 바꾸기: 문자 위치·정렬·내용 기준점도 다시 계산.</summary>
	public static void SetText(MultiLeader ml, string text)
	{
		var cd = ml.ContextData;
		cd.TextLabel = text;
		if (cd.LeaderRoots.Count == 0) return;
		var root = cd.LeaderRoots[0];
		double s = ml.ScaleFactor > 0 ? ml.ScaleFactor : 1;
		int dir = root.Direction.X < 0 ? -1 : 1;
		var c = root.ConnectionPoint;
		double endX = c.X + dir * root.LandingDistance * s;
		double h = cd.TextHeight;
		double tx = endX + dir * cd.LandingGap;
		cd.TextLocation = new XYZ(tx, c.Y + h / 2, 0);
		cd.TextAlignment = dir > 0 ? TextAlignmentType.Left : TextAlignmentType.Right;
		cd.TextAttachmentPoint = dir > 0 ? TextAttachmentPointType.Left : TextAttachmentPointType.Right;
		ml.TextAlignment = cd.TextAlignment;
		// 내용 기준점: 오른쪽이면 착륙선 끝, 왼쪽이면 문자 왼쪽 끝 (기존 CAD와 같음, 글자 폭은 어림)
		double width = EstimateWidth(text, h);
		cd.ContentBasePoint = dir > 0 ? new XYZ(endX, c.Y, 0) : new XYZ(tx - width, c.Y, 0);
	}

	private static double EstimateWidth(string text, double h)
	{
		string plain = Free2DCad.Rendering.MTextTokenizer.PlainText(text ?? string.Empty);
		int longest = plain.Split('\n').Select(l => l.Sum(ch => ch > 0x2000 ? 2 : 1)).DefaultIfEmpty(0).Max();
		return longest * h * 0.78;
	}
}

/// <summary>
/// MLEADER (기존 CAD와 같은 순서): 지시선 화살촉 위치 → 다음 점 (스타일의 최대 점 수가 되면 자동 종료) → 문자.
/// 지시선연결선먼저(L): 연결점부터. 문자를 명령줄에서 넣지 않으면 앱이 바로 문자 편집을 연다.
/// </summary>
public sealed class MLeaderCommand : EditorCommand
{
	private readonly List<XY> _pts = new();
	private readonly List<string> _lines = new();
	private bool _landingFirst;
	private bool _textStep;

	public override string Name => "MLEADER";

	public override string[] Aliases => new[] { "MLD" };

	/// <summary>문자를 비운 채 만든 다중지시선 (앱이 바로 문자 편집을 시작).</summary>
	public MultiLeader? Created { get; private set; }

	private const string FirstPrompt = "지시선 화살촉 위치 지정 또는 [지시선연결선먼저(L)/컨텐츠먼저(C)/옵션(O)] <옵션>:";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pts.Clear();
		this._lines.Clear();
		this._landingFirst = false;
		this._textStep = false;
		this.Created = null;
		this.Prompt = FirstPrompt;
	}

	private int MaxPoints
	{
		get
		{
			int n = this.Ctx != null ? MLeaderBuilder.CurrentStyle(this.Ctx.Doc.Document).MaxLeaderSegmentsPoints : 2;
			return n >= 2 ? n : 2;
		}
	}

	public override void OnText(string text)
	{
		string t = text.Trim();
		if (this._textStep)
		{
			this._lines.Add(text);
			this.Prompt = "문자의 다음 줄 입력 <완료>:";
			return;
		}
		switch (t.ToUpperInvariant())
		{
			case "L" when this._pts.Count == 0:
			case "C" when this._pts.Count == 0:
				this._landingFirst = true;
				this.Prompt = "지시선 연결선 위치 지정:";
				break;
			case "O" when this._pts.Count == 0:
				this.Ctx?.Print("지시선 유형·화살촉·착륙 거리는 다중지시선 스타일(Standard)을 따릅니다.");
				break;
			case "E" when this._pts.Count >= 2:
				this.ToText();
				break;
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this._textStep) return;
		var p = new XY(x, y);
		if (this._pts.Count > 0 && this.Ctx.Ortho)
		{
			var o = CoordinateParser.ApplyOrtho((this._pts[^1].X, this._pts[^1].Y), (x, y));
			p = new XY(o.X, o.Y);
		}
		this.Ctx.LastPoint = (p.X, p.Y);
		if (this._landingFirst)
		{
			// 연결점 → 화살촉 (두 점으로 끝)
			this._pts.Insert(0, p);
			if (this._pts.Count == 1)
			{
				this.Prompt = "지시선 화살촉 위치 지정:";
			}
			else
			{
				this.ToText();
			}
			return;
		}
		this._pts.Add(p);
		if (this._pts.Count >= this.MaxPoints)
		{
			this.ToText();
		}
		else
		{
			this.Prompt = "다음 다중지시선 점을 지정하거나 [종료(E)] <종료>:";
		}
	}

	/// <summary>점을 다 받으면 기존 CAD처럼 바로 빈 문자로 만들고 앱이 화면 편집(문자 편집기 탭)을 연다.</summary>
	private void ToText()
	{
		this._textStep = true;
		this.Create();
	}

	public override void OnEnter()
	{
		if (!this._textStep)
		{
			if (this._pts.Count >= 2) this.ToText();
			else if (this._pts.Count == 0) this.Finish();
			return;
		}
		this.Create();
	}

	private void Create()
	{
		var doc = this.Ctx!.Doc;
		double scale = DimensionMemory.Scale(DimensionStyleHelper.Current(doc.Document));
		string text = string.Join("\\P", this._lines.Select(l => l.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")));
		var ml = MLeaderBuilder.Create(doc.Document, this._pts, text, scale);
		doc.AddEntities(ml);
		if (this._lines.Count == 0) this.Created = ml;
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._pts.Count == 0 || this._textStep || this.Ctx == null) yield break;
		var pts = this._landingFirst ? new List<XY> { new(cursorX, cursorY), this._pts[0] } : this._pts.Append(new XY(cursorX, cursorY)).ToList();
		if (pts.Count < 2 || Math.Abs(pts[^1].X - pts[^2].X) + Math.Abs(pts[^1].Y - pts[^2].Y) < 1e-9) yield break;
		var ml = MLeaderBuilder.Create(this.Ctx.Doc.Document, pts, string.Empty, DimensionMemory.Scale(DimensionStyleHelper.Current(this.Ctx.Doc.Document)));
		yield return ml;
	}
}

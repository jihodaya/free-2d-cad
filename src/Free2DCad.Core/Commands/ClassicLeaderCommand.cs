using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Blocks;

namespace Free2DCad.Core.Commands;

/// <summary>
/// LEADER (기존 CAD와 같은 흐름·문구):
/// "지시선의 시작:" → "다음 점:" → "지점까지: [형식(F)/취소(U)]/&lt;주석&gt;:" (점을 찍을 때마다 반복)
/// · 형식(F) "[화살촉(A)/없음(N)/스플라인(S)/직선(ST)]/&lt;종료(E)&gt;:" · 취소(U) = 마지막 점 빼기
/// · 주석(Enter) "주석 문자의 첫 줄/&lt;옵션&gt;:" → 친 줄은 "주석 문자의 다음 줄:"로 이어 받고 빈 Enter로 끝
/// · 옵션(빈 Enter) "치수 문자 옵션: [블록(B)/복사(C)/없음(N)/공차(T)]/&lt;Mtext&gt;:" — Mtext = 화면 편집, 복사 = 고른 문자·공차·블록을 끝점으로 복사,
///   없음 = 지시선만, 공차 = 기하학적 공차 대화상자 → 끝점에 공차, 블록 = 블록 이름 → 삽입점·축척·회전.
/// 기존 CAD가 저장하는 것과 같게: 연결선 꼭짓점을 따로 넣지 않고(찍은 점 그대로), 문자는 끝점에서 (±DIMGAP, +DIMGAP)에
/// 아래 왼쪽(오른쪽으로 갈 때)·아래 오른쪽(왼쪽으로 갈 때) 맞춤, 연결 방향은 오른쪽 = 반대·왼쪽 = 같음, 문자 높이·폭 0.
/// </summary>
public sealed class ClassicLeaderCommand : EditorCommand
{
	private enum Step
	{
		First,
		Next,
		To,
		Format,
		TextFirst,
		TextNext,
		Options,
		CopyPick,
		Tolerance,
		BlockName,
		BlockPoint,
		BlockScaleX,
		BlockScaleY,
		BlockRotation,
	}

	private readonly List<XY> _pts = new();
	private readonly List<string> _lines = new();
	private Step _step;
	private bool _arrow = true;
	private bool _spline;
	private BlockRecord? _block;
	private XYZ _blockPt;
	private double _sx = 1, _sy = 1;

	public override string Name => "LEADER";

	public override string[] Aliases => new[] { "LEAD" };

	/// <summary>Mtext로 만든 빈 주석 (앱이 바로 화면 편집을 시작).</summary>
	public MText? Created { get; private set; }

	/// <summary>공차(T): 앱이 기하학적 공차 대화상자를 띄우고 SetTolerance로 결과를 넘긴다.</summary>
	public event Action<ClassicLeaderCommand>? ToleranceRequested;

	/// <summary>블록 찾아보기(BROWSE/EXPLORE): 앱이 파일을 골라 블록으로 가져온 뒤 SetBlock으로 넘긴다.</summary>
	public event Action<ClassicLeaderCommand>? BlockBrowseRequested;

	/// <summary>문자를 치는 단계에서는 공백이 글자 (Enter 대신).</summary>
	public override bool SpaceIsText => this._step is Step.TextFirst or Step.TextNext;

	/// <summary>지금 객체를 고르는 단계 (복사).</summary>
	public override bool WantsSelection => this._step == Step.CopyPick;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pts.Clear();
		this._lines.Clear();
		this._arrow = true;
		this._spline = false;
		this.Go(Step.First);
	}

	private void Go(Step step)
	{
		this._step = step;
		this.Prompt = step switch
		{
			Step.First => "지시선의 시작:",
			Step.Next => "다음 점:",
			Step.To => "지점까지: [형식(F)/취소(U)]/<주석>:",
			Step.Format => "[화살촉(A)/없음(N)/스플라인(S)/직선(ST)]/<종료(E)>:",
			Step.TextFirst => "주석 문자의 첫 줄/<옵션>:",
			Step.TextNext => "주석 문자의 다음 줄:",
			Step.Options => "치수 문자 옵션: [블록(B)/복사(C)/없음(N)/공차(T)]/<Mtext>:",
			Step.CopyPick => "도면요소 선택:",
			Step.Tolerance => "기하학적 공차 대화상자에서 내용을 지정하십시오.",
			Step.BlockName => "[블록목록(?)/찾아보기(BROWSE)/탐색(EXPLORE)]/<삽입할 블록>:",
			Step.BlockPoint => "블록의 삽입점:",
			Step.BlockScaleX => $"[구석(C)/XYZ]/X 축척비율 <{this._sx:0.000000}>:",
			Step.BlockScaleY => $"Y 축척 비율: <X 축척과 같음 ({this._sx:0.000000})>:",
			Step.BlockRotation => "블록의 회전 각도 <0.0>:",
			_ => this.Prompt,
		};
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		switch (this._step)
		{
			case Step.First:
			case Step.Next:
			case Step.To:
				{
					var p = new XY(x, y);
					if (this._pts.Count > 0 && this.Ctx.Ortho)
					{
						var o = CoordinateParser.ApplyOrtho((this._pts[^1].X, this._pts[^1].Y), (x, y));
						p = new XY(o.X, o.Y);
					}
					if (this._pts.Count > 0 && Math.Abs(p.X - this._pts[^1].X) + Math.Abs(p.Y - this._pts[^1].Y) < 1e-12)
					{
						return;
					}
					this._pts.Add(p);
					this.Ctx.LastPoint = (p.X, p.Y);
					this.Go(this._pts.Count == 1 ? Step.Next : Step.To);
					break;
				}
			case Step.BlockPoint:
				this._blockPt = Num.Z(x, y);
				this.Ctx.LastPoint = (x, y);
				this.Go(Step.BlockScaleX);
				break;
			case Step.BlockScaleX:
			case Step.BlockScaleY:
			case Step.BlockRotation:
				{
					// 점을 찍으면 삽입점에서 그 점까지 (축척 = 거리, 회전 = 각도)
					double dx = x - this._blockPt.X, dy = y - this._blockPt.Y;
					if (this._step == Step.BlockRotation)
					{
						this.PlaceBlock(Math.Atan2(dy, dx));
					}
					else
					{
						this.OnText(Math.Sqrt(dx * dx + dy * dy).ToString(System.Globalization.CultureInfo.InvariantCulture));
					}
					break;
				}
		}
	}

	public override void OnEnter()
	{
		switch (this._step)
		{
			case Step.First:
			case Step.Next:
				// 점이 둘이 안 되면 지시선 없이 끝
				this.Finish();
				break;
			case Step.To:
				this.Go(Step.TextFirst);
				break;
			case Step.Format:
				this.Go(Step.To);
				break;
			case Step.TextFirst:
				this.Go(Step.Options);
				break;
			case Step.TextNext:
				this.CreateText();
				break;
			case Step.Options:
				this.CreateText(); // <Mtext>: 빈 주석 → 화면 편집
				break;
			case Step.CopyPick:
				this.CreateWithout();
				break;
			case Step.BlockName:
				this.Go(Step.BlockName);
				break;
			case Step.BlockScaleX:
				this.Go(Step.BlockScaleY);
				break;
			case Step.BlockScaleY:
				this._sy = this._sx;
				this.Go(Step.BlockRotation);
				break;
			case Step.BlockRotation:
				this.PlaceBlock(0);
				break;
		}
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string key = text.Trim().ToUpperInvariant();
		switch (this._step)
		{
			case Step.First:
			case Step.Next:
				this.TryPoint(text);
				break;
			case Step.To:
				if (key == "F")
				{
					this.Go(Step.Format);
				}
				else if (key == "U")
				{
					if (this._pts.Count > 0) this._pts.RemoveAt(this._pts.Count - 1);
					this.Ctx.LastPoint = this._pts.Count > 0 ? (this._pts[^1].X, this._pts[^1].Y) : null;
					this.Go(this._pts.Count switch { 0 => Step.First, 1 => Step.Next, _ => Step.To });
				}
				else if (key is "A" or "ANNOTATION" or "주석")
				{
					this.Go(Step.TextFirst);
				}
				else
				{
					this.TryPoint(text);
				}
				break;
			case Step.Format:
				switch (key)
				{
					case "A": this._arrow = true; break;
					case "N": this._arrow = false; break;
					case "S": this._spline = true; break;
					case "ST": this._spline = false; break;
					case "E": break;
					default:
						this.Ctx.Print("옵션을 알 수 없습니다. A, N, S, ST, E 중에서 입력하십시오.");
						return;
				}
				this.Go(Step.To);
				break;
			case Step.TextFirst:
			case Step.TextNext:
				this._lines.Add(text);
				this.Go(Step.TextNext);
				break;
			case Step.Options:
				switch (key)
				{
					case "M":
					case "MTEXT":
						this.CreateText();
						break;
					case "N":
						this.CreateWithout();
						break;
					case "C":
						this.Go(Step.CopyPick);
						break;
					case "T":
						this.Go(Step.Tolerance);
						if (this.ToleranceRequested == null)
						{
							this.Ctx.Print("기하학적 공차 대화상자를 열 수 없습니다.");
							this.CreateWithout();
						}
						else
						{
							this.ToleranceRequested(this);
						}
						break;
					case "B":
						this.Go(Step.BlockName);
						break;
					default:
						this.Ctx.Print("옵션을 알 수 없습니다. B, C, N, T, M 중에서 입력하십시오.");
						break;
				}
				break;
			case Step.BlockName:
				if (key == "?")
				{
					var names = this.Ctx.Doc.Document.BlockRecords
						.Where(b => !b.Name.StartsWith('*') && !b.IsAnonymous && b.Name.IndexOf('|') < 0)
						.Select(b => b.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
					this.Ctx.Print(names.Count == 0 ? "정의된 블록이 없습니다." : "정의된 블록: " + string.Join(", ", names));
					this.Go(Step.BlockName);
				}
				else if (this.Ctx.Doc.Document.BlockRecords.TryGetValue(text.Trim(), out var br) && !br.Name.StartsWith('*'))
				{
					this.SetBlock(br);
				}
				else if (key is "BROWSE" or "EXPLORE")
				{
					if (this.BlockBrowseRequested == null)
					{
						this.Ctx.Print("찾아보기를 열 수 없습니다. 블록 이름을 입력하십시오.");
					}
					else
					{
						this.BlockBrowseRequested(this);
					}
				}
				else
				{
					this.Ctx.Print($"블록 \"{text.Trim()}\"을(를) 찾을 수 없습니다.");
				}
				break;
			case Step.BlockPoint:
				this.TryPoint(text);
				break;
			case Step.BlockScaleX:
				if (Num.TryParse(text, out double sx) && Math.Abs(sx) > 1e-12)
				{
					this._sx = this._sy = sx;
					this.Go(Step.BlockScaleY);
				}
				break;
			case Step.BlockScaleY:
				if (Num.TryParse(text, out double sy) && Math.Abs(sy) > 1e-12)
				{
					this._sy = sy;
					this.Go(Step.BlockRotation);
				}
				break;
			case Step.BlockRotation:
				if (Num.TryParse(text, out double deg))
				{
					this.PlaceBlock(deg * Math.PI / 180);
				}
				break;
		}
	}

	private void TryPoint(string text)
	{
		if (CoordinateParser.TryParse(text.Trim(), this.Ctx!.LastPoint, null, false, out (double X, double Y) p))
		{
			this.OnPoint(p.X, p.Y);
		}
		else
		{
			this.Ctx.Print("점 또는 옵션 키워드가 필요합니다.");
		}
	}

	/// <summary>공차 대화상자 결과 (null = 취소 → 지시선만).</summary>
	public void SetTolerance(string? text)
	{
		if (this.IsFinished || this._step != Step.Tolerance) return;
		if (string.IsNullOrEmpty(text))
		{
			this.CreateWithout();
			return;
		}
		var end = this._pts[^1];
		var tol = new Tolerance
		{
			Text = text,
			InsertionPoint = Num.Z(end.X, end.Y),
			Direction = XYZ.AxisX,
			Style = DimensionStyleHelper.Current(this.Ctx!.Doc.Document),
		};
		this.Add(LeaderCreationType.CreatedWithToleranceAnnotation, tol);
	}

	/// <summary>삽입할 블록 (이름 입력 또는 찾아보기) → 삽입점부터 묻는다.</summary>
	public void SetBlock(BlockRecord? block)
	{
		if (this.IsFinished || this._step != Step.BlockName || block == null) return;
		this._block = block;
		this._sx = this._sy = 1;
		this.Go(Step.BlockPoint);
	}

	private void PlaceBlock(double rotation)
	{
		if (this._block == null) return;
		var insert = BlockDefinition.MakeInsert(this._block, this._blockPt, this._sx, this._sy, this._sx, rotation);
		this.Add(LeaderCreationType.CreatedWithBlockReferenceAnnotation, insert);
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this._step != Step.CopyPick) return;
		var src = entities.FirstOrDefault(e => e is MText or TextEntity or Tolerance or Insert);
		if (src == null)
		{
			this.Ctx?.Print("문자, 여러 줄 문자, 공차 또는 블록을 선택하십시오.");
			return;
		}
		this.Ctx?.Selection.Clear();
		var end = this._pts[^1];
		var (dir, gap, _) = this.Placement();
		var copy = (Entity)src.Clone();
		switch (copy)
		{
			case MText m:
				m.InsertPoint = Num.Z(end.X + dir * gap, end.Y + gap);
				m.AttachmentPoint = dir > 0 ? AttachmentPointType.BottomLeft : AttachmentPointType.BottomRight;
				this.Add(LeaderCreationType.CreatedWithTextAnnotation, m);
				return;
			case Tolerance t:
				t.InsertionPoint = Num.Z(end.X, end.Y);
				this.Add(LeaderCreationType.CreatedWithToleranceAnnotation, t);
				return;
			case Insert ins:
				ins.ApplyTransform(Transform.CreateTranslation(Num.Z(end.X - ins.InsertPoint.X, end.Y - ins.InsertPoint.Y)));
				this.Add(LeaderCreationType.CreatedWithBlockReferenceAnnotation, ins);
				return;
			case TextEntity txt:
				{
					var at = txt.InsertPoint;
					txt.ApplyTransform(Transform.CreateTranslation(Num.Z(end.X + dir * gap - at.X, end.Y + gap - at.Y)));
					// 한 줄 문자는 지시선에 연결하지 않는다 (기존 CAD와 같음)
					this.Add(LeaderCreationType.CreatedWithoutAnnotation, null, txt);
					return;
				}
		}
	}

	/// <summary>방향(마지막 조각이 오른쪽이면 +1), DIMGAP, 문자 높이(DIMTXT) — 모두 치수 축척을 곱한 값.</summary>
	private (double Dir, double Gap, double Height) Placement()
	{
		var style = DimensionStyleHelper.Current(this.Ctx!.Doc.Document);
		double scale = DimensionMemory.Scale(style);
		double h = (style.TextHeight > 0 ? style.TextHeight : 2.5) * scale;
		double gap = Math.Abs(style.DimensionLineGap) * scale;
		double dir = this._pts.Count >= 2 && this._pts[^1].X < this._pts[^2].X ? -1 : 1;
		return (dir, gap, h);
	}

	private void CreateText()
	{
		var doc = this.Ctx!.Doc;
		var style = DimensionStyleHelper.Current(doc.Document);
		var (dir, gap, h) = this.Placement();
		var end = this._pts[^1];
		var mtext = new MText
		{
			Value = string.Join("\\P", this._lines.Select(MTextValue)),
			Height = h,
			InsertPoint = Num.Z(end.X + dir * gap, end.Y + gap),
			AttachmentPoint = dir > 0 ? AttachmentPointType.BottomLeft : AttachmentPointType.BottomRight,
			Style = style.Style ?? doc.Document.TextStyles.FirstOrDefault() ?? TextStyle.Default,
		};
		if (this._lines.Count == 0) this.Created = mtext;
		this.Add(LeaderCreationType.CreatedWithTextAnnotation, mtext);
	}

	private void CreateWithout() => this.Add(LeaderCreationType.CreatedWithoutAnnotation, null);

	private void Add(LeaderCreationType type, Entity? annotation, Entity? extra = null)
	{
		var doc = this.Ctx!.Doc;
		if (this._pts.Count < 2)
		{
			this.Finish();
			return;
		}
		var (dir, _, _) = this.Placement();
		var leader = this.MakeLeader(this._pts);
		leader.CreationType = type;
		// 기존 CAD: 주석이 오른쪽이면 "반대", 왼쪽이거나 주석이 없으면 "같음"
		leader.HookLineDirection = annotation != null && dir > 0 ? HookLineDirection.Opposite : HookLineDirection.Same;
		leader.AssociatedAnnotation = annotation;
		var list = new List<Entity> { leader };
		if (annotation != null) list.Add(annotation);
		if (extra != null) list.Add(extra);
		doc.AddEntities(list.ToArray());
		this.Finish();
	}

	private Leader MakeLeader(IEnumerable<XY> pts) => new()
	{
		Vertices = pts.Select(p => Num.Z(p.X, p.Y)).ToList(),
		ArrowHeadEnabled = this._arrow,
		Style = DimensionStyleHelper.Current(this.Ctx!.Doc.Document),
		PathType = this._spline ? LeaderPathType.Spline : LeaderPathType.StraightLineSegments,
		CreationType = LeaderCreationType.CreatedWithoutAnnotation,
		HorizontalDirection = XYZ.AxisX,
		TextHeight = 0,
		TextWidth = 0,
	};

	private static string MTextValue(string s) => s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this.Ctx == null || this.IsFinished || this._pts.Count == 0 || this._step is not (Step.Next or Step.To))
		{
			yield break;
		}
		var c = new XY(cursorX, cursorY);
		if (Math.Abs(c.X - this._pts[^1].X) + Math.Abs(c.Y - this._pts[^1].Y) < 1e-12) yield break;
		yield return this.MakeLeader(this._pts.Append(c));
	}
}

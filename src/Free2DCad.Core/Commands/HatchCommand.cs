using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Hatching;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Commands;

/// <summary>
/// HATCH 명령 (기존 CAD 방식).
/// 내부 점을 여러 번 클릭하거나(S로 객체 선택) 경계를 모은 뒤 Enter로 해치를 만든다.
/// 대화상자에서 '추가: 내부 점 선택/객체 선택'으로 부를 때는 CollectOnly=true로 경계만 모으고 끝낸다.
/// </summary>
public sealed class HatchCommand : EditorCommand
{
	private enum Mode { Points, Objects }

	private Mode _mode;
	private Scene? _scene;
	private SceneIndex? _index;

	/// <summary>마지막으로 쓴 설정 (다음 HATCH의 기본값, 기존 CAD처럼 기억).</summary>
	public static HatchSettings LastSettings { get; set; } = new();

	public override string Name => "HATCH";

	public override string[] Aliases => new[] { "H", "BH", "BHATCH" };

	/// <summary>이번 명령의 설정. Start 전에 바꿀 수 있다.</summary>
	public HatchSettings Settings { get; set; } = LastSettings.Clone();

	/// <summary>true면 경계만 모으고 해치는 만들지 않는다 (대화상자가 이어서 만든다).</summary>
	public bool CollectOnly { get; set; }

	/// <summary>시작 방식: true면 객체 선택으로 시작.</summary>
	public bool StartWithObjects { get; set; }

	/// <summary>경계 후보 범위 (현재 화면). null이면 도면 전체.</summary>
	public Bounds? SearchWindow { get; set; }

	/// <summary>객체 선택 허용 거리(도면 단위). 화면 배율에 맞춰 UI가 넣어 준다.</summary>
	public double PickTolerance { get; set; } = 5.0;

	/// <summary>내부 점으로 찾은 영역들 (영역마다 Loops[0]=바깥, 나머지=섬).</summary>
	public List<List<double[]>> Regions { get; } = new();

	/// <summary>객체 선택으로 고른 닫힌 경계 객체들.</summary>
	public List<Entity> BoundaryObjects { get; } = new();

	/// <summary>'설정(T)' 입력 시 UI가 대화상자를 연다.</summary>
	public event Action<HatchCommand>? SettingsRequested;

	/// <summary>CollectOnly일 때 Enter 또는 Esc로 끝나면 호출 (대화상자를 다시 띄움). 모은 경계는 Regions/BoundaryObjects.</summary>
	public event Action<HatchCommand>? Collected;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._scene = SceneBuilder.Build(ctx.Doc.Document);
		this._index = new SceneIndex(this._scene);
		this._mode = this.StartWithObjects ? Mode.Objects : Mode.Points;
		this.UpdatePrompt();
	}

	private void UpdatePrompt()
	{
		this.Prompt = this._mode == Mode.Points
			? "내부 점 선택 또는 [객체 선택(S)/명령 취소(U)/설정(T)]:"
			: "객체 선택 또는 [내부 점 선택(K)/명령 취소(U)/설정(T)]:";
	}

	public override void OnText(string text)
	{
		switch (text.Trim().ToUpperInvariant())
		{
			case "S":
				this._mode = Mode.Objects;
				break;
			case "K":
				this._mode = Mode.Points;
				break;
			case "U":
				this.UndoLastPick();
				break;
			case "T":
				this.SettingsRequested?.Invoke(this);
				break;
			default:
				this.Ctx?.Print("옵션을 알 수 없습니다. S, K, U, T 중에서 입력하십시오.");
				break;
		}
		this.UpdatePrompt();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this._scene == null)
		{
			return;
		}

		if (this._mode == Mode.Points)
		{
			this.Ctx.Print("모든 것을 선택 중... 선택된 데이터 분석 중...");
			var loops = HatchBoundaryFinder.FindAt(this._scene, x, y, this.Settings.DetectIslands ? this.Settings.IslandStyle : HatchStyleType.Ignore, this.SearchWindow);
			if (loops == null)
			{
				this.Ctx.Print("유효한 해치 경계를 찾을 수 없습니다. 경계가 완전히 보이도록 확대하거나 닫힌 영역 안을 클릭하십시오.");
				return;
			}
			this.Regions.Add(loops);
			this.Ctx.Print(loops.Count > 1 ? $"경계를 찾았습니다 (고립영역 {loops.Count - 1}개)." : "경계를 찾았습니다.");
		}
		else
		{
			var picked = this._index?.Pick(x, y, this.PickTolerance);
			if (picked == null)
			{
				return;
			}
			if (!IsClosedBoundary(picked))
			{
				this.Ctx.Print("닫힌 객체(원, 닫힌 폴리선, 타원 등)만 경계로 선택할 수 있습니다.");
				return;
			}
			if (!this.BoundaryObjects.Contains(picked))
			{
				this.BoundaryObjects.Add(picked);
				this.Ctx.Print("1개를 찾음");
			}
		}
	}

	/// <summary>객체 선택 단계: 클릭·창 선택으로 경계를 고른다.</summary>
	public override bool WantsSelection => !this.IsFinished && this._mode == Mode.Objects;

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		int added = 0, rejected = 0;
		foreach (var e in entities)
		{
			if (!IsClosedBoundary(e))
			{
				rejected++;
			}
			else if (!this.BoundaryObjects.Contains(e))
			{
				this.BoundaryObjects.Add(e);
				added++;
			}
		}
		if (added > 0) this.Ctx?.Print($"{added}개를 찾음");
		if (rejected > 0) this.Ctx?.Print("닫힌 객체(원, 닫힌 폴리선, 타원 등)만 경계로 선택할 수 있습니다.");
		this.UpdatePrompt();
	}

	private void UndoLastPick()
	{
		if (this._mode == Mode.Points && this.Regions.Count > 0)
		{
			this.Regions.RemoveAt(this.Regions.Count - 1);
		}
		else if (this.BoundaryObjects.Count > 0)
		{
			this.BoundaryObjects.RemoveAt(this.BoundaryObjects.Count - 1);
		}
		else if (this.Regions.Count > 0)
		{
			this.Regions.RemoveAt(this.Regions.Count - 1);
		}
	}

	public override void OnEnter()
	{
		if (this.Ctx == null)
		{
			return;
		}
		if (!this.CollectOnly)
		{
			var hatches = CreateHatches(this.Settings, this.Regions, this.BoundaryObjects);
			if (hatches.Count > 0)
			{
				this.Ctx.Doc.AddEntities(hatches);
				LastSettings = this.Settings.Clone();
			}
			this.Finish();
			return;
		}
		this.Finish();
		this.Collected?.Invoke(this);
	}

	public override void OnCancel()
	{
		base.OnCancel();
		if (this.CollectOnly)
		{
			// 기존 CAD처럼 Esc도 대화상자로 돌아간다 (고른 경계는 유지)
			this.Collected?.Invoke(this);
		}
	}

	/// <summary>모은 경계로 해치 생성. SeparateHatches가 아니면 모든 경계를 해치 하나로 묶는다.</summary>
	public static List<Hatch> CreateHatches(HatchSettings s, IReadOnlyList<List<double[]>> regions, IReadOnlyList<Entity> objects)
	{
		var result = new List<Hatch>();
		if (s.SeparateHatches)
		{
			result.AddRange(regions.Select(r => HatchFactory.CreateFromLoops(r, s)));
			result.AddRange(objects.Select(o => HatchFactory.CreateFromEntities(new[] { o }, s)));
			return result;
		}
		if (regions.Count > 0)
		{
			var all = regions.SelectMany(r => r).ToList();
			var h = HatchFactory.CreateFromLoops(all, s);
			if (objects.Count > 0)
			{
				foreach (var o in objects)
				{
					h.Paths.Add(new Hatch.BoundaryPath(new[] { o }));
				}
			}
			result.Add(h);
		}
		else if (objects.Count > 0)
		{
			result.Add(HatchFactory.CreateFromEntities(objects, s));
		}
		return result;
	}

	/// <summary>경계로 쓸 수 있는 닫힌 객체인지.</summary>
	public static bool IsClosedBoundary(Entity e) => e switch
	{
		Arc => false,
		Circle => true,
		LwPolyline p => p.IsClosed,
		Polyline2D p => p.IsClosed,
		Ellipse el => Math.Abs(el.EndParameter - el.StartParameter) >= Math.PI * 2 - 1e-9,
		Spline sp => sp.IsClosed,
		_ => false,
	};

	/// <summary>객체 선택으로 고른 경계 (기존 CAD처럼 점선으로 강조).</summary>
	public override IEnumerable<Entity> Highlighted => this.BoundaryObjects;

	/// <summary>내부 점으로 찾은 경계 (바깥·섬 모두 점선으로 강조).</summary>
	public override IEnumerable<double[]> HighlightedLoops => this.Regions.SelectMany(r => r);

	/// <summary>객체 선택 단계에서는 마우스를 올린 객체를 미리 강조한다.</summary>
	public override bool PickingObject => !this.IsFinished && this._mode == Mode.Objects;
}

using ACadSharp.Entities;

namespace Free2DCad.Core.Commands;

/// <summary>
/// 명령 실행에 필요한 문서, 선택 상태, 작도 보조 상태 및 출력 콜백을 제공하는 컨텍스트.
/// </summary>
public sealed class CommandContext
{
	public EditorDocument Doc { get; }

	public ISet<Entity> Selection { get; }

	public bool Ortho { get; set; }

	public (double X, double Y)? LastPoint { get; set; }

	public Action<string> Print { get; }

	/// <summary>Print로 낸 줄 수 (명령이 입력에 반응했는지 앱이 알아보는 데 씀).</summary>
	public int PrintCount { get; private set; }

	/// <summary>화면 보기 조작 (ZOOM·PAN). 앱이 설정, 시험에서는 없을 수 있음.</summary>
	public IViewController? View { get; set; }

	/// <summary>점을 넘길 때 Shift를 누르고 있었는지 (자르기 중 Shift = 연장 등).</summary>
	public bool ShiftPressed { get; set; }

	/// <summary>
	/// 화면의 장면 색인으로 점 아래 객체 고르기 (앱이 설정, 화면 5px 허용 오차). 없으면 PickAt이 장면을 새로 만든다(느림).
	/// </summary>
	public Func<double, double, Entity?>? Picker { get; set; }

	/// <summary>점 아래 객체 (명령에서 객체 하나를 클릭으로 고를 때).</summary>
	public Entity? PickAt(double x, double y)
	{
		if (this.Picker != null)
		{
			return this.Picker(x, y);
		}
		var scene = Free2DCad.Rendering.SceneBuilder.Build(this.Doc.Document);
		return new SceneIndex(scene).Pick(x, y, 5.0);
	}

	public CommandContext(
		EditorDocument doc,
		ISet<Entity>? selection = null,
		bool ortho = false,
		(double X, double Y)? lastPoint = null,
		Action<string>? print = null)
	{
		this.Doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this.Selection = selection ?? new HashSet<Entity>();
		this.Ortho = ortho;
		this.LastPoint = lastPoint;
		var target = print ?? (_ => { });
		this.Print = msg =>
		{
			this.PrintCount++;
			target(msg);
		};
	}
}

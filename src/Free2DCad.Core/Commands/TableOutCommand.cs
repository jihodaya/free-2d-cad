using Free2DCad.Core.Tools;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Commands;

/// <summary>
/// TABLEOUT 명령: 영역을 지정하여 선과 텍스트로 구성된 표를 CSV 또는 테이블 데이터로 추출한다.
/// </summary>
public sealed class TableOutCommand : EditorCommand
{
	private int _step;
	private (double X, double Y) _p1;

	public override string Name => "TABLEOUT";
	public override string[] Aliases => new[] { "TBO" };

	public Action<TableResult>? OnTableExtracted { get; set; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this.Prompt = "표 영역의 첫 번째 구석점 지정:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;

		if (this._step == 0)
		{
			this._p1 = (x, y);
			this.Ctx.LastPoint = (x, y);
			this._step = 1;
			this.Prompt = "표 영역의 반대쪽 구석점 지정:";
		}
		else
		{
			var region = new Bounds(
				Math.Min(this._p1.X, x),
				Math.Min(this._p1.Y, y),
				Math.Max(this._p1.X, x),
				Math.Max(this._p1.Y, y));

			var scene = SceneBuilder.Build(this.Ctx.Doc.Document);
			var result = TableExtractor.Extract(scene, region);

			this.Ctx.Print($"표 추출 완료: {result.Rows.Length}행");
			this.OnTableExtracted?.Invoke(result);
			this.Finish();
		}
	}
}

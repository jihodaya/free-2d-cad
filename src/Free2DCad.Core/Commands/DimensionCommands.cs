using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;

namespace Free2DCad.Core.Commands;

/// <summary>
/// DIST 명령: 두 점 사이의 거리, 각도, 증분(ΔX, ΔY)을 계산하여 출력한다.
/// </summary>
public sealed class DistCommand : EditorCommand
{
	private int _step;
	private (double X, double Y) _p1;

	public override string Name => "DIST";
	public override string[] Aliases => new[] { "DI" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this.Prompt = "첫 번째 점 지정:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;

		if (this._step == 0)
		{
			this._p1 = (x, y);
			this.Ctx.LastPoint = (x, y);
			this._step = 1;
			this.Prompt = "두 번째 점 지정:";
		}
		else
		{
			double dx = x - this._p1.X;
			double dy = y - this._p1.Y;
			double dist = Math.Sqrt(dx * dx + dy * dy);
			double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
			if (deg < 0) deg += 360.0;

			this.Ctx.Print($"거리 = {dist:F4}, 각도 = {deg:F2}°, ΔX = {dx:F4}, ΔY = {dy:F4}");
			this.Finish();
		}
	}
}



/// <summary>새 치수에 쓸 현재 치수 스타일 (Header.CurrentDimensionStyleName, 없으면 첫 스타일).</summary>
internal static class DimensionStyleHelper
{
	internal static DimensionStyle Current(CadDocument doc)
	{
		string? curName = doc.Header.CurrentDimensionStyleName;
		var style = !string.IsNullOrEmpty(curName) && doc.DimensionStyles.Contains(curName)
			? doc.DimensionStyles[curName]
			: doc.DimensionStyles.FirstOrDefault() ?? DimensionStyle.Default;
		// 주석 축척 (상태 표시줄 1:1 버튼)
		return Free2DCad.Core.Styles.AnnotationScale.Apply(doc, style);
	}
}


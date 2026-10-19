using ACadSharp.Entities;
using CSMath;

namespace Free2DCad.Core.Editing;

/// <summary>
/// 선형·정렬 치수의 정의점 계산. ACadSharp는 DefinitionPoint를 "두 번째 점 쪽 치수선 끝"으로 그대로 쓰므로,
/// 사용자가 찍은 치수선 위치를 그대로 넣으면 두 번째 치수보조선이 비스듬히 꺾여 그려졌다.
/// </summary>
public static class DimensionGeometry
{
	/// <summary>치수선이 through 점을 지나도록 DefinitionPoint를 둔다.</summary>
	public static void PlaceDimensionLine(DimensionAligned d, XYZ through)
	{
		d.DefinitionPoint = DefinitionPointFor(d, through);
	}

	/// <summary>
	/// 치수 문자 옮기기 (그립·DIMTEDIT). DIMTMOVE 0(치수선과 텍스트 이동)이면 기존 CAD처럼 치수선도 그 점을 지나게 옮긴다
	/// — 문자 위치는 치수선 위의 점으로 저장되고 그리는 문자는 DIMTAD만큼 띄운다.
	/// </summary>
	public static void MoveText(DimensionAligned d, XYZ to)
	{
		if (d.GetActiveDimensionStyle().TextMovement == ACadSharp.Tables.TextMovement.MoveLineWithText)
		{
			PlaceDimensionLine(d, to);
		}
		d.TextMiddlePoint = to;
		d.IsTextUserDefinedLocation = true;
	}

	public static XYZ DefinitionPointFor(DimensionAligned d, XYZ through)
	{
		if (d is DimensionLinear lin)
		{
			// 치수선 방향 xVec = (cos θ, sin θ). 두 번째 점에서 치수선에 내린 수선의 발
			double c = Math.Cos(lin.Rotation), s = Math.Sin(lin.Rotation);
			double t = (through.X - lin.SecondPoint.X) * -s + (through.Y - lin.SecondPoint.Y) * c; // 치수선까지 수직 거리
			return new XYZ(lin.SecondPoint.X - s * t, lin.SecondPoint.Y + c * t, 0);
		}
		double dx = d.SecondPoint.X - d.FirstPoint.X, dy = d.SecondPoint.Y - d.FirstPoint.Y;
		double len = Math.Sqrt(dx * dx + dy * dy);
		if (len < 1e-12)
		{
			return through;
		}
		double vx = -dy / len, vy = dx / len; // 측정 방향의 수직
		double off = (through.X - d.FirstPoint.X) * vx + (through.Y - d.FirstPoint.Y) * vy;
		return new XYZ(d.SecondPoint.X + vx * off, d.SecondPoint.Y + vy * off, 0);
	}
}

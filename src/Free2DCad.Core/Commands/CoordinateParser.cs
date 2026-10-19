namespace Free2DCad.Core.Commands;

/// <summary>
/// CAD 좌표 입력 문자열을 파싱하는 유틸리티.
/// </summary>
public static class CoordinateParser
{
	/// <summary>
	/// 사용자 입력 문자열을 절대좌표, 상대좌표(@dx,dy), 상대극좌표(@거리&lt;각도) 또는 단일 거리로 해석한다.
	/// </summary>
	public static bool TryParse(
		string input,
		(double X, double Y)? lastPoint,
		(double X, double Y)? cursorPoint,
		bool ortho,
		out (double X, double Y) result)
	{
		result = default;
		if (string.IsNullOrWhiteSpace(input))
		{
			return false;
		}

		input = input.Trim();

		// 1. 상대 극좌표: @거리<각도 또는 거리<각도
		int angleIdx = input.IndexOf('<');
		if (angleIdx > 0)
		{
			string distStr = input.Substring(0, angleIdx);
			if (distStr.StartsWith('@'))
			{
				distStr = distStr.Substring(1);
			}
			string angleStr = input.Substring(angleIdx + 1);

			if (double.TryParse(distStr, out double dist) && double.TryParse(angleStr, out double angleDeg))
			{
				double rad = angleDeg * Math.PI / 180.0;
				double dx = dist * Math.Cos(rad);
				double dy = dist * Math.Sin(rad);
				double baseX = lastPoint?.X ?? 0.0;
				double baseY = lastPoint?.Y ?? 0.0;
				result = (baseX + dx, baseY + dy);
				return true;
			}
			return false;
		}

		// 2. 상대 직교 좌표: @dx,dy
		if (input.StartsWith('@'))
		{
			string sub = input.Substring(1);
			string[] parts = sub.Split(',');
			if (parts.Length == 2 && double.TryParse(parts[0], out double dx) && double.TryParse(parts[1], out double dy))
			{
				double baseX = lastPoint?.X ?? 0.0;
				double baseY = lastPoint?.Y ?? 0.0;
				result = (baseX + dx, baseY + dy);
				return true;
			}
			return false;
		}

		// 3. 절대 좌표: x,y
		if (input.Contains(','))
		{
			string[] parts = input.Split(',');
			if (parts.Length == 2 && double.TryParse(parts[0], out double x) && double.TryParse(parts[1], out double y))
			{
				result = (x, y);
				return true;
			}
			return false;
		}

		// 4. 거리 숫자만 입력: 마지막 점에서 커서 방향으로 그 거리
		if (double.TryParse(input, out double d))
		{
			if (lastPoint.HasValue)
			{
				double dirX = 1.0, dirY = 0.0;
				if (cursorPoint.HasValue)
				{
					dirX = cursorPoint.Value.X - lastPoint.Value.X;
					dirY = cursorPoint.Value.Y - lastPoint.Value.Y;
				}

				if (ortho)
				{
					if (Math.Abs(dirX) >= Math.Abs(dirY))
					{
						dirY = 0;
						dirX = dirX >= 0 ? 1 : -1;
					}
					else
					{
						dirX = 0;
						dirY = dirY >= 0 ? 1 : -1;
					}
				}
				else
				{
					double len = Math.Sqrt(dirX * dirX + dirY * dirY);
					if (len > 1e-9)
					{
						dirX /= len;
						dirY /= len;
					}
					else
					{
						dirX = 1;
						dirY = 0;
					}
				}

				result = (lastPoint.Value.X + dirX * d, lastPoint.Value.Y + dirY * d);
				return true;
			}
			else
			{
				result = (d, 0.0);
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// 기준점 대비 현재 좌표에 직교(수평/수직) 구속조건을 적용한다.
	/// </summary>
	public static (double X, double Y) ApplyOrtho((double X, double Y) basePt, (double X, double Y) current)
	{
		double dx = current.X - basePt.X;
		double dy = current.Y - basePt.Y;
		if (Math.Abs(dx) >= Math.Abs(dy))
		{
			return (current.X, basePt.Y);
		}
		else
		{
			return (basePt.X, current.Y);
		}
	}
}

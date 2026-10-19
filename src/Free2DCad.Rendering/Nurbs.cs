namespace Free2DCad.Rendering;

/// <summary>
/// NURBS(스플라인) 곡선을 선분 점들로 바꾼다 (De Boor 알고리즘).
/// ACadSharp의 TryPolygonalVertexes는 '닫힘·주기(Periodic)' 스플라인의 첫 점을 (0,0)으로 계산해
/// 도면에 원점까지 뻗는 긴 선이 생겼다 (test-3). 매듭 벡터를 그대로 따라 평가하므로 주기 플래그와 무관하다.
/// </summary>
public static class Nurbs
{
	/// <summary>
	/// degree 차수, knots 매듭, ctrl 제어점(x,y), weights 가중치(없으면 모두 1).
	/// 서로 다른 매듭 구간마다 segmentsPerSpan개 선분. 입력이 맞지 않으면 null.
	/// </summary>
	public static List<(double X, double Y)>? Tessellate(int degree, IReadOnlyList<double> knots, IReadOnlyList<(double X, double Y)> ctrl, IReadOnlyList<double>? weights, int segmentsPerSpan = 3, int maxPoints = 200)
	{
		int n = ctrl.Count;
		int p = degree;
		if (n < 2 || p < 1 || knots.Count != n + p + 1)
		{
			return null;
		}
		bool rational = weights != null && weights.Count == n && weights.Any(w => Math.Abs(w - 1) > 1e-12);

		double t0 = knots[p];
		double t1 = knots[n];
		if (!(t1 > t0))
		{
			return null;
		}

		// 서로 다른 매듭 구간 [knots[i], knots[i+1]] (길이 > 0) 마다 나눈다
		var spans = new List<(double A, double B)>();
		for (int i = p; i < n; i++)
		{
			if (knots[i + 1] - knots[i] > 1e-12)
			{
				spans.Add((knots[i], knots[i + 1]));
			}
		}
		// 구간이 적으면 더 촘촘히 (구간당 최대 16, 전체 maxPoints 이내): 3점짜리 구간 하나는 꺾인 선처럼 보였다
		int per = Math.Max(1, Math.Min(Math.Max(segmentsPerSpan, 16), maxPoints / Math.Max(spans.Count, 1)));

		var pts = new List<(double X, double Y)>(spans.Count * per + 1);
		var d = new double[p + 1, 3];
		foreach (var (a, b) in spans)
		{
			for (int s = 0; s < per; s++)
			{
				pts.Add(Evaluate(a + (b - a) * s / per));
			}
		}
		pts.Add(Evaluate(t1));
		return pts;

		(double X, double Y) Evaluate(double t)
		{
			// t가 속한 구간 k: knots[k] <= t < knots[k+1] (끝점은 마지막 유효 구간)
			int k = n - 1;
			if (t < t1)
			{
				int lo = p, hi = n;
				while (hi - lo > 1)
				{
					int mid = (lo + hi) / 2;
					if (knots[mid] <= t) lo = mid; else hi = mid;
				}
				k = lo;
			}
			for (int j = 0; j <= p; j++)
			{
				int idx = k - p + j;
				double w = rational ? weights![idx] : 1.0;
				d[j, 0] = ctrl[idx].X * w;
				d[j, 1] = ctrl[idx].Y * w;
				d[j, 2] = w;
			}
			for (int r = 1; r <= p; r++)
			{
				for (int j = p; j >= r; j--)
				{
					int i = k - p + j;
					double denom = knots[i + p - r + 1] - knots[i];
					double alpha = denom > 1e-15 ? (t - knots[i]) / denom : 0;
					for (int c = 0; c < 3; c++)
					{
						d[j, c] = (1 - alpha) * d[j - 1, c] + alpha * d[j, c];
					}
				}
			}
			double wt = d[p, 2];
			return Math.Abs(wt) > 1e-15 ? (d[p, 0] / wt, d[p, 1] / wt) : (d[p, 0], d[p, 1]);
		}
	}
}

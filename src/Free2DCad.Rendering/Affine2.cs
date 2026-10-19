namespace Free2DCad.Rendering;

/// <summary>
/// 2D 아핀 변환 (double 정밀도). x' = A*x + C*y + E, y' = B*x + D*y + F
/// </summary>
public readonly record struct Affine2(double A, double B, double C, double D, double E, double F)
{
	public static readonly Affine2 Identity = new(1, 0, 0, 1, 0, 0);

	public static Affine2 Translation(double x, double y) => new(1, 0, 0, 1, x, y);

	public static Affine2 Rotation(double radians)
	{
		double c = Math.Cos(radians), s = Math.Sin(radians);
		return new(c, s, -s, c, 0, 0);
	}

	public static Affine2 Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

	/// <summary>this를 먼저 적용하고 그다음 <paramref name="then"/>을 적용하는 변환.</summary>
	public Affine2 Then(Affine2 then) => new(
		then.A * this.A + then.C * this.B,
		then.B * this.A + then.D * this.B,
		then.A * this.C + then.C * this.D,
		then.B * this.C + then.D * this.D,
		then.A * this.E + then.C * this.F + then.E,
		then.B * this.E + then.D * this.F + then.F);

	public (double X, double Y) Apply(double x, double y) => (this.A * x + this.C * y + this.E, this.B * x + this.D * y + this.F);

	/// <summary>로컬 X축이 변환된 방향의 각도 (라디안).</summary>
	public double RotationAngle => Math.Atan2(this.B, this.A);

	/// <summary>로컬 Y축 방향 길이 배율 (문자 높이 변환용).</summary>
	public double ScaleY => Math.Sqrt(this.C * this.C + this.D * this.D);

	public double ScaleX => Math.Sqrt(this.A * this.A + this.B * this.B);

	/// <summary>좌우가 뒤집히는 변환인지 (행렬식 음수).</summary>
	public bool IsMirrored => this.A * this.D - this.B * this.C < 0;
}

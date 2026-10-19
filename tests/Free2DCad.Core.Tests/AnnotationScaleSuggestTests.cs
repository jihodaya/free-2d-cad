using Free2DCad.Core.Styles;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>주석 축척 자동 제안: 그린 범위가 A3(420×297)의 2배를 넘으면 범위가 들어가는 흔한 축척.</summary>
public class AnnotationScaleSuggestTests
{
	[Theory]
	[InlineData(300, 200, null)]     // 용지 안 → 제안 없음
	[InlineData(800, 500, null)]     // 2배 안 → 제안 없음
	[InlineData(1200, 800, 5.0)]     // 약 2.9배 → 1:5
	[InlineData(4200, 2000, 10.0)]   // 10배 → 1:10
	[InlineData(9000, 1000, 50.0)]   // 약 21배 → 1:50
	[InlineData(251800, 90000, 500.0)] // 아주 큼 → 1:500
	public void Suggest_SmallestCommonScaleThatFitsA3(double w, double h, double? expected)
	{
		Assert.Equal(expected, AnnotationScale.Suggest(w, h, 420, 297));
	}
}

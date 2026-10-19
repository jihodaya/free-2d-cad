using Xunit;
using Xunit.Abstractions;

namespace Free2DCad.Core.Tests;

public class StyleDumpTests
{
	private readonly ITestOutputHelper _out;

	public StyleDumpTests(ITestOutputHelper o) => this._out = o;

	[Fact]
	public void DumpTemplateDimStyle()
	{
		var doc = EditorDocument.New();
		var h = doc.Document.Header;
		this._out.WriteLine($"current={h.CurrentDimensionStyleName}");
		foreach (var s in doc.Document.DimensionStyles)
		{
			this._out.WriteLine($"{s.Name}: adec={s.AngularDecimalPlaces} dec={s.DecimalPlaces} clrd={s.DimensionLineColor} clre={s.ExtensionLineColor} clrt={s.TextColor} txt={s.TextHeight} asz={s.ArrowSize} scale={s.ScaleFactor} blk={s.ArrowBlock?.Name} blk1={s.DimArrow1?.Name} style={s.Style?.Name}/{s.Style?.Filename} gap={s.DimensionLineGap} tad={s.TextVerticalAlignment} tvp={s.TextVerticalPosition} tix={s.TextInsideExtensions} tofl={s.TextOutsideExtensions} tih={s.TextInsideHorizontal} toh={s.TextOutsideHorizontal} azin={s.AngularZeroHandling} dimzin={s.ZeroHandling}");
		}
	}
}

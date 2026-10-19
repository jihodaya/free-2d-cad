using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System.IO;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>하이퍼링크 (XData PE_URL, 기존 CAD 형식: 주소 문자열 하나).</summary>
public class HyperlinkTests
{
	[Fact]
	public void SetUndoAndRoundTrip()
	{
		var doc = EditorDocument.New();
		var l = new Line(XYZ.Zero, new XYZ(10, 0, 0));
		doc.AddEntities(l);
		doc.Execute(Hyperlinks.SetAction(new[] { l }, "https://example.com/a"));
		Assert.Equal("https://example.com/a", Hyperlinks.Get(l));
		string dir = Path.Combine(Path.GetTempPath(), "Free2DCad_Url_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try
		{
			string path = Path.Combine(dir, "u.dwg");
			doc.Save(path);
			var re = DwgReader.Read(path);
			Assert.Equal("https://example.com/a", Hyperlinks.Get(re.Entities.OfType<Line>().Single()));
		}
		finally
		{
			try { Directory.Delete(dir, true); } catch { }
		}
		doc.Undo();
		Assert.Equal("", Hyperlinks.Get(l));
		Assert.False(l.ExtendedData.TryGet(Hyperlinks.AppName, out _));
	}
}

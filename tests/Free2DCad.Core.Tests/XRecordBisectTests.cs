using ACadSharp.Objects;
using Xunit;

namespace Free2DCad.Core.Tests;

public class XRecordBisectTests
{
	[Fact]
	public void SaveVariants()
	{
		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		if (dir == null) return;
		void Save(string name, Action<XRecord> fill)
		{
			var doc = EditorDocument.New();
			var d = new CadDictionary("F2D_TEST");
			doc.Document.RootDictionary.Add("F2D_TEST", d);
			var rec = new XRecord("R1");
			fill(rec);
			d.Add("R1", rec);
			doc.Save(Path.Combine(dir, name + ".dwg"), ACadSharp.ACadVersion.AC1018);
		}
		// Save("x_emptystr", r => { r.CreateEntry(1, string.Empty); r.CreateEntry(3, "0"); });
		var d2 = EditorDocument.New();
		Free2DCad.Core.Styles.LayerStates.Save(d2, "S1");
		d2.Save(Path.Combine(dir, "x_state_fixed.dwg"), ACadSharp.ACadVersion.AC1018);
	}
}

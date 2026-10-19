using ACadSharp.Entities;
using ACadSharp.Objects;
using Xunit;
using Xunit.Abstractions;

namespace Free2DCad.Core.Tests;

public class RefDumpTests
{
	private readonly ITestOutputHelper _o;

	public RefDumpTests(ITestOutputHelper o) => this._o = o;

	[Fact]
	public void Dump()
	{
		string? path = Environment.GetEnvironmentVariable("F2D_REF_DWG");
		if (path == null || !File.Exists(path)) return;
		var doc = EditorDocument.Open(path).Document;
		foreach (var e in doc.ModelSpace.Entities)
		{
			this._o.WriteLine($"ENT {e.GetType().Name} {e.ObjectName} handle={e.Handle:X}");
			if (e is MultiLeader ml)
			{
				foreach (var p in typeof(MultiLeader).GetProperties())
				{
					try { this._o.WriteLine($"  {p.Name} = {p.GetValue(ml)}"); } catch { }
				}
				var cd = ml.ContextData;
				foreach (var p in cd.GetType().GetProperties())
				{
					try { this._o.WriteLine($"  CD.{p.Name} = {p.GetValue(cd)}"); } catch { }
				}
				foreach (var root in cd.LeaderRoots)
				{
					foreach (var p in root.GetType().GetProperties()) { try { this._o.WriteLine($"    ROOT.{p.Name} = {p.GetValue(root)}"); } catch { } }
					foreach (var line in root.Lines)
					{
						foreach (var p in line.GetType().GetProperties()) { try { this._o.WriteLine($"      LINE.{p.Name} = {p.GetValue(line)}"); } catch { } }
						foreach (var pt in line.Points) this._o.WriteLine($"        PT {pt}");
					}
				}
			}
			if (e is Dimension d)
			{
				foreach (var p in d.GetType().GetProperties())
				{
					try { this._o.WriteLine($"  {p.Name} = {p.GetValue(d)}"); } catch { }
				}
			}
		}
		this._o.WriteLine("LAYERS ext dict: " + (doc.Layers.XDictionary != null));
		void DumpDict(CadDictionary d, string ind)
		{
			foreach (var item in d)
			{
				this._o.WriteLine($"{ind}{item.GetType().Name} {item.Name}");
				if (item is CadDictionary sub) DumpDict(sub, ind + "  ");
				if (item is XRecord xr) foreach (var en in xr.Entries) this._o.WriteLine($"{ind}    [{en.Code}] {en.Value}");
			}
		}
		if (doc.Layers.XDictionary != null) DumpDict(doc.Layers.XDictionary, "  ");
		this._o.WriteLine("ROOT:");
		foreach (var item in doc.RootDictionary) this._o.WriteLine($"  {item.GetType().Name} {item.Name}");
		this._o.WriteLine("MLEADERSTYLES:");
		foreach (var s in doc.MLeaderStyles) this._o.WriteLine($"  {s.Name}");
		this._o.WriteLine("CLASSES:");
		foreach (var c in doc.Classes) this._o.WriteLine($"  {c.DxfName} {c.CppClassName} {c.ClassNumber}");
	}
}

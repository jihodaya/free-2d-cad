using ACadSharp;
using ACadSharp.IO;
using Xunit;
using Xunit.Abstractions;

namespace Free2DCad.Core.Tests;

/// <summary>다른 이름으로 저장의 모든 형식(DWG 2000~2018, DXF 2000~2018, DWT)으로 저장한 뒤 다시 읽어 객체 수가 같은지.</summary>
public class SaveFormatTests
{
	private readonly ITestOutputHelper _out;
	public SaveFormatTests(ITestOutputHelper o) => this._out = o;

	public static IEnumerable<object[]> Formats() => new[]
	{
		new object[] { ACadVersion.AC1015, "dwg" },
		new object[] { ACadVersion.AC1018, "dwg" },
		new object[] { ACadVersion.AC1024, "dwg" },
		new object[] { ACadVersion.AC1027, "dwg" },
		new object[] { ACadVersion.AC1032, "dwg" },
		new object[] { ACadVersion.AC1015, "dxf" },
		new object[] { ACadVersion.AC1018, "dxf" },
		new object[] { ACadVersion.AC1021, "dxf" },
		new object[] { ACadVersion.AC1024, "dxf" },
		new object[] { ACadVersion.AC1027, "dxf" },
		new object[] { ACadVersion.AC1032, "dxf" },
		new object[] { ACadVersion.AC1018, "dwt" },
	};

	[Theory]
	[MemberData(nameof(Formats))]
	public void SaveAndReload_Test2(ACadVersion version, string ext)
	{
		string src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "sample", "test-2.dwg");
		if (!File.Exists(src)) return;
		var doc = EditorDocument.Open(src);
		int before = doc.Document.Entities.Count();
		string outDir = Path.Combine(Path.GetTempPath(), "free2dcad-formats");
		Directory.CreateDirectory(outDir);
		string path = Path.Combine(outDir, $"test-2-{version}.{ext}");
		doc.Save(path, version);
		Assert.Equal(version, doc.SaveVersion);

		CadDocument back = ext == "dxf" ? DxfReader.Read(path) : DwgReader.Read(path);
		this._out.WriteLine($"{version} {ext}: {new FileInfo(path).Length / 1024} KB, entities {before} → {back.Entities.Count()}, header {back.Header.Version}");
		Assert.Equal(version, back.Header.Version);
		Assert.Equal(before, back.Entities.Count());
	}
}

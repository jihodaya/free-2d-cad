using ACadSharp;
using ACadSharp.IO;
using Free2DCad.Core.Styles;
using Xunit;

namespace Free2DCad.Core.Tests;

public class StylesTests
{
	[Fact]
	public void LinetypeLibrary_Load_UndoAndSaveReload()
	{
		var doc = EditorDocument.New();
		// 템플릿에 이미 있는 선종류는 건너뛰므로, 없는 것만 센다
		string[] names = LinetypeLibrary.All.Select(d => d.Name).ToArray();
		string[] missing = names.Where(n => !doc.Document.LineTypes.Contains(n)).ToArray();
		Assert.NotEmpty(missing);
		bool hadHidden = doc.Document.LineTypes.Contains("HIDDEN");
		int added = LinetypeLibrary.Load(doc, names);
		Assert.Equal(missing.Length, added);
		Assert.True(doc.Document.LineTypes.Contains("HIDDEN"));

		string path = Path.Combine(Path.GetTempPath(), $"f2d-lt-{Guid.NewGuid():N}.dwg");
		try
		{
			doc.Save(path);
			CadDocument loaded = DwgReader.Read(path);
			foreach (string n in missing)
			{
				Assert.True(loaded.LineTypes.Contains(n), n);
			}
			var iso = loaded.LineTypes[missing[0]];
			Assert.Equal(LinetypeLibrary.Find(missing[0])!.Pattern.Length, iso.Segments.Count());
		}
		finally
		{
			File.Delete(path);
		}

		doc.Undo();
		Assert.All(missing, n => Assert.False(doc.Document.LineTypes.Contains(n)));
		Assert.Equal(hadHidden, doc.Document.LineTypes.Contains("HIDDEN"));
	}
}

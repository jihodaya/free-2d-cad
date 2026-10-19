using ACadSharp.Blocks;
using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Blocks;
using Free2DCad.Core.Commands;
using Free2DCad.Rendering;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>XREF: 부착·그리기·저장(경로만)·상대 경로로 찾기·언로드·결합·분리.</summary>
public class XrefTests
{
	private static string MakeRefFile(string dir)
	{
		var refDoc = EditorDocument.New();
		refDoc.AddEntities(new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), new Circle { Center = new XYZ(5, 5, 0), Radius = 2 });
		string path = Path.Combine(dir, "ref_part.dwg");
		refDoc.Save(path);
		return path;
	}

	[Fact]
	public void Attach_Render_Save_Reopen_Unload_Bind_Detach()
	{
		string dir = Path.Combine(Path.GetTempPath(), "f2d_xref_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try
		{
			string refPath = MakeRefFile(dir);
			var host = EditorDocument.New();
			host.Save(Path.Combine(dir, "host.dwg"));

			var br = Xrefs.Attach(host, refPath, false, Xrefs.PathMode.Relative);
			Assert.True(br.Flags.HasFlag(BlockTypeFlags.XRef));
			Assert.Equal("ref_part.dwg", br.BlockEntity.XRefPath); // 같은 폴더 → 상대 경로
			var cmd = new XrefPlaceCommand(br, null, null, null, null);
			cmd.Start(new CommandContext(host));
			cmd.OnPoint(100, 0);
			cmd.OnEnter(); // X 축척 1
			cmd.OnEnter(); // Y 축척 = X
			cmd.OnEnter(); // 회전 0
			Assert.True(cmd.IsFinished);
			var ins = host.Document.Entities.OfType<Insert>().Single();
			Assert.Equal(1, Xrefs.RefCount(host.Document, br));

			// 참조 도면의 선·원이 삽입 위치에 그려짐
			var scene = SceneBuilder.Build(host.Document);
			Assert.Contains(scene.Polylines, p => Math.Abs(p.Points[0] - 100) < 1e-6 && Math.Abs(p.Points[2] - 110) < 1e-6);
			Assert.Equal("로드됨", XrefLoader.StatusOf(host.Document, br));

			// 저장 → 다시 열기: 경로만 저장되고 내용은 다시 읽힘
			host.Save(Path.Combine(dir, "host.dwg"));
			var re = EditorDocument.Open(Path.Combine(dir, "host.dwg"));
			var rbr = Xrefs.List(re.Document).Single();
			Assert.Empty(rbr.Entities);
			Assert.Equal("로드됨", XrefLoader.StatusOf(re.Document, rbr));

			// 언로드 → 안 그려짐, 다시 로드 → 그려짐
			Xrefs.SetUnloaded(re, rbr, true);
			Assert.Equal("언로드됨", XrefLoader.StatusOf(re.Document, rbr));
			Assert.DoesNotContain(SceneBuilder.Build(re.Document).Polylines, p => Math.Abs(p.Points[0] - 100) < 1e-6);
			Xrefs.Reload(re, rbr);
			Assert.Equal("로드됨", XrefLoader.StatusOf(re.Document, rbr));

			// 결합 → 일반 블록 (내용 복사, 외부 참조 아님)
			Assert.True(Xrefs.Bind(re, rbr));
			Assert.False(XrefLoader.IsXref(rbr));
			Assert.Equal(2, rbr.Entities.Count);
			re.Undo();
			Assert.True(XrefLoader.IsXref(rbr));

			// 분리 → 삽입·정의 없음, 되돌리기로 복원
			Xrefs.Detach(re, rbr);
			Assert.Empty(Xrefs.List(re.Document));
			Assert.Empty(re.Document.Entities.OfType<Insert>());
			re.Undo();
			Assert.Single(Xrefs.List(re.Document));
		}
		finally
		{
			XrefLoader.Invalidate();
			try { Directory.Delete(dir, true); } catch (Exception) { }
		}
	}

	[Fact]
	public void Sample_ForStd()
	{
		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		if (dir == null) return;
		string refPath = MakeRefFile(dir);
		var host = EditorDocument.New();
		host.Save(Path.Combine(dir, "xref_host.dwg"));
		var br = Xrefs.Attach(host, refPath, false, Xrefs.PathMode.Relative);
		host.AddEntities(Xrefs.MakeInsert(br, new XYZ(100, 0, 0)));
		host.Save(Path.Combine(dir, "xref_host.dwg"), ACadSharp.ACadVersion.AC1018);
		XrefLoader.Invalidate();
	}

	[Fact]
	public void Resolve_UsesExtraSearchPath_LikeStd()
	{
		// 기존 CAD Xref 관리자 "외부참조 검색 추가 경로": 저장 경로·도면 폴더에 없으면 그 폴더에서 같은 이름을 찾는다
		string root = Path.Combine(Path.GetTempPath(), "f2d_xrefsp_" + Guid.NewGuid().ToString("N"));
		string hostDir = Path.Combine(root, "host"), libDir = Path.Combine(root, "lib");
		Directory.CreateDirectory(hostDir);
		Directory.CreateDirectory(libDir);
		string old = XrefLoader.SearchPaths;
		try
		{
			string refPath = MakeRefFile(libDir);
			var host = EditorDocument.New();
			host.Save(Path.Combine(hostDir, "host.dwg"));
			var br = Xrefs.Attach(host, refPath, false, Xrefs.PathMode.Relative);
			br.BlockEntity.XRefPath = "ref_part.dwg"; // 저장 경로가 도면 폴더 기준인데 파일은 다른 폴더에 있음
			XrefLoader.SearchPaths = AppContext.BaseDirectory;
			Assert.Null(XrefLoader.Resolve(host.Document, br));
			XrefLoader.SearchPaths = AppContext.BaseDirectory + ";" + libDir;
			Assert.Equal(Path.GetFullPath(refPath), XrefLoader.Resolve(host.Document, br));
		}
		finally
		{
			XrefLoader.SearchPaths = old;
			try { Directory.Delete(root, true); } catch (Exception) { }
		}
	}
}

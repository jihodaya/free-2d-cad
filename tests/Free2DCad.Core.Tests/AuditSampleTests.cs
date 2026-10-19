using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>기존 CAD AUDIT 확인용 견본 (F2D_AUDIT_OUT 폴더를 주면 그 안에 저장, 없으면 임시 폴더에 저장 후 다시 읽기).</summary>
public class AuditSampleTests
{
	private static void Feed(EditorCommand cmd, params string[] inputs)
	{
		foreach (string s in inputs)
		{
			if (cmd.IsFinished) break;
			if (s == "") cmd.OnEnter();
			else if (s.StartsWith("p:"))
			{
				var xy = s[2..].Split(',');
				cmd.OnPoint(double.Parse(xy[0]), double.Parse(xy[1]));
			}
			else cmd.OnText(s);
		}
	}

	[Fact]
	public void NewEntities_SaveAndReload()
	{
		var doc = EditorDocument.New();
		var ctx = new CommandContext(doc);
		Entity? picked = null;
		ctx.Picker = (_, _) => picked;
		void Run(EditorCommand c, params string[] inputs) { c.Start(ctx); Feed(c, inputs); }

		Run(new CircleCommand { StartMode = CircleCommand.CircleMode.ThreePoint }, "p:0,0", "p:20,20", "p:40,0");
		Run(new ArcCommand(), "p:100,0", "p:120,20", "p:140,0");
		picked = doc.Document.Entities.OfType<Arc>().Last();
		Run(new DimArcCommand(), "p:120,20", "p:120,30");
		Run(new DimOrdinateCommand(), "p:50,50", "p:50,80");
		Run(new DimLinearCommand(), "p:0,100", "p:50,100", "p:25,110");
		Run(new DimChainCommand(true), "p:90,100", "");
		Run(new LeaderCommand("QLEADER"), "p:60,60", "p:80,80", "", "지시선 문자", "");
		Run(new PolygonCommand(), "6", "p:200,0", "I", "15");
		Run(new EllipseCommand(), "p:250,0", "p:290,0", "p:270,8");
		Run(new SplineCommand(), "p:300,0", "p:320,20", "p:340,0", "p:360,20", "", "", ""); // 점 → 시작·끝 접선 Enter
		Run(new PointCommand(), "M", "p:10,200", "p:20,200", ""); // 다중(M): 여러 점
		Run(new XLineCommand(false), "p:0,300", "p:10,310", "");
		Run(new XLineCommand(true), "p:0,320", "p:10,330", "");
		Run(new DonutCommand(), "4", "10", "p:100,200", "");
		Run(new RevCloudCommand(), "A", "5", "p:150,200", "p:200,240");
		Run(new SolidCommand(), "p:250,200", "p:270,200", "p:250,220", "p:270,220");
		Run(new PlineCommand(), "p:300,200", "W", "1", "", "p:320,200", "A", "p:320,220", "");
		Run(new AttDefCommand(), "TAG1", "값 입력", "기본", "2.5", "p:400,200");
		var l1 = new Line(new XYZ(400, 190, 0), new XYZ(420, 190, 0));
		doc.AddEntities(l1);
		var blk = new BlockCommand();
		blk.Start(ctx);
		Feed(blk, "ATTBLK", "p:400,190");
		blk.OnSelection(new Entity[] { doc.Document.Entities.OfType<AttributeDefinition>().Single(), l1 });
		blk.OnEnter();
		Run(new InsertCommand(), "ATTBLK", "p:450,190", "", "", "A-1");
		var g1 = new Line(new XYZ(0, 400, 0), new XYZ(10, 400, 0));
		var g2 = new Line(new XYZ(0, 410, 0), new XYZ(10, 410, 0));
		doc.AddEntities(g1, g2);
		var grp = new GroupCommand(false);
		grp.Start(new CommandContext(doc, new HashSet<Entity> { g1, g2 }));
		grp.OnText("그룹1");
		var q = new GroupCommand(true);
		q.Start(new CommandContext(doc, new HashSet<Entity> { g1 }));
		DrawOrderCommand.Apply(doc, new[] { g2 }, DrawOrderCommand.Placement.Back, null);

		string dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT") ?? Path.GetTempPath();
		foreach (var (name, ver) in new[] { ("audit_new_2004.dwg", ACadSharp.ACadVersion.AC1018), ("audit_new_2018.dwg", ACadSharp.ACadVersion.AC1032), ("audit_new_2018.dxf", ACadSharp.ACadVersion.AC1032) })
		{
			string path = Path.Combine(dir, name);
			doc.Save(path, ver);
			var re = EditorDocument.Open(path);
			Assert.Single(re.Document.Entities.OfType<DimensionArc>());
			Assert.Single(re.Document.Entities.OfType<Leader>());
			Assert.Equal(2, re.Document.Groups.Count());
			Assert.True(re.Document.Entities.OfType<Point>().Count() >= 2);
			if (Environment.GetEnvironmentVariable("F2D_AUDIT_OUT") == null) File.Delete(path);
		}
	}
}

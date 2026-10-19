using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>기존 CAD AUDIT·비교용 견본 2 (F2D_AUDIT_OUT이 있을 때만 파일로 남김).</summary>
public class AuditSample2Tests
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
	public void Sample2_SaveAndReload()
	{
		var doc = EditorDocument.New();
		Entity? pick = null;
		var queue = new Queue<Entity>();
		var ctx = new CommandContext(doc) { Picker = (_, _) => queue.Count > 0 ? queue.Dequeue() : pick };
		void Run(EditorCommand c, params string[] inputs) { c.Start(ctx); Feed(c, inputs); }

		// 각도 치수 3가지 (기존 CAD 견본과 같은 배치)
		Line L(double x1, double y1, double x2, double y2) { var l = new Line(new XYZ(x1 * 0.15, -y1 * 0.15, 0), new XYZ(x2 * 0.15, -y2 * 0.15, 0)); doc.AddEntities(l); return l; }
		var a = L(68, 108, 325, 370);
		var b = L(325, 370, 513, 133);
		var c = L(513, 133, 890, 500);
		var d = L(667, 510, 1007, 115);
		void Ang(Line l1, Line l2, double x, double y) { queue.Enqueue(l1); queue.Enqueue(l2); Run(new DimAngularCommand(), "p:0,0", "p:0,0", $"p:{x * 0.15},{-y * 0.15}"); }
		Ang(a, b, 313, 170);
		Ang(b, c, 520, 345);
		Ang(c, d, 780, 545);

		// 다중선, 와이프아웃
		Run(new MLineCommand(), "p:0,-150", "p:60,-150", "p:90,-120", "");
		doc.AddEntities(new Line(new XYZ(100, -150, 0), new XYZ(200, -150, 0)));
		doc.AddEntities(WipeoutCommand.Create(new List<XY> { new(120, -170), new(180, -170), new(180, -130), new(120, -130) }));

		// 꺾기 반지름
		var arc = new Arc { Center = new XYZ(300, -150, 0), Radius = 40, StartAngle = 0, EndAngle = Math.PI / 2 };
		doc.AddEntities(arc);
		pick = arc;
		Run(new DimJoggedCommand(), "p:330,-120", "p:290,-140", "p:340,-110", "p:315,-135");

		// 연관 배열 (직사각형) + 도면층 상태
		var circ = new Circle { Center = new XYZ(0, -250, 0), Radius = 5 };
		doc.AddEntities(circ);
		var arr = new ArrayCommand(ArrayCommand.ArrayKind.Rect);
		arr.Start(new CommandContext(doc, new HashSet<Entity> { circ }));
		Feed(arr, "COU", "4", "2", "S", "20", "20", "X");
		Free2DCad.Core.Styles.LayerStates.Save(doc, "견본 상태");

		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		string outDir = dir ?? Path.GetTempPath();
		foreach (var (name, ver) in new[] { ("audit3_2004.dwg", ACadSharp.ACadVersion.AC1018), ("audit3_2018.dwg", ACadSharp.ACadVersion.AC1032), ("audit3_2018.dxf", ACadSharp.ACadVersion.AC1032) })
		{
			string path = Path.Combine(outDir, name);
			doc.Save(path, ver);
			var re = EditorDocument.Open(path);
			Assert.Equal(3, re.Document.Entities.OfType<DimensionAngular2Line>().Count());
			Assert.Single(re.Document.Entities.OfType<MLine>());
			Assert.Single(re.Document.Entities.OfType<Wipeout>());
			Assert.Contains(re.Document.Groups, g => (g.Description ?? "").StartsWith("F2DARRAY"));
			Assert.NotEmpty(Free2DCad.Core.Styles.LayerStates.List(re.Document));
			if (dir == null) File.Delete(path);
		}
	}
}

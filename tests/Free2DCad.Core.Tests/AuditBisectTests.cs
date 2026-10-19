using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>기존 CAD 읽기 오류 원인 찾기용: 기능 하나씩 따로 저장 (F2D_AUDIT_OUT이 있을 때만).</summary>
public class AuditBisectTests
{
	[Fact]
	public void SaveEachFeature()
	{
		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		if (dir == null) return;
		void Save(string name, Action<EditorDocument> build)
		{
			var doc = EditorDocument.New();
			build(doc);
			doc.Save(Path.Combine(dir, name + ".dwg"), ACadSharp.ACadVersion.AC1018);
		}
		Save("b_mline", doc => doc.AddEntities(MLineCommand.Build(new List<XY> { new(0, 0), new(60, 0), new(90, 30) }, false, doc.Document.MLineStyles.First(), 20, MLineJustification.Top)!));
		Save("b_wipeout", doc => doc.AddEntities(WipeoutCommand.Create(new List<XY> { new(0, 0), new(60, 0), new(60, 40), new(0, 40) })));
		Save("b_layerstate", doc => { doc.AddLayer("A"); Free2DCad.Core.Styles.LayerStates.Save(doc, "S1"); });
		Save("b_group", doc =>
		{
			var c = new Circle { Center = XYZ.Zero, Radius = 5 };
			doc.AddEntities(c);
			var arr = new ArrayCommand(ArrayCommand.ArrayKind.Rect);
			arr.Start(new CommandContext(doc, new HashSet<Entity> { c }));
			foreach (var t in new[] { "COU", "3", "1", "X" }) arr.OnText(t);
		});
		Save("b_jogged", doc =>
		{
			var arc = new Arc { Center = XYZ.Zero, Radius = 40, StartAngle = 0, EndAngle = Math.PI / 2 };
			doc.AddEntities(arc);
			var cmd = new DimJoggedCommand();
			cmd.Start(new CommandContext(doc) { Picker = (_, _) => arc });
			cmd.OnPoint(30, 30);
			cmd.OnPoint(-10, 10);
			cmd.OnPoint(40, 40);
			cmd.OnPoint(15, 15);
		});
		Save("b_angular", doc =>
		{
			var l1 = new Line(XYZ.Zero, new XYZ(100, 0, 0));
			var l2 = new Line(XYZ.Zero, new XYZ(0, 100, 0));
			doc.AddEntities(l1, l2);
			var q = new Queue<Entity>(new Entity[] { l1, l2 });
			var cmd = new DimAngularCommand();
			cmd.Start(new CommandContext(doc) { Picker = (_, _) => q.Dequeue() });
			cmd.OnPoint(0, 0);
			cmd.OnPoint(0, 0);
			cmd.OnPoint(40, 40);
		});
	}
}

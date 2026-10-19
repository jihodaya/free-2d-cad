using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>ARRAYEDIT, SPLINEDIT, DIMJOGGED, DIMEDIT, SCALETEXT, CENTERMARK/LINE, DIMSPACE, WBLOCK, ZOOM.</summary>
public class MoreCommands3Tests
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
	public void ArrayEdit_ChangesCount()
	{
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(0, 0, 0), Radius = 1 };
		doc.AddEntities(c);
		var a = new ArrayCommand(ArrayCommand.ArrayKind.Rect);
		a.Start(new CommandContext(doc, new HashSet<Entity> { c }));
		Feed(a, "COU", "3", "1", "X");
		Assert.Equal(3, doc.Document.Entities.OfType<Circle>().Count());
		Assert.NotNull(ArrayEditCommand.Find(doc, c));

		var e = new ArrayEditCommand();
		e.Start(new CommandContext(doc, new HashSet<Entity> { c }));
		Feed(e, "C", "5", "", "X");
		Assert.Equal(5, doc.Document.Entities.OfType<Circle>().Count());
		doc.Undo();
		Assert.Equal(3, doc.Document.Entities.OfType<Circle>().Count());
	}

	[Fact]
	public void SplineEdit_RefineKeepsShape_AndClose()
	{
		var sp = SplineCommand.Build(new List<XY> { new(0, 0), new(10, 10), new(20, 0), new(30, 10) }, controlVertices: true)!;
		var before = Enumerable.Range(0, 11).Select(i => SplineEditCommand.Evaluate(sp, i / 10.0)).ToList();
		int n = sp.ControlPoints.Count;
		SplineEditCommand.InsertKnot(sp, 0.37);
		Assert.Equal(n + 1, sp.ControlPoints.Count);
		var after = Enumerable.Range(0, 11).Select(i => SplineEditCommand.Evaluate(sp, i / 10.0)).ToList();
		for (int i = 0; i < 11; i++)
		{
			Assert.Equal(before[i].X, after[i].X, 6);
			Assert.Equal(before[i].Y, after[i].Y, 6);
		}

		var doc = EditorDocument.New();
		doc.AddEntities(sp);
		var cmd = new SplineEditCommand();
		cmd.Start(new CommandContext(doc, new HashSet<Entity> { sp }));
		cmd.OnText("C");
		Assert.True(sp.Flags.HasFlag(SplineFlags.Closed));
		cmd.OnText("U");
		Assert.False(sp.Flags.HasFlag(SplineFlags.Closed));
	}

	[Fact]
	public void DimJogged_CreatesRadiusWithJog()
	{
		var doc = EditorDocument.New();
		var arc = new Arc { Center = XYZ.Zero, Radius = 500, StartAngle = 0, EndAngle = Math.PI / 2 };
		doc.AddEntities(arc);
		var cmd = new DimJoggedCommand();
		cmd.Start(new CommandContext(doc) { Picker = (_, _) => arc });
		Feed(cmd, "p:350,350", "p:300,100", "p:420,300", "p:200,150");
		var d = doc.Document.Entities.OfType<DimensionLargeRadial>().Single();
		Assert.Equal(500, d.Measurement, 6);
		Assert.StartsWith("R500", d.GetMeasurementText());
		Assert.Equal(3, d.Block!.Entities.OfType<Line>().Count());

		// DWG·DXF 저장 후 다시 읽어도 꺾기 반지름 치수 그대로 (기존 CAD와 같은 객체)
		foreach (string ext in new[] { ".dwg", ".dxf" })
		{
			string path = Path.Combine(Path.GetTempPath(), "f2d_jog" + ext);
			doc.Save(path);
			var back = EditorDocument.Open(path).Document.Entities.OfType<DimensionLargeRadial>().Single();
			Assert.Equal(d.OverrideCenter.X, back.OverrideCenter.X, 6);
			Assert.Equal(d.JogPoint.Y, back.JogPoint.Y, 6);
			Assert.Equal(d.ChordPoint.X, back.ChordPoint.X, 6);
			File.Delete(path);
		}
	}

	[Fact]
	public void DimEdit_NewText_And_Home()
	{
		var doc = EditorDocument.New();
		var lin = new DimLinearCommand();
		lin.Start(new CommandContext(doc));
		Feed(lin, "p:0,0", "p:10,0", "p:5,5");
		var d = doc.Document.Entities.OfType<DimensionLinear>().Single();
		var ed = new DimEditCommand();
		ed.Start(new CommandContext(doc));
		Feed(ed, "N", "A-<>");
		ed.OnSelection(new[] { d });
		ed.OnEnter();
		Assert.Equal("A-<>", d.Text);
	}

	[Fact]
	public void ScaleText_And_TotalLength_And_CenterMark()
	{
		var doc = EditorDocument.New();
		var t = new TextEntity { Value = "가", Height = 2, InsertPoint = XYZ.Zero };
		doc.AddEntities(t);
		var st = new ScaleTextCommand();
		st.Start(new CommandContext(doc, new HashSet<Entity> { t }));
		Feed(st, "S", "2.5");
		Assert.Equal(5, t.Height, 6);

		var log = new List<string>();
		var l1 = new Line(XYZ.Zero, new XYZ(3, 4, 0));
		var circle = new Circle { Center = XYZ.Zero, Radius = 1 };
		doc.AddEntities(l1, circle);
		var tl = new TotalLengthCommand();
		tl.Start(new CommandContext(doc, new HashSet<Entity> { l1 }, print: log.Add));
		Assert.Contains(log, s => s.Contains("전체 길이 = 5"));

		Assert.Equal(6, CenterMarkCommand.Make(doc.Document, circle).Count);
		var cl = CenterLineCommand.Make(new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), new Line(new XYZ(10, 4, 0), new XYZ(0, 4, 0)))!;
		Assert.Equal(2, cl.StartPoint.Y, 6);
	}

	[Fact]
	public void DimSpace_EquallySpaces()
	{
		var doc = EditorDocument.New();
		Dimension Make(double y)
		{
			var c = new DimLinearCommand();
			c.Start(new CommandContext(doc));
			Feed(c, "p:0,0", "p:10,0", $"p:5,{y}");
			return doc.Document.Entities.OfType<DimensionLinear>().Last();
		}
		var b = Make(5);
		var d1 = Make(13);
		var d2 = Make(30);
		var cmd = new DimSpaceCommand();
		cmd.Start(new CommandContext(doc));
		cmd.OnSelection(new Entity[] { b });
		cmd.OnSelection(new Entity[] { d1, d2 });
		cmd.OnEnter();
		cmd.OnText("10");
		Assert.Equal(15, d1.DefinitionPoint.Y, 6);
		Assert.Equal(25, d2.DefinitionPoint.Y, 6);
	}

	[Fact]
	public void Wblock_ExportsMovedCopy()
	{
		var doc = EditorDocument.New();
		var l = new Line(new XYZ(100, 100, 0), new XYZ(110, 100, 0));
		doc.AddEntities(l);
		var exported = WblockCommand.Export(new[] { l }, new XYZ(100, 100, 0));
		var e = Assert.IsType<Line>(Assert.Single(exported.Document.ModelSpace.Entities));
		Assert.Equal(0, e.StartPoint.X, 6);
		Assert.Equal(100, l.StartPoint.X, 6);
		var path = Path.Combine(Path.GetTempPath(), $"wb_{Guid.NewGuid():N}.dwg");
		try
		{
			exported.Save(path);
			Assert.Single(EditorDocument.Open(path).Document.ModelSpace.Entities.OfType<Line>());
		}
		finally
		{
			File.Delete(path);
		}
	}

	private sealed class FakeView : IViewController
	{
		public List<string> Calls { get; } = new();
		public double ViewHeight => 100;
		public void ZoomExtents() => this.Calls.Add("E");
		public void ZoomWindow(double x1, double y1, double x2, double y2) => this.Calls.Add($"W{x1},{y1},{x2},{y2}");
		public void ZoomBy(double factor) => this.Calls.Add($"S{factor}");
		public void ZoomCenter(double x, double y, double? height) => this.Calls.Add($"C{x},{y},{height}");
		public bool ZoomPrevious() { this.Calls.Add("P"); return true; }
		public void PanBy(double dx, double dy) => this.Calls.Add($"PAN{dx},{dy}");
	}

	[Fact]
	public void Zoom_And_Pan_UseView()
	{
		var doc = EditorDocument.New();
		var v = new FakeView();
		void Run(EditorCommand c, params string[] inputs) { c.Start(new CommandContext(doc) { View = v }); Feed(c, inputs); }
		Run(new ZoomCommand(), "E");
		Run(new ZoomCommand(), "2X");
		Run(new ZoomCommand(), "p:0,0", "p:10,10");
		Run(new ZoomCommand(), "P");
		Run(new PanCommand(), "p:0,0", "p:5,-5");
		Assert.Equal(new[] { "E", "S2", "W0,0,10,10", "P", "PAN5,-5" }, v.Calls);
	}
}

public class RadiusPrefixTests
{
	[Fact]
	public void RadiusAndDiameter_HavePrefix_WithUserTextFlag()
	{
		var r = new DimensionRadius { DefinitionPoint = CSMath.XYZ.Zero, AngleVertex = new CSMath.XYZ(10, 0, 0), IsTextUserDefinedLocation = true };
		Assert.StartsWith("R", r.GetMeasurementText());
		var d = new DimensionDiameter { AngleVertex = new CSMath.XYZ(-10, 0, 0), DefinitionPoint = new CSMath.XYZ(10, 0, 0), IsTextUserDefinedLocation = true };
		Assert.StartsWith("∅", d.GetMeasurementText());
	}
}

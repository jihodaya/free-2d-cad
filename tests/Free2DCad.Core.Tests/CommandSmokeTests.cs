using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>
/// 모든 등록 명령을 여러 입력 순서로 실행해 예외가 나지 않는지 확인한다 (앱 튕김 방지용 연기 시험).
/// 화면처럼 입력마다 Preview를 부르고, 중간 Enter/Space, 옵션 글자, 취소, 빈 선택 등을 섞는다.
/// </summary>
public class CommandSmokeTests
{
	private static EditorDocument SampleDoc(out HashSet<Entity> selection)
	{
		var doc = EditorDocument.New();
		var line = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		var line2 = new Line(new XYZ(50, -50, 0), new XYZ(50, 50, 0));
		var circle = new Circle { Center = new XYZ(200, 0, 0), Radius = 30 };
		var arc = new Arc { Center = new XYZ(300, 0, 0), Radius = 20, StartAngle = 0, EndAngle = Math.PI };
		var pl = new LwPolyline { IsClosed = true };
		foreach (var (x, y) in new[] { (0.0, 100.0), (80.0, 100.0), (80.0, 160.0), (0.0, 160.0) })
		{
			pl.Vertices.Add(new LwPolyline.Vertex(new XY(x, y)));
		}
		var text = new TextEntity { Value = "시험", Height = 5, InsertPoint = new XYZ(0, 200, 0) };
		doc.AddEntities(line, line2, circle, arc, pl, text);
		selection = new HashSet<Entity> { line, circle };
		return doc;
	}

	/// <summary>입력 순서: p=점, e=Enter, t:글자, c=취소, v=Preview만.</summary>
	private static readonly string[][] Scripts =
	{
		new[] { "p:10,10", "p:60,40", "e" },
		new[] { "p:10,10", "p:60,40", "p:90,10", "e", "e" },
		new[] { "e", "e" },
		new[] { "p:0,0", "e" },
		new[] { "p:50,0", "p:200,30", "p:120,80", "p:130,90", "e" },
		new[] { "p:10,10", "t:10", "t:@20,0", "t:45", "e" },
		new[] { "t:R", "t:5", "p:100,0", "p:50,0", "e" },
		new[] { "p:10,10", "c" },
		new[] { "p:200,0", "p:230,0", "e", "p:300,20", "e" },
		new[] { "t:S", "p:0,0", "t:K", "t:U", "e" },
		new[] { "p:0,0", "p:0,0", "p:0,0", "e" }, // 같은 점 반복 (0 길이)
		new[] { "p:1220,300", "p:1300,300", "p:1260,260", "e" }, // 화면 시험과 같은 좌표
		new[] { "p:0,0", "p:10,0", "p:20,0", "e" }, // 일직선 세 점 (호/원 3점)
	};

	public static IEnumerable<object[]> Commands() =>
		CommandRegistry.All.Select(c => new object[] { c.Name });

	[Theory]
	[MemberData(nameof(Commands))]
	public void Command_DoesNotThrow_ForVariousInputs(string name)
	{
		var failures = new List<string>();
		for (int si = 0; si < Scripts.Length; si++)
		{
			foreach (bool withSelection in new[] { true, false })
			{
				var doc = SampleDoc(out var sel);
				var cmd = CommandRegistry.Create(name)!;
				var ctx = new CommandContext(doc, withSelection ? sel : new HashSet<Entity>(), print: _ => { });
				string step = "Start";
				try
				{
					cmd.Start(ctx);
					_ = cmd.Preview(5, 5).ToList();
					foreach (string op in Scripts[si])
					{
						if (cmd.IsFinished) break;
						step = op;
						if (op.StartsWith("p:"))
						{
							var xy = op[2..].Split(',').Select(double.Parse).ToArray();
							_ = cmd.Preview(xy[0] + 1, xy[1] + 1).ToList();
							cmd.OnPoint(xy[0], xy[1]);
							ctx.LastPoint = (xy[0], xy[1]);
						}
						else if (op.StartsWith("t:"))
						{
							cmd.OnText(op[2..]);
						}
						else if (op == "e")
						{
							cmd.OnEnter();
						}
						else if (op == "c")
						{
							cmd.OnCancel();
						}
						if (!cmd.IsFinished)
						{
							_ = cmd.Preview(33, 44).ToList();
						}
					}
					if (!cmd.IsFinished)
					{
						cmd.OnCancel();
					}
					// 만든 도형의 범위가 NaN/무한이면 화면 좌표계가 깨져 선이 모두 사라진다
					var ext = Free2DCad.Rendering.SceneBuilder.Build(doc.Document).Extents;
					if (double.IsNaN(ext.MinX) || double.IsNaN(ext.MinY) || double.IsNaN(ext.MaxX) || double.IsNaN(ext.MaxY) || (!ext.IsEmpty && (double.IsInfinity(ext.Width) || double.IsInfinity(ext.Height))))
					{
						failures.Add($"[스크립트 {si}, 선택 {withSelection}] 장면 범위가 유한하지 않음: {ext}");
					}
					// 실행 취소/다시 실행도 예외 없이
					doc.Undo();
					doc.Redo();
				}
				catch (Exception ex)
				{
					failures.Add($"[스크립트 {si}, 선택 {withSelection}, 단계 {step}] {ex.GetType().Name}: {ex.Message}");
				}
			}
		}
		Assert.True(failures.Count == 0, name + "\n" + string.Join("\n", failures));
	}
}

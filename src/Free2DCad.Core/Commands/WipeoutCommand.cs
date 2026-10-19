using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>
/// WIPEOUT: 다각형 영역을 배경색으로 가리는 객체. 점을 차례로 찍거나(Enter로 닫음), 폴리라인(P)으로, 프레임(F) 켜기/끄기.
/// </summary>
public sealed class WipeoutCommand : EditorCommand
{
	private readonly List<XY> _pts = new();
	private string? _await;
	private LwPolyline? _source;

	public override string Name => "WIPEOUT";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pts.Clear();
		this._await = null;
		this.Prompt = "첫번째 지점 또는 [프레임(F)/폴리라인(P)] <폴리라인>을 지정하세요:";
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		switch (this._await)
		{
			case "F":
				if (t.StartsWith("OF")) Free2DCad.Rendering.SceneBuilder.WipeoutFrames = false;
				else if (t.StartsWith("ON") || t.Length == 0) Free2DCad.Rendering.SceneBuilder.WipeoutFrames = true;
				this.Ctx?.Print(Free2DCad.Rendering.SceneBuilder.WipeoutFrames ? "이미지와 와이프아웃 프레임이 켜져 있습니다." : "이미지와 와이프아웃 프레임이 꺼져 있습니다.");
				this.Finish();
				return;
			case "DEL":
				this.FromPolyline(t.StartsWith("Y"));
				return;
		}
		if (this._pts.Count > 0) return;
		if (t == "F")
		{
			this._await = "F";
			this.Prompt = Free2DCad.Rendering.SceneBuilder.WipeoutFrames ? "모드 입력 [켜기(ON)/끄기(OFF)] <켜기>:" : "모드 입력 [켜기(ON)/끄기(OFF)] <끄기>:";
		}
		else if (t == "P")
		{
			this._await = "P";
			this.Prompt = "닫힌 폴리라인 선택:";
		}
	}

	public override void OnEnter()
	{
		if (this._await == "F")
		{
			this.OnText("");
			return;
		}
		if (this._await == "DEL")
		{
			this.FromPolyline(false);
			return;
		}
		if (this._pts.Count == 0 && this._await == null)
		{
			this._await = "P";
			this.Prompt = "닫힌 폴리라인 선택:";
			return;
		}
		if (this._pts.Count >= 3)
		{
			this.Ctx!.Doc.AddEntities(Create(this._pts));
		}
		this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._await == "P")
		{
			if (this.Ctx.PickAt(x, y) is LwPolyline { IsClosed: true } pl && pl.Vertices.All(v => Math.Abs(v.Bulge) < 1e-12))
			{
				this._source = pl;
				this._await = "DEL";
				this.Prompt = "폴리라인 지우기 [예(Y)/아니오(N)] <아니오>:";
			}
			else
			{
				this.Ctx.Print("선택된 객체는 닫힌 폴리라인이어야 하며, 오직 선분만으로 구성되어야 합니다.");
			}
			return;
		}
		if (this._await != null) return;
		this._pts.Add(new XY(x, y));
		this.Ctx.LastPoint = (x, y);
		this.Prompt = "다음 점 지정 또는 [명령 취소(U)] <완료>:";
	}

	private void FromPolyline(bool delete)
	{
		var pl = this._source!;
		var w = Create(pl.Vertices.Select(v => v.Location).ToList());
		Geom.CopyProps(pl, w);
		if (delete) this.Ctx!.Doc.Replace(new[] { pl }, new[] { w }, "와이프아웃");
		else this.Ctx!.Doc.AddEntities(w);
		this.Finish();
	}

	/// <summary>
	/// 와이프아웃 만들기: 삽입점 = 범위 왼쪽 아래, 이미지 1×1 픽셀, U·V = 범위 폭·높이,
	/// 경계 = 픽셀 좌표(가운데 원점, y 뒤집음) — 닫힌 다각형으로 첫 점을 끝에 한 번 더.
	/// </summary>
	public static Wipeout Create(IReadOnlyList<XY> pts)
	{
		double minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y), maxX = pts.Max(p => p.X), maxY = pts.Max(p => p.Y);
		double w = Math.Max(maxX - minX, 1e-9), h = Math.Max(maxY - minY, 1e-9);
		var wo = new Wipeout
		{
			InsertPoint = new XYZ(minX, minY, 0),
			UVector = new XYZ(w, 0, 0),
			VVector = new XYZ(0, h, 0),
			Size = new XY(1, 1),
			ClippingState = true,
			Brightness = 50,
			Contrast = 50,
			Fade = 0,
		};
		foreach (var p in pts)
		{
			wo.ClipBoundaryVertices.Add(new XY((p.X - minX) / w - 0.5, 0.5 - (p.Y - minY) / h));
		}
		wo.ClipBoundaryVertices.Add(wo.ClipBoundaryVertices[0]);
		return wo;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._pts.Count == 0 || this._await != null) yield break;
		var pl = new LwPolyline { IsClosed = true };
		foreach (var p in this._pts) pl.Vertices.Add(new LwPolyline.Vertex(p));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(cursorX, cursorY)));
		yield return pl;
	}
}

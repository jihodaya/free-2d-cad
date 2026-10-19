using System.Text;
using ACadSharp.Entities;
using CSMath;
using Free2DCad.Rendering;
using Xunit;

namespace Free2DCad.Core.Tests;

public class ShxFontTests
{
	/// <summary>작은 "shapes 1.0" SHX를 만들어 본다: 글꼴 정보(above 10) + 'L' 모양 (위로 10, 오른쪽 6, 펜 들고 오른쪽 2).</summary>
	private static byte[] MakeShapesFont()
	{
		var ms = new MemoryStream();
		ms.Write(Encoding.ASCII.GetBytes("CAD-86 shapes 1.0\r\n\x1A"));
		byte[] info = { (byte)'T', 0, 10, 2, 0, 0 };          // 이름 "T", above 10, below 2, modes, 끝
		// 펜 올림(02) → 위로 10(A4) → 펜 내림(01) → 아래로 10(AC) → 오른쪽 6(60) → 펜 올림 → 오른쪽 2(20) → 끝
		byte[] shapeL = { (byte)'L', 0, 0x02, 0xA4, 0x01, 0xAC, 0x60, 0x02, 0x20, 0x00 };
		void W16(int v) { ms.WriteByte((byte)(v & 0xFF)); ms.WriteByte((byte)(v >> 8)); }
		W16(0); W16('L'); W16(2);          // 첫 번호, 마지막 번호, 개수
		W16(0); W16(info.Length);
		W16('L'); W16(shapeL.Length);
		ms.Write(info);
		ms.Write(shapeL);
		return ms.ToArray();
	}

	[Fact]
	public void ParsesShapesFont_AndInterpretsVectors()
	{
		var f = ShxFont.Parse(MakeShapesFont());
		Assert.NotNull(f);
		Assert.Equal(ShxFont.ShxKind.Shapes, f!.Kind);
		Assert.Equal(10, f.Above);
		var g = f.GetGlyph('L');
		Assert.NotNull(g);
		// 펜 올리고 위로 10 → 펜 내리고 아래로 10, 오른쪽 6 → 펜 올리고 오른쪽 2 = 다음 글자 위치 (8, 0)
		var stroke = Assert.Single(g!.Strokes);
		Assert.Equal(new double[] { 0, 10, 0, 0, 6, 0 }, stroke);
		Assert.Equal(8, g.AdvanceX, 6);
		Assert.Equal(0, g.AdvanceY, 6);
	}
}

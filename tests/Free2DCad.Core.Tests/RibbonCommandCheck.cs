using Free2DCad.Core.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Free2DCad.Core.Tests;

public class RibbonCommandCheck
{
	private readonly ITestOutputHelper _o;

	public RibbonCommandCheck(ITestOutputHelper o) => this._o = o;

	[Fact]
	public void ListUnregistered()
	{
		string? file = Environment.GetEnvironmentVariable("F2D_RIBBON_CMDS");
		if (file == null) return;
		foreach (var c in File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0))
		{
			if (!CommandRegistry.IsRegistered(c) && CommandPresets.Create(c) == null) this._o.WriteLine("MISSING " + c);
		}
	}
}

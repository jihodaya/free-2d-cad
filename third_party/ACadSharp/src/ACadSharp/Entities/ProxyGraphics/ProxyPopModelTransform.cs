namespace ACadSharp.Entities.ProxyGraphics;

/// <summary>
/// Represents a proxy graphics entity for a pop model transform in CAD.
/// </summary>
public class ProxyPopModelTransform : IProxyGeometry
{
	/// <inheritdoc/>
	public GraphicsType GraphicsType { get { return GraphicsType.PopModelTransform; } }
}
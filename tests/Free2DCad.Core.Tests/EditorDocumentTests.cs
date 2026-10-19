using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core;
using Free2DCad.Core.Actions;
using Xunit;

namespace Free2DCad.Core.Tests;

public class EditorDocumentTests
{
	[Fact]
	public void AddEntities_Undo_Redo_WorksCorrectly()
	{
		var ed = EditorDocument.New();
		var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 10, 0));

		bool changedFired = false;
		ed.Changed += (_, _) => changedFired = true;

		// 1. Add
		ed.AddEntities(line);
		Assert.True(changedFired);
		Assert.True(ed.IsDirty);
		Assert.True(ed.CanUndo);
		Assert.False(ed.CanRedo);
		Assert.Contains(line, ed.Document.Entities);

		// 2. Undo
		changedFired = false;
		ed.Undo();
		Assert.True(changedFired);
		Assert.False(ed.CanUndo);
		Assert.True(ed.CanRedo);
		Assert.DoesNotContain(line, ed.Document.Entities);

		// 3. Redo
		changedFired = false;
		ed.Redo();
		Assert.True(changedFired);
		Assert.True(ed.CanUndo);
		Assert.False(ed.CanRedo);
		Assert.Contains(line, ed.Document.Entities);
	}

	[Fact]
	public void Move_ThenUndo_RestoresCoordinates()
	{
		var ed = EditorDocument.New();
		var line = new Line(new XYZ(5, 10, 0), new XYZ(20, 30, 0));
		ed.AddEntities(line);

		ed.Move(new[] { line }, 15, -5);

		Assert.Equal(20, line.StartPoint.X, precision: 4);
		Assert.Equal(5, line.StartPoint.Y, precision: 4);
		Assert.Equal(35, line.EndPoint.X, precision: 4);
		Assert.Equal(25, line.EndPoint.Y, precision: 4);

		// Undo
		ed.Undo();

		Assert.Equal(5, line.StartPoint.X, precision: 4);
		Assert.Equal(10, line.StartPoint.Y, precision: 4);
		Assert.Equal(20, line.EndPoint.X, precision: 4);
		Assert.Equal(30, line.EndPoint.Y, precision: 4);

		// Redo
		ed.Redo();

		Assert.Equal(20, line.StartPoint.X, precision: 4);
		Assert.Equal(5, line.StartPoint.Y, precision: 4);
	}

	[Fact]
	public void Erase_ThenUndo_RestoresEntities()
	{
		var ed = EditorDocument.New();
		var line1 = new Line(new XYZ(0, 0, 0), new XYZ(1, 1, 0));
		var line2 = new Line(new XYZ(2, 2, 0), new XYZ(3, 3, 0));
		ed.AddEntities(line1, line2);
		Assert.Equal(2, ed.Document.Entities.Count);

		// Erase line1
		ed.Erase(line1);
		Assert.Single(ed.Document.Entities);
		Assert.DoesNotContain(line1, ed.Document.Entities);
		Assert.Contains(line2, ed.Document.Entities);

		// Undo
		ed.Undo();
		Assert.Equal(2, ed.Document.Entities.Count);
		Assert.Contains(line1, ed.Document.Entities);
		Assert.Contains(line2, ed.Document.Entities);

		// Redo
		ed.Redo();
		Assert.Single(ed.Document.Entities);
		Assert.DoesNotContain(line1, ed.Document.Entities);
	}

	[Fact]
	public void Copy_IncreasesEntityCount_AndCanUndo()
	{
		var ed = EditorDocument.New();
		var line1 = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
		var line2 = new Line(new XYZ(0, 0, 0), new XYZ(0, 10, 0));
		ed.AddEntities(line1, line2);
		Assert.Equal(2, ed.Document.Entities.Count);

		// Copy with offset (5, 5)
		var copies = ed.Copy(new[] { line1, line2 }, 5, 5);

		Assert.Equal(2, copies.Count);
		Assert.Equal(4, ed.Document.Entities.Count);

		var copiedLine1 = Assert.IsType<Line>(copies[0]);
		Assert.Equal(5, copiedLine1.StartPoint.X, precision: 4);
		Assert.Equal(5, copiedLine1.StartPoint.Y, precision: 4);
		Assert.Equal(15, copiedLine1.EndPoint.X, precision: 4);
		Assert.Equal(5, copiedLine1.EndPoint.Y, precision: 4);

		// Undo removes copies
		ed.Undo();
		Assert.Equal(2, ed.Document.Entities.Count);

		// Redo adds copies back
		ed.Redo();
		Assert.Equal(4, ed.Document.Entities.Count);
	}

	[Fact]
	public void Rotate_90Degrees_CoordinatesTransformedCorrectly()
	{
		var ed = EditorDocument.New();
		// Line from (0,0) to (10, 0)
		var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
		ed.AddEntities(line);

		// Rotate 90 degrees (Math.PI / 2) around origin (0, 0)
		ed.Rotate(new[] { line }, 0, 0, Math.PI / 2.0);

		Assert.Equal(0, line.StartPoint.X, precision: 3);
		Assert.Equal(0, line.StartPoint.Y, precision: 3);
		Assert.Equal(0, line.EndPoint.X, precision: 3);
		Assert.Equal(10, line.EndPoint.Y, precision: 3);

		// Undo
		ed.Undo();
		Assert.Equal(10, line.EndPoint.X, precision: 3);
		Assert.Equal(0, line.EndPoint.Y, precision: 3);
	}

	[Fact]
	public void Scale_ScalesCoordinatesAroundBasePoint()
	{
		var ed = EditorDocument.New();
		var line = new Line(new XYZ(10, 10, 0), new XYZ(20, 10, 0));
		ed.AddEntities(line);

		// Scale 2x around (10, 10)
		ed.Scale(new[] { line }, 10, 10, 2.0);

		Assert.Equal(10, line.StartPoint.X, precision: 4);
		Assert.Equal(10, line.StartPoint.Y, precision: 4);
		Assert.Equal(30, line.EndPoint.X, precision: 4);
		Assert.Equal(10, line.EndPoint.Y, precision: 4);

		// Undo
		ed.Undo();
		Assert.Equal(20, line.EndPoint.X, precision: 4);
	}

	[Fact]
	public void Mirror_AcrossAxis_TransformsOrCopiesCorrectly()
	{
		var ed = EditorDocument.New();
		// Line from (2, 3) to (5, 3)
		var line = new Line(new XYZ(2, 3, 0), new XYZ(5, 3, 0));
		ed.AddEntities(line);

		// 1. Mirror without keepOriginal across x-axis (y = 0, points (0,0)-(1,0))
		ed.Mirror(new[] { line }, 0, 0, 1, 0, keepOriginal: false);
		Assert.Equal(2, line.StartPoint.X, precision: 4);
		Assert.Equal(-3, line.StartPoint.Y, precision: 4);
		Assert.Equal(5, line.EndPoint.X, precision: 4);
		Assert.Equal(-3, line.EndPoint.Y, precision: 4);

		// Undo
		ed.Undo();
		Assert.Equal(3, line.StartPoint.Y, precision: 4);

		// 2. Mirror with keepOriginal across y-axis (x = 0, points (0,0)-(0,1))
		var clones = ed.Mirror(new[] { line }, 0, 0, 0, 1, keepOriginal: true);
		Assert.Single(clones);
		Assert.Equal(2, ed.Document.Entities.Count);

		var mirrored = Assert.IsType<Line>(clones[0]);
		Assert.Equal(-2, mirrored.StartPoint.X, precision: 4);
		Assert.Equal(3, mirrored.StartPoint.Y, precision: 4);
		Assert.Equal(-5, mirrored.EndPoint.X, precision: 4);
		Assert.Equal(3, mirrored.EndPoint.Y, precision: 4);

		// Undo
		ed.Undo();
		Assert.Single(ed.Document.Entities);
	}

	[Fact]
	public void AddEntities_AssignsCurrentLayer_WhenNoLayerSpecified()
	{
		var ed = EditorDocument.New();
		var customLayer = new Layer("CustomLayer");
		ed.Document.Layers.Add(customLayer);
		ed.CurrentLayer = customLayer;

		var line = new Line(new XYZ(0, 0, 0), new XYZ(1, 1, 0));
		ed.AddEntities(line);

		Assert.Equal("CustomLayer", line.Layer.Name);
	}

	[Fact]
	public void ChangeLayer_And_ChangeColor_WorkAndUndo()
	{
		var ed = EditorDocument.New();
		var line = new Line(new XYZ(0, 0, 0), new XYZ(1, 1, 0));
		ed.AddEntities(line);

		var layerA = new Layer("LayerA");
		ed.Document.Layers.Add(layerA);

		// Change Layer
		ed.SetLayer(new[] { line }, layerA);
		Assert.Equal("LayerA", line.Layer.Name);
		ed.Undo();
		Assert.Equal("0", line.Layer.Name);

		// Change Color
		ed.SetColor(new[] { line }, new Color(1)); // Red
		Assert.Equal((short)1, line.Color.Index);
		ed.Undo();
		Assert.True(line.Color.IsByLayer);
	}

	[Fact]
	public void SetLayerOn_TogglesAndUndoes()
	{
		var ed = EditorDocument.New();
		var layer = new Layer("TestLayer") { IsOn = true };
		ed.Document.Layers.Add(layer);

		ed.SetLayerOn(layer);
		Assert.False(layer.IsOn);

		ed.Undo();
		Assert.True(layer.IsOn);

		ed.Redo();
		Assert.False(layer.IsOn);
	}

	[Fact]
	public void ChangeText_WorksAndUndoes()
	{
		var ed = EditorDocument.New();
		var text = new TextEntity { Value = "OldText" };
		ed.AddEntities(text);

		ed.ChangeText(text, "NewText");
		Assert.Equal("NewText", text.Value);

		ed.Undo();
		Assert.Equal("OldText", text.Value);

		ed.Redo();
		Assert.Equal("NewText", text.Value);
	}
}

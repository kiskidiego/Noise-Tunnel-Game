using Godot;

[GlobalClass]
partial class PointOfInterest : Resource
{
    [Export] public string Name { get; set; }
    [Export] public bool randomizePosition { get; set; } = false;
    [Export] public Vector3I Position { get; set; }
    [Export] public string Description { get; set; }
    [Export] public int[] leadsTo { get; set; } // Array of indices to other points of interest
}
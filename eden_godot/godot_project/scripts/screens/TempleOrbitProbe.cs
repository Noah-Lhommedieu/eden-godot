using Godot;

namespace EdenClient.Screens;

/// <summary>QA-only camera sweep for the pre-game S218 stage. Activated
/// by <c>--temple-orbit-test=/absolute/path/prefix</c>; hides screen
/// controls, waits for the map GLB, captures four azimuths at two
/// distances, then exits. This records the actual rendered scene rather
/// than inferring a good camera from asset bounds.</summary>
public partial class TempleOrbitProbe : Node
{
    private readonly TempleBackdrop _stage;
    private readonly string _prefix;
    private int _frame;
    private int _view;
    private bool _started;

    public TempleOrbitProbe(TempleBackdrop stage, string prefix)
    {
        _stage = stage;
        _prefix = prefix;
        Name = "__temple_orbit_probe__";
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (!_stage.TempleLoaded)
            return;

        if (!_started)
        {
            _started = true;
            if (_stage.Root.GetParent() is Control screen)
                foreach (var child in screen.GetChildren())
                    if (child is Control control &&
                        child != _stage.Root)
                        control.Visible = false;
            // View the original NIF coordinates from the measured
            // surface, independently from the proposed stage offset.
            // This distinguishes a bad map translation from a bad shot.
            _frame = 0;
        }

        _frame++;
        if (_frame < 18)
            return;

        var center = _stage.CharMarker.GlobalPosition;
        var angle = _view * Mathf.Pi / 4f;
        var radius = _view < 4 ? 34f : 62f;
        var height = _view < 4 ? 9f : 28f;
        var camera = center + new Vector3(
            Mathf.Sin(angle) * radius, height,
            Mathf.Cos(angle) * radius);
        _stage.Camera.Fov = _view < 4 ? 42f : 55f;
        _stage.Camera.GlobalPosition = camera;
        _stage.Camera.LookAt(center + new Vector3(0f, 3f, 0f),
            Vector3.Up);

        RenderingServer.ForceDraw();
        var image = GetViewport().GetTexture()?.GetImage();
        if (image != null)
        {
            var file = $"{_prefix}_view{_view}.png";
            var error = image.SavePng(file);
            GD.Print($"[temple-probe] {file} save={error} " +
                $"camera={_stage.Camera.GlobalPosition}");
        }

        _view++;
        _frame = 0;
        if (_view >= 8)
        {
            GD.Print("[temple-probe] 8 views captured");
            GetTree().Quit();
        }
    }
}

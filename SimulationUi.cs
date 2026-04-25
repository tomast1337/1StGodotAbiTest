using Godot;
using System;

public partial class SimulationUi : Control
{
    [Export]
    public NodePath SimulationHandlerPath { get; set; } = "../../SimulationHandler";

    [Export]
    public NodePath RendererPath { get; set; } = "../..";

    [Export]
    public NodePath ParticleCountLabelPath { get; set; } = "VBox/ParticleCountLabel";

    [Export]
    public NodePath EmitterButtonPath { get; set; } = "VBox/ToolRow/EmitterButton";

    [Export]
    public NodePath AttractorButtonPath { get; set; } = "VBox/ToolRow/AttractorButton";

    [Export]
    public NodePath DrawWallButtonPath { get; set; } = "VBox/ToolRow/DrawWallButton";

    [Export]
    public NodePath ResetButtonPath { get; set; } = "VBox/ResetButton";

    [Export]
    public NodePath UiVBoxPath { get; set; } = "VBox";

    private SimulationHandler _simulation;
    private ParticleRenderer2D _renderer;
    private Label _particleCountLabel;
    private Button _emitterButton;
    private Button _attractorButton;
    private Button _drawWallButton;
    private Button _resetButton;
    private VBoxContainer _uiVBox;
    private OptionButton _resolutionSelector;
    private HSlider _zoomSlider;
    private Label _zoomLabel;

    public override void _Ready()
    {
        _simulation = GetNodeOrNull<SimulationHandler>(SimulationHandlerPath);
        _renderer = GetNodeOrNull<ParticleRenderer2D>(RendererPath);
        _particleCountLabel = GetNodeOrNull<Label>(ParticleCountLabelPath);
        _emitterButton = GetNodeOrNull<Button>(EmitterButtonPath);
        _attractorButton = GetNodeOrNull<Button>(AttractorButtonPath);
        _drawWallButton = GetNodeOrNull<Button>(DrawWallButtonPath);
        _resetButton = GetNodeOrNull<Button>(ResetButtonPath);
        _uiVBox = GetNodeOrNull<VBoxContainer>(UiVBoxPath);

        if (_simulation is null || _renderer is null || _particleCountLabel is null)
        {
            GD.PushError("Simulation UI is missing required node references.");
            return;
        }

        SetupToolButton(_emitterButton, ParticleRenderer2D.SimulationTool.Emitter);
        SetupToolButton(_attractorButton, ParticleRenderer2D.SimulationTool.Attractor);
        SetupToolButton(_drawWallButton, ParticleRenderer2D.SimulationTool.DrawWall);

        if (_resetButton is not null)
        {
            _resetButton.Pressed += () => _renderer.ResetSimulation();
        }

        CreateAdvancedControls();
        SelectTool(ParticleRenderer2D.SimulationTool.Emitter);
    }

    public override void _Process(double delta)
    {
        if (_simulation is null || _particleCountLabel is null)
        {
            return;
        }

        _particleCountLabel.Text = $"Particles: {_simulation.GetParticleCount()}";
    }

    private void SetupToolButton(Button button, ParticleRenderer2D.SimulationTool tool)
    {
        if (button is null)
        {
            return;
        }

        button.ToggleMode = true;
        button.Pressed += () => SelectTool(tool);
    }

    private void SelectTool(ParticleRenderer2D.SimulationTool tool)
    {
        if (_renderer is null)
        {
            return;
        }

        _renderer.SetActiveTool(tool);
        SetToolButtonState(_emitterButton, tool == ParticleRenderer2D.SimulationTool.Emitter);
        SetToolButtonState(_attractorButton, tool == ParticleRenderer2D.SimulationTool.Attractor);
        SetToolButtonState(_drawWallButton, tool == ParticleRenderer2D.SimulationTool.DrawWall);
    }

    private static void SetToolButtonState(Button button, bool pressed)
    {
        if (button is not null)
        {
            button.ButtonPressed = pressed;
        }
    }

    private void CreateAdvancedControls()
    {
        if (_uiVBox is null || _simulation is null || _renderer is null)
        {
            return;
        }

        _resolutionSelector = new OptionButton();
        _resolutionSelector.AddItem("64x64");
        _resolutionSelector.AddItem("128x128");
        _resolutionSelector.AddItem("256x256");
        _resolutionSelector.ItemSelected += OnResolutionSelected;
        _uiVBox.AddChild(_resolutionSelector);
        _resolutionSelector.Selected = ResolutionIndexForCurrentGrid();

        _zoomLabel = new Label();
        _uiVBox.AddChild(_zoomLabel);

        _zoomSlider = new HSlider
        {
            MinValue = _renderer.MinZoom,
            MaxValue = _renderer.MaxZoom,
            Step = 0.05,
            Value = _renderer.Zoom
        };
        _zoomSlider.ValueChanged += OnZoomChanged;
        _uiVBox.AddChild(_zoomSlider);

        UpdateZoomLabel((float)_zoomSlider.Value);
    }

    private int ResolutionIndexForCurrentGrid()
    {
        return _simulation.GridWidth switch
        {
            128 => 1,
            256 => 2,
            _ => 0
        };
    }

    private void OnResolutionSelected(long index)
    {
        if (_simulation is null)
        {
            return;
        }

        int size = index switch
        {
            1 => 128,
            2 => 256,
            _ => 64
        };

        _simulation.SetGridResolution(size, size);
    }

    private void OnZoomChanged(double value)
    {
        if (_renderer is null)
        {
            return;
        }

        _renderer.SetZoom((float)value);
        UpdateZoomLabel((float)value);
    }

    private void UpdateZoomLabel(float value)
    {
        if (_zoomLabel is not null)
        {
            _zoomLabel.Text = $"Zoom: {value:F2}x";
        }
    }
}

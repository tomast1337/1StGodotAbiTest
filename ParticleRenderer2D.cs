using Godot;
using System;

public partial class ParticleRenderer2D : Node2D
{
    public enum SimulationTool
    {
        Emitter,
        Attractor,
        DrawWall,
    }

    [Export]
    public NodePath SimulationHandlerPath { get; set; } = "SimulationHandler";

    [Export]
    public float Zoom { get; set; } = 1.0f;

    [Export]
    public float ParticleRadius { get; set; } = 4.0f;

    [Export]
    public Color ParticleColor { get; set; } = Colors.White;

    [Export]
    public double SpawnIntervalSeconds { get; set; } = 0.001;

    [Export]
    public float SpawnSpeed { get; set; } = 8.0f;

    [Export]
    public float AttractorStrength { get; set; } = 80.0f;

    [Export]
    public float AttractorRadius { get; set; } = 12.0f;

    [Export]
    public int WallBrushRadius { get; set; } = 1;

    [Export]
    public Color WallColor { get; set; } = new(0.35f, 0.45f, 1.0f, 0.45f);

    [Export]
    public float ZoomStep { get; set; } = 0.1f;

    [Export]
    public float MinZoom { get; set; } = 0.25f;

    [Export]
    public float MaxZoom { get; set; } = 4.0f;

    public SimulationTool ActiveTool { get; private set; } = SimulationTool.Emitter;

    private SimulationHandler _simulation;
    private readonly RandomNumberGenerator _random = new();
    private double _spawnTimer;
    private bool _wasSpawning;
    private MultiMeshInstance2D _particleBatch;
    private MultiMesh _particleMultiMesh;
    private QuadMesh _particleMesh;

    public override void _Ready()
    {
        _random.Randomize();
        CreateParticleBatch();

        _simulation = GetNodeOrNull<SimulationHandler>(SimulationHandlerPath);
        if (_simulation is null)
        {
            GD.PushError($"Particle renderer could not find SimulationHandler at '{SimulationHandlerPath}'.");
        }
    }

    public override void _Process(double delta)
    {
        UseActiveTool(delta);
        UpdateParticleBatch();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_simulation is null || !_simulation.IsRunning)
        {
            return;
        }

        DrawWalls();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_simulation is null || !_simulation.IsRunning)
        {
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.C })
        {
            _simulation.ClearParticles();
        }

        if (@event is InputEventMouseButton { Pressed: true } mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                SetZoom(Zoom + ZoomStep);
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                SetZoom(Zoom - ZoomStep);
            }
        }
    }

    public void SetActiveTool(SimulationTool tool)
    {
        ActiveTool = tool;
        ResetEmitterTimer();
    }

    public void ResetSimulation()
    {
        _simulation?.ResetSimulation();
        ResetEmitterTimer();
    }

    private void UseActiveTool(double delta)
    {
        if (_simulation is null || !_simulation.IsRunning)
        {
            return;
        }

        if (!Input.IsMouseButtonPressed(MouseButton.Left) || IsPointerOverUi())
        {
            ResetEmitterTimer();
            return;
        }

        Vector2 position = ScreenToSim(GetLocalMousePosition());

        switch (ActiveTool)
        {
            case SimulationTool.Emitter:
                SpawnWhileMouseHeld(delta, position);
                break;
            case SimulationTool.Attractor:
                _simulation.ApplyAttractor(position, AttractorStrength, AttractorRadius, (float)delta);
                break;
            case SimulationTool.DrawWall:
                _simulation.PaintWall(position, WallBrushRadius);
                break;
        }
    }

    private void CreateParticleBatch()
    {
        _particleMesh = new QuadMesh
        {
            Size = Vector2.One * ParticleRadius * 2.0f,
        };

        _particleMultiMesh = new MultiMesh
        {
            Mesh = _particleMesh,
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            InstanceCount = 0,
            VisibleInstanceCount = 0,
        };

        _particleBatch = new MultiMeshInstance2D
        {
            Name = "ParticleBatch",
            Multimesh = _particleMultiMesh,
            Texture = CreateParticleTexture(),
        };

        AddChild(_particleBatch);
    }

    private void UpdateParticleBatch()
    {
        if (_simulation is null || !_simulation.IsRunning)
        {
            _particleMultiMesh.VisibleInstanceCount = 0;
            return;
        }

        IntPtr particlesPtr = _simulation.GetParticleBuffer(out int count);
        if (particlesPtr == IntPtr.Zero || count == 0)
        {
            _particleMultiMesh.VisibleInstanceCount = 0;
            return;
        }

        EnsureParticleBatchCapacity(count);

        for (int i = 0; i < count; i++)
        {
            SimulationHandler.NativeParticle particle =
                SimulationHandler.ReadParticle(particlesPtr, i);

            _particleMultiMesh.SetInstanceTransform2D(
                i,
                new Transform2D(0.0f, SimToScreen(particle.Pos.ToGodot()))
            );
            _particleMultiMesh.SetInstanceColor(i, ParticleColor);
        }

        _particleMultiMesh.VisibleInstanceCount = count;
    }

    private void EnsureParticleBatchCapacity(int count)
    {
        if (_particleMultiMesh.InstanceCount >= count)
        {
            return;
        }

        _particleMultiMesh.InstanceCount = NextPowerOfTwo(count);
    }

    private Texture2D CreateParticleTexture()
    {
        const int size = 32;
        const float radius = (size - 1) * 0.5f;
        Vector2 center = Vector2.One * radius;
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = new Vector2(x, y).DistanceTo(center);
                float alpha = Mathf.Clamp(radius + 0.5f - distance, 0.0f, 1.0f);
                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, alpha));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    private void DrawWalls()
    {
        Vector2 cellSize = GetDynamicScale();

        for (int y = 0; y < _simulation.GridHeight; y++)
        {
            for (int x = 0; x < _simulation.GridWidth; x++)
            {
                if (!_simulation.IsWallCell(x, y))
                {
                    continue;
                }

                Vector2 position = SimToScreen(new Vector2(x - 0.5f, y - 0.5f));
                DrawRect(new Rect2(position, cellSize), WallColor);
            }
        }
    }

    private void SpawnWhileMouseHeld(double delta, Vector2 position)
    {
        if (!_wasSpawning)
        {
            SpawnParticle(position);
            _wasSpawning = true;
            _spawnTimer = 0.0;
            return;
        }

        _spawnTimer += delta;
        while (_spawnTimer >= SpawnIntervalSeconds)
        {
            SpawnParticle(position);
            _spawnTimer -= SpawnIntervalSeconds;
        }
    }

    private void SpawnParticle(Vector2 position)
    {
        _simulation.AddParticle(position, RandomVelocity());
    }

    private Vector2 RandomVelocity()
    {
        float angle = _random.RandfRange(0.0f, Mathf.Tau);
        return Vector2.FromAngle(angle) * SpawnSpeed;
    }

    private void ResetEmitterTimer()
    {
        _wasSpawning = false;
        _spawnTimer = 0.0;
    }

    private bool IsPointerOverUi()
    {
        return GetViewport().GuiGetHoveredControl() is not null;
    }

    private Vector2 SimToScreen(Vector2 position)
    {
        return position * GetDynamicScale();
    }

    private Vector2 ScreenToSim(Vector2 position)
    {
        Vector2 scale = GetDynamicScale();
        return new Vector2(
            scale.X <= Mathf.Epsilon ? 0.0f : position.X / scale.X,
            scale.Y <= Mathf.Epsilon ? 0.0f : position.Y / scale.Y
        );
    }

    private Vector2 GetDynamicScale()
    {
        if (_simulation is null || _simulation.GridWidth <= 0 || _simulation.GridHeight <= 0)
        {
            return Vector2.One;
        }

        Vector2 viewportSize = GetViewportRect().Size;
        Vector2 baseScale = new Vector2(
            viewportSize.X / _simulation.GridWidth,
            viewportSize.Y / _simulation.GridHeight
        );
        return baseScale * Zoom;
    }

    public void SetZoom(float value)
    {
        Zoom = Mathf.Clamp(value, MinZoom, MaxZoom);
    }
}

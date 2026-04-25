using Godot;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

public partial class SimulationHandler : Node
{
    private const string NativeLibraryName = "particle_sim";
    private const string NativeLibraryPath = "res://particle_sim/target/release/libparticle_sim.so";
    public const int MaxParticles = 1048576;
    public const int GridWidth = 64;
    public const int GridHeight = 64;

    private IntPtr _simulation = IntPtr.Zero;
    private readonly float[] _sdf = new float[GridWidth * GridHeight];

    public bool IsRunning => _simulation != IntPtr.Zero;

    static SimulationHandler()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ResolveNativeLibrary);
    }

    public override void _Ready()
    {
        _simulation = SimCreate((UIntPtr)MaxParticles, (UIntPtr)GridWidth, (UIntPtr)GridHeight);
        if (_simulation == IntPtr.Zero)
        {
            GD.PushError("Failed to create native particle simulation.");
            return;
        }

        ResetSdf();
        SeedParticles();
    }

    public override void _Process(double delta)
    {
        if (_simulation == IntPtr.Zero)
        {
            return;
        }

        SimStep(_simulation, (float)delta);
    }

    public override void _ExitTree()
    {
        if (_simulation == IntPtr.Zero)
        {
            return;
        }

        SimDestroy(_simulation);
        _simulation = IntPtr.Zero;
    }

    private static IntPtr ResolveNativeLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath
    )
    {
        if (libraryName != NativeLibraryName)
        {
            return IntPtr.Zero;
        }

        string path = ProjectSettings.GlobalizePath(NativeLibraryPath);
        return NativeLibrary.Load(path, assembly, searchPath);
    }

    private void ResetSdf()
    {
        Array.Fill(_sdf, 1.0f);
        UploadSdf();
    }

    private bool UploadSdf()
    {
        if (_simulation == IntPtr.Zero)
        {
            return false;
        }

        bool uploaded = SimUpdateSdf(_simulation, _sdf, (UIntPtr)_sdf.Length) != 0;
        if (!uploaded)
        {
            GD.PushError("Failed to upload SDF grid to native particle simulation.");
        }

        return uploaded;
    }

    private void SeedParticles()
    {
        NativeParticle particle = new()
        {
            Pos = new NativeVec2 { X = 4.0f, Y = 4.0f },
            Vel = new NativeVec2 { X = 8.0f, Y = 3.0f },
        };

        if (SimAddParticle(_simulation, particle) == 0)
        {
            GD.PushError("Failed to add initial particle to native particle simulation.");
        }
    }

    public int GetParticleCount()
    {
        if (_simulation == IntPtr.Zero)
        {
            return 0;
        }

        return checked((int)SimParticleCount(_simulation));
    }

    public bool TryGetParticle(int index, out NativeParticle particle)
    {
        particle = default;

        if (_simulation == IntPtr.Zero || index < 0)
        {
            return false;
        }

        IntPtr particlesPtr = SimGetParticles(_simulation, out UIntPtr count);
        if (particlesPtr == IntPtr.Zero || (ulong)index >= count.ToUInt64())
        {
            return false;
        }

        int particleSize = Marshal.SizeOf<NativeParticle>();
        IntPtr particlePtr = IntPtr.Add(particlesPtr, checked(index * particleSize));
        particle = Marshal.PtrToStructure<NativeParticle>(particlePtr);
        return true;
    }

    public IntPtr GetParticleBuffer(out int count)
    {
        count = 0;

        if (_simulation == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr particlesPtr = SimGetParticles(_simulation, out UIntPtr nativeCount);
        if (particlesPtr == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        count = checked((int)nativeCount);
        return particlesPtr;
    }

    public static NativeParticle ReadParticle(IntPtr particlesPtr, int index)
    {
        int particleSize = Marshal.SizeOf<NativeParticle>();
        IntPtr particlePtr = IntPtr.Add(particlesPtr, checked(index * particleSize));
        return Marshal.PtrToStructure<NativeParticle>(particlePtr);
    }

    public bool AddParticle(Vector2 position, Vector2 velocity)
    {
        if (_simulation == IntPtr.Zero)
        {
            return false;
        }

        NativeParticle particle = new()
        {
            Pos = NativeVec2.FromGodot(position),
            Vel = NativeVec2.FromGodot(velocity),
        };

        return SimAddParticle(_simulation, particle) != 0;
    }

    public void ClearParticles()
    {
        if (_simulation != IntPtr.Zero)
        {
            SimClearParticles(_simulation);
        }
    }

    public void ResetSimulation()
    {
        ClearParticles();
        ResetSdf();
    }

    public bool ApplyAttractor(Vector2 position, float strength, float radius, float delta)
    {
        if (_simulation == IntPtr.Zero)
        {
            return false;
        }

        return SimApplyAttractor(
            _simulation,
            NativeVec2.FromGodot(position),
            strength,
            radius,
            delta
        ) != 0;
    }

    public bool PaintWall(Vector2 position, int brushRadius = 1)
    {
        if (_simulation == IntPtr.Zero)
        {
            return false;
        }

        int centerX = Math.Clamp(Mathf.RoundToInt(position.X), 0, GridWidth - 1);
        int centerY = Math.Clamp(Mathf.RoundToInt(position.Y), 0, GridHeight - 1);
        int radius = Math.Max(0, brushRadius);
        int radiusSquared = radius * radius;

        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            if (y < 0 || y >= GridHeight)
            {
                continue;
            }

            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x < 0 || x >= GridWidth)
                {
                    continue;
                }

                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy > radiusSquared)
                {
                    continue;
                }

                _sdf[y * GridWidth + x] = -1.0f;
            }
        }

        return UploadSdf();
    }

    public bool IsWallCell(int x, int y)
    {
        if (x < 0 || x >= GridWidth || y < 0 || y >= GridHeight)
        {
            return false;
        }

        return _sdf[y * GridWidth + x] < 0.0f;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeVec2
    {
        public float X;
        public float Y;

        public readonly Vector2 ToGodot()
        {
            return new Vector2(X, Y);
        }

        public static NativeVec2 FromGodot(Vector2 value)
        {
            return new NativeVec2 { X = value.X, Y = value.Y };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeParticle
    {
        public NativeVec2 Pos;
        public NativeVec2 Vel;
    }

    [DllImport(NativeLibraryName, EntryPoint = "sim_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SimCreate(UIntPtr maxParticles, UIntPtr width, UIntPtr height);

    [DllImport(NativeLibraryName, EntryPoint = "sim_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SimDestroy(IntPtr simulation);

    [DllImport(NativeLibraryName, EntryPoint = "sim_step", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SimStep(IntPtr simulation, float dt);

    [DllImport(NativeLibraryName, EntryPoint = "sim_clear_particles", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SimClearParticles(IntPtr simulation);

    [DllImport(NativeLibraryName, EntryPoint = "sim_add_particle", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte SimAddParticle(IntPtr simulation, NativeParticle particle);

    [DllImport(NativeLibraryName, EntryPoint = "sim_get_particles", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SimGetParticles(IntPtr simulation, out UIntPtr count);

    [DllImport(NativeLibraryName, EntryPoint = "sim_update_sdf", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte SimUpdateSdf(
        IntPtr simulation,
        [In] float[] sdfData,
        UIntPtr dataLen
    );

    [DllImport(NativeLibraryName, EntryPoint = "sim_apply_attractor", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte SimApplyAttractor(
        IntPtr simulation,
        NativeVec2 target,
        float strength,
        float radius,
        float dt
    );

    [DllImport(NativeLibraryName, EntryPoint = "sim_particle_count", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr SimParticleCount(IntPtr simulation);

    [DllImport(NativeLibraryName, EntryPoint = "sim_particle_capacity", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr SimParticleCapacity(IntPtr simulation);
}
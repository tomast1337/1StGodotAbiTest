# 1st Godot ABI Test

Godot 4 C# project that renders and controls a particle simulation implemented in Rust.

This project is intentionally manual right now: Godot does not build the Rust shared library for you. You must compile the Rust library yourself before running the Godot scene.

## Requirements

- Godot 4.6 with .NET/C# support
- .NET SDK for the Godot C# project
- Rust toolchain with Cargo
- Linux currently, because the C# loader points at `libparticle_sim.so`

## Project Layout

- `particle_sim/`: Rust simulation crate
- `particle_sim/target/release/libparticle_sim.so`: compiled Rust shared library loaded by Godot
- `SimulationHandler.cs`: owns the native Rust simulation pointer and FFI calls
- `ParticleRenderer2D.cs`: batch-renders particles and handles active tools
- `SimulationUi.cs`: in-game UI for count, tool buttons, and reset
- `main.tscn`: main Godot scene

## Manual Build Steps

From the project root:

```bash
cd particle_sim
cargo build --release --target-dir target
```

This must produce:

```text
particle_sim/target/release/libparticle_sim.so
```

The C# code loads that exact path through:

```text
res://particle_sim/target/release/libparticle_sim.so
```

If the file is missing or stale, Godot may fail to start the simulation or keep running old Rust code.

## Run In Godot

1. Build the Rust shared library manually:

   ```bash
   cd particle_sim
   cargo build --release --target-dir target
   ```

2. Open the project folder in Godot:

   ```text
   /home/tomast1337/1StGodotAbiTest
   ```

3. Run the main scene.

The configured main scene is `main.tscn`.

## Optional C# Build Check

From the project root:

```bash
dotnet build "1st godot ABI Test.csproj"
```

This only checks the C# side. It does not rebuild Rust.

## Controls

Use the in-game UI panel:

- `Emitter`: hold left mouse to spawn particles.
- `Attractor`: hold left mouse to pull existing particles toward the cursor.
- `Draw Wall`: hold left mouse to paint collision wall cells.
- `Reset`: clear particles and reset walls.

Keyboard:

- `C`: clear particles.

## Important Notes

- Rebuild Rust after changing anything in `particle_sim/src/lib.rs`.
- Restart the Godot run after rebuilding Rust so the native library is reloaded.
- The Rust library is loaded through a raw C ABI. Keep exported Rust structs marked with `#[repr(C)]` and keep matching C# structs in sync.
- Current native library path is Linux-specific. Windows/macOS will need the C# path updated to load `.dll` or `.dylib`.

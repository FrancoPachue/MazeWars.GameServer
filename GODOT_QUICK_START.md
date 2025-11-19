# Godot C# Quick Start Guide - MazeWars Client

**Document Version:** 1.0
**Created:** 2025-11-19
**Target:** Godot 4.3 (.NET)
**Server:** MazeWars.GameServer (C#)

---

## Table of Contents

1. [Installation](#installation)
2. [Project Creation](#project-creation)
3. [Sharing Server DTOs](#sharing-server-dtos)
4. [First Scene Setup](#first-scene-setup)
5. [Testing MessagePack](#testing-messagepack)
6. [Next Steps](#next-steps)

---

## Installation

### 1. Install .NET 8.0 SDK

```bash
# Check if already installed
dotnet --version
# Should show: 8.0.x

# If not installed:
# Windows: https://dotnet.microsoft.com/download/dotnet/8.0
# Download: .NET 8.0 SDK (x64)
```

### 2. Download Godot 4.3 (.NET Version)

**⚠️ CRITICAL: Download the ".NET" version, NOT the standard version**

**Windows:**
```
URL: https://godotengine.org/download/windows/

Look for: "Godot Engine - .NET (C#)"
File: Godot_v4.3-stable_mono_win64.zip
Size: ~150 MB
```

**Linux:**
```
URL: https://godotengine.org/download/linux/

File: Godot_v4.3-stable_mono_linux_x86_64.zip
```

**macOS:**
```
URL: https://godotengine.org/download/macos/

File: Godot_v4.3-stable_mono_macos.universal.zip
```

### 3. Extract and Run

```bash
# Extract the downloaded ZIP
# Run: Godot_v4.3-stable_mono_win64.exe (or equivalent)
```

### 4. Verify .NET Detection

1. Open Godot
2. Go to **Editor → Editor Settings**
3. Navigate to **Dotnet → Build**
4. Check **Dotnet CLI Path**: should show path to `dotnet.exe`
5. If not detected, click **Browse** and locate your `dotnet.exe`:
   - Usually: `C:\Program Files\dotnet\dotnet.exe`

---

## Project Creation

### 1. Create New Project

1. In Godot, click **New Project**
2. **Project Name:** `MazeWars.Client`
3. **Project Path:** Choose location (e.g., `C:\Dev\MazeWars.Client`)
4. **Renderer:** Forward+ (best for 2D/3D hybrid)
5. Click **Create & Edit**

### 2. Verify C# Support

1. Create a test script:
   - Right-click in **FileSystem** panel
   - Select **Create New → Script**
   - **Language:** C# (should be available)
   - **Class Name:** `TestScript`
   - Click **Create**

2. Check that `.csproj` file was created:
   - Look for `MazeWars.Client.csproj` in project root

3. Build the project:
   - Top menu: **Project → Tools → C# → Build Project**
   - Should succeed without errors

### 3. Configure Project Settings

**Display Settings (For Top-Down 2D):**
1. **Project → Project Settings → Display → Window**
   - **Viewport Width:** 1920
   - **Viewport Height:** 1080
   - **Mode:** Windowed
   - **Resizable:** On

**Input Map (WASD Movement):**
1. **Project → Project Settings → Input Map**
2. Add actions:
   - `move_up`: W, Arrow Up
   - `move_down`: S, Arrow Down
   - `move_left`: A, Arrow Left
   - `move_right`: D, Arrow Right
   - `sprint`: Shift (Left)
   - `ability_1`: 1
   - `ability_2`: 2
   - `ability_3`: 3
   - `inventory`: I
   - `chat`: Enter

---

## Sharing Server DTOs

You have multiple options to share code between server and client:

### Option A: Git Submodule (Recommended for Separate Repos)

**Advantages:**
- Separate repositories (clean separation)
- Client auto-updates when server DTOs change
- Version control for both projects

**Setup:**
```bash
cd MazeWars.Client/
mkdir Shared
cd Shared
git submodule add https://github.com/FrancoPachue/MazeWars.GameServer Server
```

**Reference in .csproj:**
```xml
<ItemGroup>
  <Compile Include="Shared\Server\Network\Models\*.cs" Link="Shared\NetworkModels\%(Filename)%(Extension)" />
  <Compile Include="Shared\Server\Network\Packets\*.cs" Link="Shared\NetworkPackets\%(Filename)%(Extension)" />
</ItemGroup>
```

### Option B: Project Reference (Recommended for Mono-repo)

**Advantages:**
- Simplest setup
- Direct project reference
- IntelliSense works perfectly

**Create a Shared Library:**
```bash
cd MazeWars.GameServer/
dotnet new classlib -n MazeWars.Shared
```

**Move DTOs to Shared Project:**
```
MazeWars.Shared/
├── Network/
│   ├── Models/
│   │   ├── NetworkMessage.cs
│   │   ├── PlayerInputMessage.cs
│   │   ├── GameStateUpdate.cs
│   │   └── ... (all DTO classes)
│   └── Packets/
└── MazeWars.Shared.csproj
```

**Reference from Client:**
```xml
<!-- MazeWars.Client.csproj -->
<ItemGroup>
  <ProjectReference Include="..\MazeWars.GameServer\MazeWars.Shared\MazeWars.Shared.csproj" />
</ItemGroup>
```

**Reference from Server:**
```xml
<!-- MazeWars.GameServer.csproj -->
<ItemGroup>
  <ProjectReference Include="MazeWars.Shared\MazeWars.Shared.csproj" />
</ItemGroup>
```

### Option C: File Linking (Quick & Dirty)

**Advantages:**
- No repo changes
- Works immediately

**Setup:**
```xml
<!-- MazeWars.Client.csproj -->
<ItemGroup>
  <Compile Include="..\MazeWars.GameServer\Network\Models\*.cs" Link="Shared\%(Filename)%(Extension)" />
</ItemGroup>
```

**⚠️ Downside:** Changes in client won't sync back to server automatically.

### 🏆 My Recommendation: Option B (Shared Library)

This is the cleanest approach:
1. Create `MazeWars.Shared` class library
2. Move all DTOs there
3. Both client and server reference it
4. Automatically shared, no duplication

---

## Installing NuGet Packages

### 1. Add Required Packages

Edit `MazeWars.Client.csproj`:

```xml
<Project Sdk="Godot.NET.Sdk/4.3.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>MazeWars.Client</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- Networking -->
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.0" />
    <PackageReference Include="MessagePack" Version="2.5.140" />
    <PackageReference Include="MessagePack.Annotations" Version="2.5.140" />

    <!-- Utilities -->
    <PackageReference Include="System.Numerics.Vectors" Version="4.5.0" />
  </ItemGroup>

  <!-- If using Option B (Shared Library) -->
  <ItemGroup>
    <ProjectReference Include="..\MazeWars.GameServer\MazeWars.Shared\MazeWars.Shared.csproj" />
  </ItemGroup>
</Project>
```

### 2. Restore Packages

```bash
# In Godot editor
# Top menu: Project → Tools → C# → Build Project

# Or in terminal
cd MazeWars.Client/
dotnet restore
dotnet build
```

---

## First Scene Setup

### 1. Create Main Scene

1. **Scene → New Scene**
2. Select **2D Scene** (creates Node2D root)
3. Rename root node to `Main`
4. **Scene → Save Scene As:** `res://Scenes/Main.tscn`

### 2. Add Player Node

1. Right-click **Main** → **Add Child Node**
2. Search for **CharacterBody2D**
3. Rename to `Player`
4. Add children to Player:
   - **Sprite2D** (for player graphic)
   - **CollisionShape2D** (for physics)
   - **Camera2D** (to follow player)

### 3. Create Player Script

1. Select **Player** node
2. Click **Attach Script** button (scroll icon)
3. **Template:** Empty
4. **Path:** `res://Scripts/Player.cs`
5. Click **Create**

Replace contents with:

```csharp
using Godot;

namespace MazeWars.Client;

public partial class Player : CharacterBody2D
{
    [Export] public float Speed { get; set; } = 300f;

    private Sprite2D _sprite;
    private Camera2D _camera;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _camera = GetNode<Camera2D>("Camera2D");

        // Enable camera
        _camera.Enabled = true;

        // Set placeholder sprite color
        _sprite.Texture = CreatePlaceholderTexture();
    }

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Vector2.Zero;

        if (Input.IsActionPressed("move_up"))
            velocity.Y -= 1;
        if (Input.IsActionPressed("move_down"))
            velocity.Y += 1;
        if (Input.IsActionPressed("move_left"))
            velocity.X -= 1;
        if (Input.IsActionPressed("move_right"))
            velocity.X += 1;

        if (velocity.Length() > 0)
        {
            velocity = velocity.Normalized() * Speed;
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private Texture2D CreatePlaceholderTexture()
    {
        var image = Image.Create(32, 32, false, Image.Format.Rgba8);
        image.Fill(Colors.Blue);
        return ImageTexture.CreateFromImage(image);
    }
}
```

### 4. Configure Player Components

**Sprite2D:**
- **Centered:** On
- **Texture:** Will be set by script

**CollisionShape2D:**
- Click **Shape** → **New CircleShape2D**
- **Radius:** 16

**Camera2D:**
- **Enabled:** Will be set by script
- **Position Smoothing → Enabled:** On
- **Position Smoothing → Speed:** 5.0

### 5. Run the Scene

1. Press **F6** (Run Current Scene)
2. You should see:
   - Blue square (player)
   - Camera following player
   - WASD movement working

---

## Testing MessagePack

Let's verify MessagePack works with your server DTOs.

### 1. Create Test Script

Create `res://Scripts/NetworkTest.cs`:

```csharp
using Godot;
using MessagePack;
using System;

namespace MazeWars.Client;

public partial class NetworkTest : Node
{
    public override void _Ready()
    {
        TestMessagePackSerialization();
    }

    private void TestMessagePackSerialization()
    {
        try
        {
            // Test 1: Simple NetworkMessage
            var message = new MazeWars.GameServer.Network.Models.NetworkMessage
            {
                Type = "TestMessage",
                PlayerId = "player_123",
                Data = "Hello from Godot!",
                Timestamp = DateTime.UtcNow
            };

            // Serialize
            var bytes = MessagePackSerializer.Serialize(message);
            GD.Print($"✅ Serialized: {bytes.Length} bytes");

            // Deserialize
            var deserialized = MessagePackSerializer.Deserialize<MazeWars.GameServer.Network.Models.NetworkMessage>(bytes);
            GD.Print($"✅ Deserialized: Type={deserialized.Type}, PlayerId={deserialized.PlayerId}");

            // Test 2: PlayerInputMessage
            var inputMessage = new MazeWars.GameServer.Network.Models.PlayerInputMessage
            {
                PlayerId = "player_123",
                Velocity = new System.Numerics.Vector2(1.0f, 0.5f),
                IsSprinting = true,
                SequenceNumber = 42,
                Timestamp = DateTime.UtcNow
            };

            var inputBytes = MessagePackSerializer.Serialize(inputMessage);
            GD.Print($"✅ Input serialized: {inputBytes.Length} bytes");

            var deserializedInput = MessagePackSerializer.Deserialize<MazeWars.GameServer.Network.Models.PlayerInputMessage>(inputBytes);
            GD.Print($"✅ Input deserialized: Velocity=({deserializedInput.Velocity.X}, {deserializedInput.Velocity.Y}), Sprint={deserializedInput.IsSprinting}");

            GD.Print("\n🎉 MessagePack test successful! Client can communicate with server.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"❌ MessagePack test failed: {ex.Message}");
            GD.PrintErr($"Stack trace: {ex.StackTrace}");
        }
    }
}
```

### 2. Add to Main Scene

1. Open `Main.tscn`
2. Right-click **Main** → **Add Child Node**
3. Select **Node** (base type)
4. Rename to `NetworkTest`
5. Click **Attach Script**
6. Select existing script: `res://Scripts/NetworkTest.cs`

### 3. Run Test

1. Press **F5** (Run Project)
2. Check **Output** panel (bottom)
3. You should see:

```
✅ Serialized: 89 bytes
✅ Deserialized: Type=TestMessage, PlayerId=player_123
✅ Input serialized: 47 bytes
✅ Input deserialized: Velocity=(1, 0.5), Sprint=True

🎉 MessagePack test successful! Client can communicate with server.
```

**If you see errors:**
- Check that MessagePack package is installed (`dotnet list package`)
- Verify `MazeWars.Shared` is referenced correctly
- Make sure all DTO classes have `[MessagePackObject]` attributes

---

## Project Structure After Setup

```
MazeWars.Client/
├── .godot/                    (Godot temp files, .gitignore this)
├── .vs/                       (Visual Studio temp, .gitignore this)
├── bin/                       (.gitignore this)
├── obj/                       (.gitignore this)
├── Scenes/
│   └── Main.tscn
├── Scripts/
│   ├── Player.cs
│   └── NetworkTest.cs
├── Assets/                    (create later)
│   ├── Sprites/
│   ├── Sounds/
│   └── Fonts/
├── Shared/                    (if using Option A/B)
│   └── Server/                (git submodule)
├── .gitignore
├── project.godot
├── MazeWars.Client.csproj
└── MazeWars.Client.sln
```

### Recommended .gitignore

Create `.gitignore` in project root:

```gitignore
# Godot-specific ignores
.godot/
.import/
export.cfg
export_presets.cfg

# C# / Mono ignores
.mono/
.vs/
bin/
obj/
*.csproj.user
*.suo
*.user
*.userosscache
*.sln.docstates

# Build results
[Dd]ebug/
[Rr]elease/
x64/
[Bb]in/
[Oo]bj/

# NuGet
*.nupkg
**/packages/*

# User-specific files
*.userprefs
.DS_Store
```

---

## Next Steps

### Immediate (Day 1):
1. ✅ Verify player movement works
2. ✅ Verify MessagePack test passes
3. 📝 Read `CLIENT_DEVELOPMENT_ROADMAP.md` (Phase 1)

### Week 1:
1. Create room grid (4×4)
2. Add room transitions
3. Create placeholder sprites for all entities
4. Implement camera smoothing

### Week 2:
1. Implement SignalR connection
2. Implement UDP connection
3. Send player input to server
4. Receive game state updates

### Resources:
- **Godot Docs (C#):** https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/index.html
- **MessagePack C#:** https://github.com/neuecc/MessagePack-CSharp
- **SignalR Client:** https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client

---

## Troubleshooting

### "Build failed: Could not find .NET SDK"
**Solution:**
1. Verify .NET is installed: `dotnet --version`
2. Restart Godot
3. Editor Settings → Dotnet → Build → Set CLI path manually

### "MessagePack not found"
**Solution:**
```bash
dotnet add package MessagePack --version 2.5.140
dotnet restore
# Then rebuild in Godot
```

### "Cannot reference server DTOs"
**Solution:**
- Check your chosen sharing method (A, B, or C)
- Verify path in `.csproj` is correct
- Try absolute path if relative doesn't work

### "Game runs but input doesn't work"
**Solution:**
1. Project Settings → Input Map
2. Verify all actions are defined
3. Check Input.IsActionPressed("move_up") matches action name exactly

---

## Support

**Godot Community:**
- Discord: https://discord.gg/godotengine
- Reddit: r/godot
- Forum: https://forum.godotengine.org/

**MazeWars Specific:**
- Refer to `CLIENT_DEVELOPMENT_ROADMAP.md` for detailed implementation
- Check `GAME_MECHANICS_COMPLETE_ANALYSIS.md` for game systems reference

---

## Summary Checklist

Before moving to Phase 1 development, ensure:

- [ ] .NET 8.0 SDK installed and detected
- [ ] Godot 4.3 (.NET version) running
- [ ] New project created and builds successfully
- [ ] MessagePack NuGet package installed
- [ ] Server DTOs accessible (via chosen sharing method)
- [ ] MessagePack test passes
- [ ] Player movement works (WASD + camera)
- [ ] Input Map configured
- [ ] `.gitignore` configured
- [ ] Project structure matches recommendation

**If all boxes checked:** You're ready for Phase 1! 🚀

**Time to complete this guide:** 1-2 hours

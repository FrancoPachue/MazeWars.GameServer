# MazeWars Client Repository Setup Guide

**Target:** Create `MazeWars.Client` repository for Godot development
**Time Required:** 15-20 minutes
**Prerequisites:** Godot 4.3 .NET installed, Git installed

---

## Quick Setup (For Creating New Repo)

### Step 1: Create Godot Project Locally

1. **Open Godot 4.3 (.NET version)**
2. **Click "New Project"**
3. **Configure:**
   - Project Name: `MazeWars.Client`
   - Project Path: `C:\Dev\MazeWars.Client` (or your preferred location)
   - Renderer: **Forward+**
4. **Click "Create & Edit"**

### Step 2: Close Godot and Setup Git

```bash
# Navigate to your project folder
cd C:\Dev\MazeWars.Client

# Initialize Git repository
git init

# Copy the .gitignore file (see .gitignore.client.example in server repo)
# Or create .gitignore manually (content below)

# Create initial folder structure
mkdir Scenes Scenes/Game Scenes/UI
mkdir Scripts Scripts/Networking Scripts/Game Scripts/UI
mkdir Assets Assets/Sprites Assets/Sounds Assets/Fonts
mkdir Shared
```

### Step 3: Create .gitignore

Create `.gitignore` in project root with this content:

```gitignore
# Godot 4+ specific ignores
.godot/
.import/

# Godot-specific ignores
*.translation
export.cfg
export_presets.cfg

# Imported translations
*.import

# Mono-specific ignores
.mono/
data_*/
mono_crash.*.json

# C# / .NET specific
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
x86/
[Bb]in/
[Oo]bj/

# Visual Studio / Rider
.vs/
.vscode/
.idea/
*.sln.iml

# NuGet Packages
*.nupkg
*.snupkg
**/[Pp]ackages/*

# System Files
.DS_Store
Thumbs.db

# Godot export files
*.exe
*.pck
*.apk
*.zip
*.dmg

# Keep submodule link but ignore content
Shared/Server/*
!Shared/Server/.git
```

### Step 4: Configure C# Project (.csproj)

Edit `MazeWars.Client.csproj` to add NuGet packages:

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

  <!-- Link to server DTOs via submodule -->
  <ItemGroup>
    <Compile Include="Shared/Server/Network/Models/*.cs" Link="Shared/NetworkModels/%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

### Step 5: Create README.md

Create `README.md` in project root:

```markdown
# MazeWars Client

2D Top-Down Multiplayer Game Client built with Godot 4.3 (C#/.NET)

## Prerequisites

- Godot 4.3 (.NET version)
- .NET 8.0 SDK
- Visual Studio 2022 or Rider (optional, for C# development)

## Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/YourUsername/MazeWars.Client
   cd MazeWars.Client
   ```

2. Initialize submodules (to get server DTOs):
   ```bash
   git submodule update --init --recursive
   ```

3. Restore NuGet packages:
   ```bash
   dotnet restore
   ```

4. Open project in Godot:
   - Launch Godot 4.3 (.NET)
   - Click "Import"
   - Navigate to project folder and select `project.godot`
   - Click "Import & Edit"

5. Build the C# project:
   - In Godot: **Project → Tools → C# → Build Project**

## Project Structure

```
MazeWars.Client/
├── Scenes/          # Godot scene files (.tscn)
│   ├── Game/        # Gameplay scenes
│   └── UI/          # UI scenes
├── Scripts/         # C# scripts
│   ├── Networking/  # Network communication
│   ├── Game/        # Game logic
│   └── UI/          # UI controllers
├── Assets/          # Game assets
│   ├── Sprites/     # 2D sprites
│   ├── Sounds/      # Audio files
│   └── Fonts/       # Font files
└── Shared/          # Shared code with server
    └── Server/      # Git submodule → MazeWars.GameServer
```

## Server Connection

The client connects to the MazeWars game server:
- **WebSocket (SignalR):** Reliable messages, lobby, chat
- **UDP:** Player input and game state updates
- **Serialization:** MessagePack binary format

## Development

See documentation:
- [CLIENT_DEVELOPMENT_ROADMAP.md](../MazeWars.GameServer/CLIENT_DEVELOPMENT_ROADMAP.md)
- [GODOT_QUICK_START.md](../MazeWars.GameServer/GODOT_QUICK_START.md)
- [GAME_MECHANICS_COMPLETE_ANALYSIS.md](../MazeWars.GameServer/GAME_MECHANICS_COMPLETE_ANALYSIS.md)

## Running the Game

1. Start the server:
   ```bash
   cd ../MazeWars.GameServer
   dotnet run
   ```

2. Run the client:
   - In Godot: Press **F5** (Run Project)
   - Or press **F6** (Run Current Scene)

## License

[Your License Here]
```

### Step 6: Link Server Repository as Submodule

```bash
# Add server repo as submodule in Shared/Server
git submodule add https://github.com/FrancoPachue/MazeWars.GameServer Shared/Server

# This creates:
# - .gitmodules file (tracked by Git)
# - Shared/Server/ folder (link to server repo)
```

### Step 7: Initial Commit

```bash
# Stage all files
git add .

# Create initial commit
git commit -m "Initial commit: Godot 4.3 C# project setup

- Project structure created
- NuGet packages configured (MessagePack, SignalR)
- Server DTOs linked via submodule
- .gitignore configured for Godot and C#"
```

### Step 8: Create GitHub Repository

**Option A: Via GitHub Website**
1. Go to https://github.com/new
2. Repository name: `MazeWars.Client`
3. Description: `2D Top-Down Multiplayer Game Client (Godot 4.3 C#)`
4. Public or Private: Your choice
5. **DO NOT** initialize with README (you already have one)
6. Click "Create repository"

**Option B: Via GitHub CLI (if installed)**
```bash
gh repo create MazeWars.Client --public --source=. --remote=origin
```

### Step 9: Push to GitHub

```bash
# Add remote (replace YourUsername with your GitHub username)
git remote add origin https://github.com/YourUsername/MazeWars.Client.git

# Push to main branch
git branch -M main
git push -u origin main
```

### Step 10: Verify Setup

```bash
# Check that submodule is initialized
git submodule status
# Should show: Shared/Server pointing to commit hash

# Open in Godot
# - Should open without errors
# - File structure should be visible

# Build C# project
# In Godot: Project → Tools → C# → Build Project
# Should complete successfully
```

---

## Working with Submodules

### Cloning the Client Repo (for other developers or Claude)

```bash
# Clone with submodules
git clone --recurse-submodules https://github.com/YourUsername/MazeWars.Client

# Or if already cloned without submodules
git submodule update --init --recursive
```

### Updating Server DTOs

```bash
# When server DTOs change, update the submodule
cd Shared/Server
git pull origin main

# Commit the submodule update
cd ../..
git add Shared/Server
git commit -m "Update server DTOs to latest version"
git push
```

---

## Alternative: Shared Library Approach

If you prefer not to use submodules, you can create a shared library:

### Step 1: Create Shared Library in Server Repo

```bash
cd MazeWars.GameServer
dotnet new classlib -n MazeWars.Shared
```

### Step 2: Move DTOs to Shared Library

```
MazeWars.GameServer/
├── MazeWars.Shared/
│   ├── Network/
│   │   └── Models/
│   │       ├── NetworkMessage.cs
│   │       ├── PlayerInputMessage.cs
│   │       └── ... (all DTOs)
│   └── MazeWars.Shared.csproj
```

### Step 3: Reference from Client

```xml
<!-- MazeWars.Client.csproj -->
<ItemGroup>
  <ProjectReference Include="..\MazeWars.GameServer\MazeWars.Shared\MazeWars.Shared.csproj" />
</ItemGroup>
```

This approach requires both repos to be in the same parent folder:
```
Dev/
├── MazeWars.GameServer/
└── MazeWars.Client/
```

---

## For Claude to Work on the Repo

Once you've created the repository:

1. **Share the GitHub URL** with Claude: `https://github.com/YourUsername/MazeWars.Client`

2. **Claude will need access** to:
   - Clone the repo
   - Create branches
   - Make commits
   - Push changes

3. **Recommended workflow:**
   ```
   You: "Work on MazeWars.Client repo: <GitHub URL>"
   Claude: Will clone, create branch, make changes, commit, push
   You: Review changes in GitHub
   ```

4. **For development sessions:**
   - Claude will create feature branches (e.g., `claude/networking-setup`)
   - Make commits with clear messages
   - You review and merge via pull requests

---

## Checklist Before Sharing Repo

- [ ] Godot project created and opens successfully
- [ ] .gitignore configured
- [ ] Folder structure created
- [ ] .csproj includes NuGet packages
- [ ] README.md created
- [ ] Submodule added and initialized
- [ ] Initial commit made
- [ ] GitHub repository created
- [ ] Code pushed to GitHub
- [ ] Repository is public (or Claude has access)
- [ ] C# build succeeds in Godot

---

## Quick Commands Summary

```bash
# Full setup from scratch
cd C:\Dev\MazeWars.Client  # After creating project in Godot
git init
# Copy .gitignore
mkdir Scenes Scripts Assets Shared
git submodule add https://github.com/FrancoPachue/MazeWars.GameServer Shared/Server
git add .
git commit -m "Initial commit: Godot 4.3 C# project setup"
git remote add origin https://github.com/YourUsername/MazeWars.Client.git
git branch -M main
git push -u origin main
```

---

## Troubleshooting

### "Submodule path contains uncommitted changes"
```bash
cd Shared/Server
git status
git checkout main
git pull
cd ../..
```

### "Build failed: Missing references"
```bash
dotnet restore
# Then rebuild in Godot
```

### "Godot can't find .NET SDK"
```bash
# Verify .NET is installed
dotnet --version

# In Godot: Editor → Editor Settings → Dotnet → Build
# Set CLI path manually to dotnet.exe
```

### "Cannot push to GitHub"
```bash
# Check remote URL
git remote -v

# Re-add if needed
git remote set-url origin https://github.com/YourUsername/MazeWars.Client.git
```

---

## Next Steps After Repo Creation

1. Follow **GODOT_QUICK_START.md** for initial development
2. Implement Phase 1 from **CLIENT_DEVELOPMENT_ROADMAP.md**
3. Set up CI/CD (optional, GitHub Actions for builds)
4. Share repo URL with team or Claude for collaboration

---

## Estimated Time

- **Project creation:** 5 minutes
- **Git setup:** 5 minutes
- **GitHub creation and push:** 5 minutes
- **Verification:** 5 minutes
- **Total:** ~20 minutes

Good luck! 🚀

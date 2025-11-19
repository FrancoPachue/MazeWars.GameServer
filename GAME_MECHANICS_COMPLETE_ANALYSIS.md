# MazeWars.GameServer - Comprehensive Game Mechanics Analysis

## Executive Summary
MazeWars.GameServer is a real-time multiplayer game server featuring team-based PvP combat, procedural dungeon exploration, AI-controlled enemies, and a dynamic loot system. The game runs on a 60 FPS game loop with delta compression for network optimization.

---

# 1. COMBAT SYSTEM

## Location
- **Main System**: `/Engine/Combat/CombatSystem.cs`
- **Interface**: `/Engine/Combat/Interface/ICombatSystem.cs`
- **Models**: `/Engine/Combat/Models/`

## Fully Implemented Features

### Attack System
- **Melee Combat**: Direct damage calculation based on attacker class and target armor
- **Attack Range Detection**: 60-degree cone targeting (dot product calculation)
- **Attack Cooldown**: Configurable per class (default: 1000ms)
- **Attack Speed Variance**: 20% damage variance applied per attack
- **Critical Hit Chance**: Scout class has 15% critical hit chance (1.5x multiplier)
- **Class-Based Damage**:
  - Tank: 35 base damage
  - Scout: 25 base damage
  - Support: 20 base damage
- **Strength Bonus**: +2 damage per strength stat point
- **Weapon Damage Integration**: Reads weapon damage properties from equipped items

### Ability System
Three core ability types organized by class:

#### Scout Abilities
- **Dash**: Move forward 10 units (20 mana cost, 5s cooldown)
- **Stealth**: 5-second invisibility effect (30 mana cost, 12s cooldown)

#### Tank Abilities
- **Charge**: Move 8 units, deal 30 damage to nearby enemies (25 mana, 8s cooldown)
- **Shield**: 50 shield points + 50% damage reduction for 10s (40 mana, 15s cooldown)

#### Support Abilities
- **Heal**: Restore 40 HP to nearby teammates within 8 units (35 mana, 6s cooldown)
- **Buff**: Apply speed boost (1.5x) for 8 seconds to 6-unit radius (30 mana, 10s cooldown)

### Status Effect System
- **Shield**: Reduces incoming damage before health
- **Slow**: Reduces movement speed to 50% for 3 seconds
- **Poison**: 5 damage per second for 5 seconds
- **Speed Boost**: 1.5x movement speed (temporary)
- **Regen**: Restores health over duration
- **Stealth**: Hides player presence (5s default)

### Damage Reduction
- **Shield Absorption**: Shield absorbs damage first before health
- **Armor**: Player damage reduction modifier (0-1.0 scale)
- **Class Modifiers**: Armor provides flat damage reduction

### Death Mechanics
- **Instant Death**: Health <= 0
- **Player Death Events**: Triggers loot drop, respawn logic
- **Killer Attribution**: Combat system tracks who dealt final damage
- **Death State**: `IsAlive = false`, `Health = 0`

## Key Classes
- `CombatSystem`: Core combat logic, 539 lines
- `CombatResult`: Outcome of combat (Success, ErrorMessage, Events, TargetsHit)
- `AbilityResult`: Ability execution result
- `CombatEvent`: Combat event record (EventType, SourceId, TargetId, Value, Position)

## Configuration Options
```csharp
GameBalance Settings:
- AttackRange: 1.5 units (default)
- AttackCooldownMs: 1000ms (default)
- BaseHealth: 100 HP (configurable: 50-500)
- MaxInventorySize: 20 items (configurable: 5-50)
```

---

# 2. MOVEMENT SYSTEM

## Location
- **Main System**: `/Engine/Movement/MovementSystem.cs`
- **Interface**: `/Engine/Movement/Interface/IMovementSystem.cs`
- **Settings**: `/Engine/Movement/Settings/MovementSettings.cs`
- **Models**: `/Engine/Movement/Models/`
- **Tracker**: `/Engine/Movement/Services/PlayerMovementTracker.cs`

## Fully Implemented Features

### Basic Movement
- **Speed Calculation**: Base speed modified by movement speed modifier and class
- **Class Speed Modifiers**:
  - Scout: 1.1x (fastest)
  - Support: 1.0x (normal)
  - Tank: 0.9x (slowest)
- **Sprinting**: 1.5x speed multiplier, costs 1 mana per second
- **Velocity Tracking**: Current velocity vector maintained per player
- **Direction Tracking**: Aim direction as float (radians)

### Collision System
- **Spatial Grid Optimization**: 32-unit cells for efficient collision detection
- **Player-to-Player Collision**: 0.8 unit radius
- **Player-to-Mob Collision**: 1.0 unit radius
- **Collision Resolution**: Push-based physics with velocity damping
- **Wall Collision**: Clamping to world bounds (±240 units)
- **Mob Collision Detection**: Checks all adjacent grid cells

### Room System
- **Room Transitions**: Automatic detection based on position
- **Room Bounds**: Min/max position validation per room
- **Connected Rooms**: Navigation between adjacent rooms
- **Encounter Detection**: Triggers when enemy teams in same room
- **Room Tracking**: `CurrentRoomId` per player

### Teleportation
- **Max Distance**: 15 units default
- **Target Validation**: Must be within world bounds
- **Collision Checks**: Target must be unblocked
- **Room Transition**: Automatically handles room changes on teleport

### Anti-Cheat Detection
- **Movement Validation**: Checks for impossible speeds (1.2x tolerance)
- **Teleport Detection**: Flags sudden position jumps
- **Suspicion Tracking**: 0-1.0 suspicion level per violation
- **Player Monitoring**: Flags players with >5 suspicious movements
- **Speed History**: Tracks recent positions for average speed calculation

## Key Classes
- `MovementSystem`: 1066 lines, 750+ configurations
- `MovementResult`: Success, NewPosition, NewVelocity, CollisionDetected
- `RoomTransitionResult`: RoomChanged, OldRoomId, NewRoomId, EncounterDetected
- `TeleportResult`: Success, FinalPosition, RoomChanged
- `SpatialGrid`: Cell-based optimization (PlayerCells, MobCells)
- `PlayerMovementTracker`: Anti-cheat, position history, speed calculation

## Configuration Options
```csharp
MovementSettings:
- BaseMovementSpeed: 5.0 (configurable: 1.0-20.0)
- SprintMultiplier: 1.5x (configurable: 1.1-3.0)
- PlayerCollisionRadius: 0.8 units
- MobCollisionRadius: 1.0 units
- MaxInputMagnitude: 1.1 (prevents overspeed inputs)
- TeleportMaxDistance: 15 units
- EnableCollisionPrediction: true
- ManaCostPerSprintSecond: 1 mana/sec
```

---

# 3. LOOT SYSTEM

## Location
- **Main System**: `/Engine/Loot/LootSystem.cs`
- **Interface**: `/Engine/Loot/Interface/ILootSystem.cs`
- **Models**: `/Engine/Loot/Models/`

## Fully Implemented Features

### Loot Spawning
- **Timed Respawn**: Configurable interval (120s default)
- **Dynamic Spawning**: Based on dead mobs and room completion
- **Max Per Room**: 5 items max (configurable)
- **Drop Chance**: Percentage-based per loot table entry
- **Quantity Range**: Min/max quantities per drop
- **Rarity Tiers**: 1-5 rarity levels with modifiers

### Loot Tables
- **Weapon Drops**: Boss/elite mob table
- **Consumable Drops**: Common table
- **Key Drops**: Boss exclusive table
- **Dynamic Rarity**: Difficulty scaling based on world age and completion
- **Weighted Selection**: Spawn weight per item type

### Player Loot Interaction
- **Grab Range**: 3 units default
- **Inventory Check**: Max 20 items (configurable: 5-50)
- **Room Validation**: Can only grab loot in current room
- **Loot Ownership**: No reservation system (free-for-all)
- **Drop Pickup**: Instant pickup without cooldown

### Item Consumption
**Consumables**:
- Health Potions: 50+ HP restoration
- Mana Potions: 50+ mana restoration
- Speed Elixir: 1.5x-2.0x speed for 5-10s
- Strength Potion: Damage buff

**Equipment**:
- Weapons: Add damage properties
- Armor: Add defense/health bonuses
- Keys: Unlock specific doors/chests

### Loot Expiration
- **Timeout**: 10 minutes default (configurable)
- **Automatic Cleanup**: Dead mobs' loot expires
- **Density Management**: Removes oldest items if room exceeds limit

### Drop Mechanics
**On Player Death**:
- Drop top 3 inventory items
- Drop location: Near death position (±2 units offset)
- Room preservation: Drops in death room

**On Mob Death**:
- Max 2 drops per mob (configurable)
- Boss drops guaranteed rare items
- Luck stat multiplier: 0.1x per luck point

### Dynamic Rarity System
Rarity affected by:
- **World Completion**: +2 rarity per 100% completion
- **Room Difficulty**: +1 for center rooms
- **Trigger Condition**: 
  - Boss death: +2
  - Room clear: +1
  - Mob death: +0.5
- **Game Age**: +1 rarity after 10+ minutes

## Key Classes
- `LootSystem`: 1115 lines
- `LootGrabResult`: Success, ErrorMessage, OutOfRange, InventoryFull
- `ItemUseResult`: Success, HealthRestored, ManaRestored, AppliedEffects
- `ConsumableResult`: HealthRestored, ManaRestored, StatusEffects
- `EquipResult`: Success, StatChanges, EquippedItem
- `KeyUseResult`: Success, UnlockedDoor
- `LootStats`: Tracking spawned/taken/expired counts

## Configuration Options
```csharp
LootSettings:
- MaxLootPerRoom: 5 (configurable)
- LootExpirationTimeMinutes: 10
- LootRespawnIntervalSeconds: 120
- LootGrabRange: 3.0 units
- GlobalDropRateMultiplier: 1.0
- EnableDynamicRarity: true
- LuckStatMultiplier: 0.1
- MaxDropsPerMob: 2
- EnableLootMagnetism: false
```

---

# 4. AI/MOB SYSTEM

## Location
- **Main System**: `/Engine/MobIASystem/MobIASystem.cs`
- **Interface**: `/Engine/MobIASystem/Interface/IMobIASystem.cs`
- **Models**: `/Engine/MobIASystem/Models/`

## Fully Implemented Features

### Mob Spawning
- **Initial Spawn**: Random mobs per room (1-4 per room)
- **Dynamic Spawn**: Up to 20 additional mobs during game
- **Spawn Avoidance**: No spawning in extraction rooms
- **Position Randomization**: Within room bounds with margin
- **Type Selection**: Weighted random from available templates

### Mob Templates
Four base mob types with configurable stats:

#### Guard
- Health: 75 HP
- Damage: 25
- Speed: 1.5 units/s
- Attack Range: 3.0 units
- Detection Range: 12.0 units
- Abilities: Roar (call for help)

#### Patrol
- Health: 50 HP
- Damage: 15
- Speed: 2.0 units/s
- Attack Range: 2.5 units
- Detection Range: 8.0 units
- Abilities: None

#### Elite
- Health: 150 HP
- Damage: 40
- Speed: 1.8 units/s
- Attack Range: 4.0 units
- Detection Range: 15.0 units
- Armor: 10
- Abilities: Charge, Roar
- Critical Chance: 15%

#### Boss
- Health: 500 HP
- Damage: 75
- Speed: 1.2 units/s
- Attack Range: 5.0 units
- Detection Range: 20.0 units
- Armor: 20, Magic Resistance: 15%
- Abilities: Charge, Roar, Summon, Heal
- Critical Chance: 25%

### AI State Machine
**Six Core States**:
1. **Idle**: Waiting, passive
2. **Patrol**: Moving in patrol radius (15-25 units)
3. **Alert**: Player detected, investigating
4. **Pursuing**: Active chase
5. **Attacking**: Within range, dealing damage
6. **Fleeing**: Health < threshold (10-20% depending on type)
7. **Enraged**: Boss only, <30% health, 1.5x aggression
8. **Stunned**: Temporary disabled state
9. **Dead**: Permanent state

### AI Behavior System
**Decision Context**:
- Nearby players (detection range)
- Nearby mobs (group awareness)
- Health percentage
- Group size modifier
- Aggression level

**Behavior Triggers**:
- **Player Detection**: Switches Patrol → Alert → Pursuing
- **Health Threshold**: Switches Attacking → Fleeing
- **Group Bonus**: +10% aggression per group member
- **World Completion**: +50% aggression as world progresses

### Combat Behavior
- **Attack Cooldown**: 2.0-4.0s per template
- **Damage Calculation**: Base damage with critical chance
- **Target Priority**: Prefers player class (configurable)
- **Range Checking**: Only attacks within AttackRange
- **Attack Performance**: Tracks attacks/s per mob

### Group Behavior
- **Groups**: Up to 4 mobs per room
- **Leadership**: First mob in group is leader
- **Help Calls**: Roar ability summons nearby allies within 15-25 units
- **Coordination**:
  - **Pursuit**: Spread around target in circle
  - **Attack**: Focus fire or spread based on numbers
  - **Retreat**: Group moves together away from players

### Boss AI
- **Health Phases**:
  - <50%: Summon minions (3 patrol mobs)
  - <30%: Enrage (1.5x aggression, 1.43x attack speed)
- **Ability Usage**: Heal at <30%, Summon at <50%, Charge regularly
- **Minion Spawning**: Creates enhanced minions (1.5x aggression)

### Pathfinding
- **Simple Implementation**: Step-based movement toward target
- **Path Caching**: Reused for 2 seconds
- **Navigation Mesh**: Created per world with node spacing
- **Obstacle Avoidance**: Basic bounds checking

### Processing Optimization
- **Priority Levels**: Critical, High, Medium, Low
- **Distance-Based**: 
  - <10 units: Critical (50/frame)
  - <25 units: High (30/frame)
  - <50 units: Medium (20/frame)
  - >50 units: Low (10/frame)
- **Spatial Partitioning**: Room-based clustering
- **Lazy Updates**: Only process if `RequiresUpdate = true`

### Difficulty Scaling
Mob stats multiply by:
- **Time Multiplier**: 1.0 + (game_age_hours × 0.1)
- **Level Multiplier**: 1.0 + (avg_player_level - 1) × 0.15
- **Difficulty Setting**: Base multiplier (default 1.0)

## Key Classes
- `MobAISystem`: 2324 lines, comprehensive AI engine
- `EnhancedMob`: Extended Mob with AI state, template, stats
- `MobTemplate`: Configurable mob type definition
- `AIBehaviorResult`: State changes, actions performed
- `AIDecision`: Action choice with priority
- `MobCombatResult`: AttackPerformed, DamageDealt, TargetKilled
- `MobAbilityResult`: AbilityName, Success, AffectedPlayers, Damage

## Configuration Options
```csharp
AISettings:
- GlobalAggressionMultiplier: 1.0 (configurable)
- UpdateFrequency: 30.0 Hz
- MaxMobsPerRoom: 8 (configurable)
- DifficultyScaling: 1.0 (configurable)
- EnableGroupBehavior: true
- EnableDynamicSpawning: true
- DynamicSpawnInterval: 60.0 seconds
- MaxDynamicMobs: 20
- OptimizationDistance: 50.0 units
- BossSpawnChance: 0.1 (10%)
```

---

# 5. EXTRACTION MECHANICS

## Location
- **Model**: `/Models/ExtractionPoint.cs`
- **Network Models**: `/Network/Models/ExtractionMessage.cs`, `ExtractionProgress.cs`, `ExtractionPointUpdate.cs`
- **Processing**: `Engine/Managers/InputProcessor.cs` (handles extraction messages)
- **World Generation**: `Engine/Managers/WorldManager.cs`

## Fully Implemented Features

### Extraction Points
- **Count**: 4 per world (one per corner)
- **Positions**: 
  - Top-left: (0, 0) in room_0_0
  - Top-right: ((X-1) × spacing, 0)
  - Bottom-left: (0, (Y-1) × spacing)
  - Bottom-right: ((X-1) × spacing, (Y-1) × spacing)
- **Room Association**: Each point in a corner room
- **Active Status**: Default inactive until triggered

### Extraction Process
- **Initiation**: Player sends extraction request with extraction ID
- **Duration**: Configurable (default: 30 seconds)
- **Progress Tracking**: 
  - Current progress % (0-100)
  - Seconds remaining countdown
  - Player name display
- **Interrupt Conditions**: (Likely damage/movement, needs verification in GameEngine)
- **Completion**: Removes player from world, counts as escape

### Network Synchronization
**Messages Sent**:
- `ExtractionMessage`: Action (start/cancel), ExtractionId
- `ExtractionProgress`: PlayerId, PlayerName, Progress %, SecondsRemaining
- `ExtractionPointUpdate`: Status changes to all clients

### Team Victory Condition
- **Extraction Count**: Track players who successfully extract per team
- **Winning Condition**: First team to extract threshold wins
- **World End**: Game ends when winner determined

## Configuration Options
```csharp
GameBalance:
- ExtractionTimeSeconds: 30 (configurable: 5-300)

WorldGeneration:
- ExtractionPointCount: 4 (fixed)
```

---

# 6. INVENTORY AND ITEMS

## Location
- **Item System**: `/Engine/Items/ItemSystem.cs`
- **World Interaction**: `/Engine/Items/WorldInteractionSystem.cs`
- **Models**: `/Models/LootItem.cs`, `/Engine/Loot/Models/`
- **Network**: `/Network/Models/InventoryItem.cs`, `InventoryUpdate.cs`, `UseItemMessage.cs`

## Fully Implemented Features

### Inventory Management
- **Capacity**: 20 items max (configurable: 5-50)
- **Item Storage**: `List<LootItem>` on player
- **Inventory Update**: Sent to clients on changes
- **Equipment Slots**: Implicit (no separate equipment model)
- **Weight System**: Not implemented (unlimited capacity)

### Item Types
1. **Consumables**
   - Health potions (50-100 HP)
   - Mana potions (50-100 mana)
   - Speed elixirs (temporary 1.5-2.0x speed)
   - Strength potions (temporary damage buff)
   - Invisibility potions (stealth effect)

2. **Equipment**
   - Weapons: Add base damage, speed, crit % properties
   - Armor: Add defense, health bonuses

3. **Keys**
   - Silver, Gold, Master keys
   - Unlock specific doors/chests

4. **Tools**
   - Lockpick: Pick locks
   - Rope: Climbing/utility

### Item Properties
```csharp
LootItem Structure:
- LootId: Unique identifier
- ItemName: Display name
- ItemType: consumable|weapon|armor|key|tool
- Rarity: 1-5 (quality tier)
- Properties: Dictionary<string, object>
  - "heal": int (HP restoration)
  - "mana": int (mana restoration)
  - "damage": int (weapon stat)
  - "defense": int (armor stat)
  - "health": int (armor bonus)
  - "speed_boost": float (1.0-2.0)
  - "duration": int (seconds)
  - "equipped": bool
  - "critical": float (0.0-0.5)
```

### Item Usage
**Consumables**:
- Cannot use if already at max (health potion at max HP)
- Instant effect application
- Removed from inventory after use
- Status effects applied via CombatSystem

**Equipment**:
- Toggle equip/unequip
- No stat calculation (properties stored as-is)
- Can only equip one weapon/armor at a time

**Keys**:
- Check if unlocking valid target
- Removed after use (one-time)
- Broadcast unlock event

**Tools**:
- Lockpick: Opens locks
- Rope: Custom world interactions

### Loot Item Drop
- **On Death**: Top 3 items drop at player location
- **Ownership**: Free-for-all (no reservation)
- **Proximity**: ±2 units random offset from death position
- **Persistence**: Expires after 10 minutes

## Key Classes
- `ItemSystem`: 179 lines
- `WorldInteractionSystem`: Key/tool interaction handling
- `LootItem`: Item data model
- `UseItemResult`: Success, EffectsApplied, ErrorMessage
- `InventoryItem`: Network serialization of items

## Configuration Options
```csharp
GameBalance:
- MaxInventorySize: 20 items (configurable: 5-50)

LootSettings:
- LootGrabRange: 3.0 units
```

---

# 7. TEAM AND PVP MECHANICS

## Location
- **Player Model**: `/Models/RealTimePlayer.cs` (TeamId property)
- **Lobby**: `/Engine/Managers/LobbyManager.cs`
- **World Management**: `/Engine/Managers/WorldManager.cs`
- **Network**: `/Network/Services/NetworkService.cs`

## Fully Implemented Features

### Team System
- **Team ID**: String identifier (format: "team_*")
- **Team Assignment**: On player connect
- **Team Size**: Max 6 players (configurable: 1-16)
- **Dynamic Teams**: Multiple teams per world possible

### World Assignment
- **Team-Based Worlds**: Each team matches to world
- **Load Balancing**: Matches players to worlds with available space
- **Validation**: TeamId must start with "team"

### PvP Combat
- **Team Awareness**: Combat checks `TeamId != attacker.TeamId`
- **Friendly Fire**: OFF (cannot damage same team)
- **Threat Detection**: Detect when enemy team in same room
- **Encounter Events**: Trigger when teams meet

### Room Encounters
- **Detection**: Automatic when players from different teams enter room
- **Notification**: Broadcast encounter event to world
- **Duration**: Until teams separate or one eliminated

### Team Victory Conditions
- **Extraction Victory**: First team to extract wins
- **Team Elimination**: Last team standing wins
- **Victory Events**: Tracked in world end state

### Team Communication
- **Chat Modes**: Global and Team-specific
- **Team Chat**: Broadcast only to TeamId matches
- **Voice Channels**: Not implemented

### Player State Sharing
- **Visible to Team**: Enemy teams see objective info
- **Extraction Progress**: Shows to all players
- **Kill/Death Events**: Broadcast to world

## Key Classes
- `RealTimePlayer`: TeamId property
- `LobbyManager`: Team formation and balance
- `WorldManager`: Team → World mapping

## Configuration Options
```csharp
GameBalance:
- MaxTeamSize: 6 (configurable: 1-16)

LobbySettings:
- Available team configurations
```

---

# 8. PLAYER STATS AND PROGRESSION

## Location
- **Player Model**: `/Models/RealTimePlayer.cs`
- **Status Effects**: `/Models/StatusEffect.cs`
- **Network Model**: `/Network/Models/PlayerStats.cs`

## Fully Implemented Features

### Character Stats
**Core Stats**:
- `Health`: Current HP (0-MaxHealth)
- `MaxHealth`: 100 default
- `Mana`: Current MP (0-MaxMana)
- `MaxMana`: 100 default
- `Shield`: Temporary shield points (0-MaxShield)
- `MaxShield`: 0 default (set by abilities)

**Combat Stats**:
- `Level`: Current level (default: 1)
- `ExperiencePoints`: XP towards next level
- `Stats`: Dictionary<string, int> for custom stats
  - "strength": Damage bonus (+2 per point)
  - "luck": Loot rarity bonus (0.1x per point)
  - Other custom stats via properties

**Class Stats**:
- `PlayerClass`: "tank", "scout", "support"
- Class affects: Damage output, speed, abilities

**Combat Modifiers**:
- `DamageReduction`: 0-1.0 scale (0% to 100% reduction)
- `MovementSpeedModifier`: 1.0x default, modified by effects
- `AttackCooldowns`: Dictionary<string, DateTime> per ability

### Status Effects
Six core types:
1. **Shield**: Temporary damage reduction (shields health)
2. **Slow**: Movement penalty to 50% for duration
3. **Speed**: Movement bonus to 1.5x for duration
4. **Poison**: Damage over time (5 HP/sec typical)
5. **Regen**: Healing over time
6. **Stealth**: Invisibility for duration

**Effect Properties**:
- `EffectType`: String identifier
- `Value`: Magnitude/damage
- `ExpiresAt`: Automatic removal
- `SourcePlayerId`: Applied by which player

### Experience & Leveling
**Formula**:
- Base XP per level: 1000 (configurable)
- Multiplier: 1.5x per level
- XP per level = BaseXP × (1.5 ^ (Level - 1))

**Current Implementation**: Tracking only (no level-up mechanics visible)

### Position Tracking
- `Position`: Vector2 current location
- `Velocity`: Vector2 movement direction
- `Direction`: Float (radians) aim direction
- `CurrentRoomId`: String room location

### State Flags
- `IsAlive`: Boolean
- `IsMoving`: Boolean
- `IsSprinting`: Boolean
- `IsCasting`: Boolean
- `CastingUntil`: DateTime when cast completes

### Activity Tracking
- `LastActivity`: Timestamp of last action
- `LastAttackTime`: Last attack timestamp
- `LastDamageTime`: Last damage received timestamp
- `DeathTime`: When player died

### Delta Compression Tracking
**For Network Optimization**:
- `_lastSentPosition`: Previous sent position
- `_lastSentVelocity`: Previous sent velocity
- `_lastSentHealth`: Previous sent health
- Thresholds detect significant changes only

**Methods**:
- `HasSignificantChange()`: Check if update needed
- `MarkAsSent()`: Record sent state
- `ForceNextUpdate()`: Force next frame update (death, teleport)

## Key Classes
- `RealTimePlayer`: 139 lines, core player data
- `StatusEffect`: 9 lines, effect data
- `PlayerStats`: Network serialization model

---

# 9. WORLD AND ROOM MECHANICS

## Location
- **Game World**: `/Models/GameWorld.cs`
- **Room Model**: `/Models/Room.cs`
- **World Manager**: `/Engine/Managers/WorldManager.cs`
- **Movement System**: Handles room transitions

## Fully Implemented Features

### World Structure
**WorldId**: Unique identifier per game
**World Lifecycle**:
- `CreatedAt`: Game start timestamp
- `IsCompleted`: Game end state
- `WinningTeam`: Team that won

**World Collections**:
- `Players`: ConcurrentDictionary<string, RealTimePlayer>
- `Rooms`: Dictionary<string, Room>
- `ExtractionPoints`: Dictionary<string, ExtractionPoint>
- `AvailableLoot`: ConcurrentDictionary<string, LootItem>
- `Mobs`: ConcurrentDictionary<string, Mob>
- `LootTables`: List<LootTable> (active tables)

**Loot Tracking**:
- `TotalLootSpawned`: Counter
- `LastLootSpawn`: Timestamp for respawn interval

### Room System
**Room Grid**: Configurable 2-4 width/height
**Room ID Format**: "room_X_Y" where X, Y are coordinates

**Room Properties**:
- `Position`: Vector2 center location
- `Size`: Vector2 dimensions (50x50 default)
- `Connections`: List of adjacent room IDs
- `IsCompleted`: Whether objectives done
- `CompletedByTeam`: Which team cleared it
- `CompletedAt`: Completion timestamp
- `SpawnedLootIds`: List of loot in room

### Room Spacing & Layout
**Default Configuration**:
- Room size: 50x50 units
- Room spacing: 60 units (creates corridors)
- Grid size: 4x4 (16 total rooms)
- World bounds: ±240 units

**Room Connections**:
- Adjacent rooms only (up, down, left, right)
- No diagonal connections
- Automatic based on grid position

### Room Transitions
**Automatic Detection**:
- Movement system checks position against room bounds
- Updates `player.CurrentRoomId`
- Fires `OnRoomChanged` event
- Broadcasts encounter if multiple teams present

**Event Triggers**:
- `OnRoomChanged(player, oldRoom, newRoom)`
- `OnPlayersInRoom(world, roomId, playerList)`
- Encounter detection if enemy present

### Extraction Room Types
**4 Extraction Points** in corner rooms:
- room_0_0: Top-left extraction
- room_3_0: Top-right extraction
- room_0_3: Bottom-left extraction
- room_3_3: Bottom-right extraction

**Special Properties**:
- No mob spawning
- Extraction point location
- Safe zone (debatable)

### Room Completion
**Triggers**:
- (Not fully detailed in code, likely mob clearing)
- `Room.IsCompleted = true`
- Records winning team
- Triggers loot bonuses

**Loot Bonus**:
- First team: +1 bonus drops
- Center rooms: +1 bonus drops
- Via `LootSystem.ProcessRoomCompletionLoot()`

## Key Classes
- `GameWorld`: Core world state (19 lines model, extensive in manager)
- `Room`: Room data model (13 lines)
- `WorldManager`: 200+ lines for generation/management
- `ExtractionPoint`: 13 lines, extraction data

## Configuration Options
```csharp
WorldGeneration Settings:
- WorldSizeX: 4 rooms (configurable: 2-10)
- WorldSizeY: 4 rooms (configurable: 2-10)
- RoomSizeX: 50 units (configurable: 10-100)
- RoomSizeY: 50 units (configurable: 10-100)
- RoomSpacing: 60 units (configurable: 30-200)
- MobsPerRoom: 3 (configurable: 0-20)
- InitialLootCount: 12 (configurable: 10-200)
- LootRespawnIntervalSeconds: 120 (configurable: 30-600)
```

---

# 10. STATUS EFFECTS AND ABILITIES

## Location
- **Status Effects**: `/Models/StatusEffect.cs`
- **Combat System**: `/Engine/Combat/CombatSystem.cs` (ApplyStatusEffect, UpdateStatusEffects)
- **Loot System**: `/Engine/Loot/LootSystem.cs` (consumable effects)
- **Network**: `/Network/Models/StatusEffectUpdate.cs`, `ActiveStatusEffect.cs`

## Fully Implemented Features

### Status Effect System

**Core Properties**:
```csharp
StatusEffect:
- EffectType: String identifier (shield, slow, speed, poison, regen, stealth)
- Value: Magnitude (damage/heal per tick, speed multiplier, etc.)
- ExpiresAt: DateTime when effect expires
- SourcePlayerId: Who applied the effect
```

**Effect Updates**: Automatic removal and processing each frame

### Implemented Effects

#### 1. Shield
- **Source**: Tank Shield ability
- **Mechanism**: Damage absorption layer
- **Stacking**: No (one shield at a time)
- **Removal**: Expires after 10s or depleted
- **Value**: 50 shield points
- **Interaction**: Absorbs damage before health

#### 2. Slow
- **Source**: Tank attack (30% chance)
- **Mechanism**: Movement speed reduction
- **Effect**: `MovementSpeedModifier = 0.5f` (50% speed)
- **Duration**: 3 seconds
- **Stacking**: Overwrites previous slow
- **Removal**: Automatic, resets speed to 1.0x

#### 3. Speed Boost
- **Source**: Support Buff ability
- **Mechanism**: Movement speed increase
- **Effect**: `MovementSpeedModifier = 1.5f` (150% speed)
- **Duration**: 8 seconds
- **Stacking**: Overwrites previous speed effect
- **Removal**: Automatic, resets to 1.0x

#### 4. Poison
- **Source**: Scout attack (20% chance)
- **Mechanism**: Damage over time
- **Damage**: 5 HP per second tick
- **Duration**: 5 seconds (5 ticks)
- **Stacking**: Overwrites previous poison
- **Death Check**: Can kill player via poison
- **Removal**: Automatic after duration

#### 5. Regen
- **Source**: Consumable items (not fully implemented)
- **Mechanism**: Healing over time
- **Effect**: Restores health per second
- **Duration**: Variable per item
- **Stacking**: Overwrites previous regen

#### 6. Stealth
- **Source**: Scout Stealth ability, Invisibility potion
- **Mechanism**: Invisibility (no actual mechanic for hiding in current code)
- **Duration**: 5 seconds (ability), configurable (potion)
- **Stacking**: Overwrites previous stealth

### Ability System

**Three Class-Based Ability Sets**:

#### Scout Abilities

**1. Dash**
- **Activation**: As ability, costs 20 mana
- **Effect**: Move 10 units in target direction
- **Cooldown**: 5 seconds
- **Use Case**: Evasion, positioning
- **Damage**: None
- **Interrupt**: Can be cast while moving

**2. Stealth**
- **Activation**: As ability, costs 30 mana
- **Effect**: Apply stealth status for 5s
- **Cooldown**: 12 seconds
- **Use Case**: Escape, repositioning
- **Mechanics**: No actual hiding (cosmetic in code)

#### Tank Abilities

**1. Charge**
- **Activation**: As ability, costs 25 mana
- **Effect**: Move 8 units, damage enemies within 3 units
- **Damage**: 30 base damage
- **Cooldown**: 8 seconds
- **AoE**: 3-unit radius around arrival
- **Knockback**: None implemented
- **Group Use**: Tank initiator ability

**2. Shield**
- **Activation**: As ability, costs 40 mana
- **Effect**: 50 shield + 50% damage reduction
- **Duration**: 10 seconds
- **Cooldown**: 15 seconds
- **Defense**: Tank survival ability
- **Application**: Instant, affects user only
- **Removal**: Duration-based or depleted

#### Support Abilities

**1. Heal**
- **Activation**: As ability, costs 35 mana
- **Effect**: Restore 40 HP per teammate
- **Range**: 8 units
- **Cooldown**: 6 seconds
- **Area**: Radius-based (all teammates in range)
- **Use**: Team sustain, healing
- **Scaling**: Fixed amount (no scaling)

**2. Buff (Speed Boost)**
- **Activation**: As ability, costs 30 mana
- **Effect**: Apply speed boost to nearby teammates
- **Duration**: 8 seconds
- **Range**: 6 units
- **Cooldown**: 10 seconds
- **Buff Effect**: 1.5x movement speed
- **Duration**: Fixed 8 seconds
- **Application**: Instant to all in range

### Ability Cooldown System

**Per-Ability Cooldowns**:
- Stored in `Dictionary<string, DateTime>` per player
- Checked before ability execution
- Cooldown duration from `GetAbilityCooldown()`

**Cooldown Table**:
| Ability | Cooldown |
|---------|----------|
| Dash | 5s |
| Stealth | 12s |
| Charge | 8s |
| Shield | 15s |
| Heal | 6s |
| Buff | 10s |
| Default | 5s |

### Mana System

**Mana Usage**:
- Drain on ability cast
- Sprint costs 1 mana/sec
- Regeneration: Not implemented in code

**Ability Mana Costs**:
| Ability | Cost |
|---------|------|
| Dash | 20 |
| Stealth | 30 |
| Charge | 25 |
| Shield | 40 |
| Heal | 35 |
| Buff | 30 |

### Status Effect Lifecycle

**Application**:
1. `_combatSystem.ApplyStatusEffect(player, effect)`
2. Removes existing effects of same type
3. Applies immediate effects (shield)
4. Adds to `player.StatusEffects` list

**Processing** (each frame):
1. Check expiration: `DateTime.UtcNow >= effect.ExpiresAt`
2. Process ongoing effects (poison damage, regen heal)
3. Remove expired effects
4. Update player modifiers

**Removal**:
1. Automatic on expiration
2. `RemoveStatusEffect()` for cleanup
3. Reset modifiers to defaults

## Key Classes
- `StatusEffect`: 9 lines model
- `CombatSystem`: ApplyStatusEffect (lines 136-152), UpdateStatusEffects (lines 154-178)
- Process methods per effect type (lines 333-367)

## Configuration Options
None directly exposed; hardcoded values in CombatSystem:
- Shield value: 50
- Slow multiplier: 0.5x
- Speed multiplier: 1.5x
- Poison damage: 5 HP/s
- Poison duration: 5s
- Slow duration: 3s
- Speed duration: 8s
- Stealth duration: 5s

---

# CROSS-SYSTEM INTERACTIONS

## Game Loop
- **FPS**: 60 Hz (16.67ms per frame)
- **Order of Operations**:
  1. Process input queue
  2. Update player movement
  3. Process collisions
  4. Check room transitions
  5. Update mob behavior
  6. Process mob combat
  7. Update status effects
  8. Process loot spawning
  9. Broadcast state updates

## Event Flow
**Player Attack**:
1. Input received (PlayerInputMessage)
2. CombatSystem.ProcessAttack()
3. Damage calculation
4. Apply damage to targets
5. Fire OnCombatEvent
6. Check for death
7. Broadcast event to clients

**Player Death**:
1. Health <= 0
2. CombatSystem.ProcessPlayerDeath()
3. Trigger loot drop
4. Fire OnPlayerDeath event
5. Check team elimination
6. Broadcast death event
7. Mark for removal/respawn

**Loot Pickup**:
1. Input: LootGrabMessage
2. LootSystem.ProcessLootGrab()
3. Check distance, inventory, room
4. Transfer to inventory
5. Fire OnLootTaken
6. Update loot stats
7. Broadcast update

## Configuration Hierarchy
**GameServerSettings** (root):
- GameBalance: Combat, movement, game balance
- WorldGeneration: World layout, mob/loot spawning
- NetworkSettings: UDP config
- LobbySettings: Team formation
- TargetFPS: 60

---

# SUMMARY TABLE

| System | Status | Key Features | Classes | Config Points |
|--------|--------|--------------|---------|----------------|
| **Combat** | Complete | Attacks, abilities, damage, status effects | CombatSystem | 5 main settings |
| **Movement** | Complete | Basic movement, sprinting, collisions, anti-cheat | MovementSystem | 10+ settings |
| **Loot** | Complete | Spawn, grab, expire, dynamic rarity, drops | LootSystem | 8 settings |
| **AI/Mob** | Complete | 4 mob types, state machine, group behavior, boss AI | MobAISystem | 10 settings |
| **Extraction** | Partial | 4 points, progress tracking, duration | ExtractionPoint | 2 settings |
| **Inventory** | Complete | 20 item capacity, equipment, consumables | ItemSystem | 2 settings |
| **Teams/PvP** | Partial | Team assignment, friendly fire OFF, encounters | LobbyManager | 2 settings |
| **Stats** | Partial | Level/XP tracking, modifiers, no progression | RealTimePlayer | None |
| **World/Rooms** | Complete | Grid layout, 16 rooms, room transitions | WorldManager | 6 settings |
| **Effects/Abilities** | Complete | 6 effect types, 6 abilities, cooldown system | CombatSystem | Hardcoded |

---

# NOTABLE IMPLEMENTATION DETAILS

## Delta Compression
- RealTimePlayer tracks `_lastSentPosition`, `_lastSentHealth` etc.
- Only sends updates when `HasSignificantChange()` returns true
- Thresholds: 0.01 units position, 0.01 velocity, 0.5 radians direction
- Reduces bandwidth by 70-90%
- Force update after death, teleport via `ForceNextUpdate()`

## Anti-Cheat Detection
- MovementSystem tracks per-player `PlayerMovementTracker`
- Detects impossible speeds, teleportation, high-speed hacking
- Suspicion level 0-1.0 scale
- Flags players with >5 suspicious movements for monitoring

## Performance Optimization
- AI processing by priority (Critical > High > Medium > Low)
- Spatial grid for collision (32-unit cells)
- Lazy updates (only update if `RequiresUpdate = true`)
- Object pooling for network messages
- Concurrent collections for thread safety

## Networking Features
- UDP with packet reordering via InputBuffer
- MessagePack serialization
- Reliable message acknowledgement
- Session recovery with reconnection system
- Delta compression for player states


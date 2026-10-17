using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using ClientPlugin.Bootstrap;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering;
using Sandbox;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine.Utils;
using Sandbox.Engine.Voxels;
using Sandbox.Game.World.Generator;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.EntityComponents.Renders;
using VRage;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using Sandbox.Game.SessionComponents;

namespace CreativeFixtureHarness;

/// <summary>Test-only game plugin. The explicit gate and exact isolated data marker are mandatory.</summary>
public sealed partial class Plugin : IPlugin
{
    private const string WorldName = "SE-Vulkan Isolated Creative Fixture";
    private readonly Stopwatch clock = new Stopwatch();
    private readonly List<string> captureNames = new List<string>();
    private string reports, userData, loadWorld, baselineWorld;
    private RendererBackend expectedBackend = RendererBackend.Vulkan;
    private bool transparencyMode;
    private bool started, sawSession, spawned, textCameraSet, textCaptured, spritesPrepared, spritesCaptured, playerCameraSet, playerCaptured, completed, unloading, exiting, failed;
    private bool doorOpened, doorClosed, animationCameraSet, animationCaptured;
    private double spawnedAt, nextReport, completedAt;
    private double parityExerciseSeconds, parityLastExerciseTick;
    private int duration = 240;
    private MyCubeGrid platform, smallGrid, previewGrid;
    private MyCharacter astronaut;
    private MyTextPanel panel;
    private MyDoor door;
    private MyWindTurbine turbine;
    private int animatorCommands;
    private float doorMaximumOpenRatio;
    private Vector2 lcdTextureSize, lcdSurfaceSize;
    private bool movementStarted, movementStopped, walkingCaptured, smallGridDeletionRequested;
    private bool lightsChanged, worldDisplayChecked;
    private long billboardCommands;
    private Vector3D movementStart;
    private double astronautTravel;
    private bool planetMode;private MyPlanet planet;private int planetStage;private Vector3D planetCenter,planetSurface;
    private bool asteroidMode,asteroidNear,asteroidMid,asteroidFar,asteroidReturn,asteroidBefore,asteroidMined,asteroidAfter,asteroidReloaded,asteroidReloadCapture;
    private MyVoxelMap asteroid;
    private Vector3D asteroidCenter;
    private readonly Vector3D asteroidCorner=new Vector3D(80,20,-140);
    private string asteroidBeforeStatus,asteroidAfterStatus;
    private Vector3D? asteroidNextCamera;
    private double asteroidCameraAt;

    public void Init(object gameInstance)
    {
        planetMode=Environment.GetEnvironmentVariable("SE_VULKAN_PLANET_FIXTURE")=="1";
        asteroidMode=Environment.GetEnvironmentVariable("SE_VULKAN_ASTEROID_FIXTURE")=="1";
        if (Environment.GetEnvironmentVariable("SE_VULKAN_CREATIVE_FIXTURE") != "1")
            throw new InvalidOperationException("CreativeFixtureHarness requires explicit isolated fixture mode.");
        userData = FullDirectory(Environment.GetEnvironmentVariable("SE_VULKAN_CREATIVE_USER_DATA"));
        reports = FullDirectory(Environment.GetEnvironmentVariable("SE_VULKAN_CREATIVE_REPORT_DIR"));
        string marker = Path.Combine(userData, ".se-vulkan-creative-fixture");
        if (!File.Exists(marker) || !SameDirectory(File.ReadAllText(marker).Trim(), userData) || !SameDirectory(MyFileSystem.UserDataPath, userData))
            throw new InvalidOperationException("The actual user-data path must match the explicitly marked isolated fixture directory.");
        string runRoot = Path.GetDirectoryName(userData);
        if (!Path.GetFileName(userData).Equals("GameData", StringComparison.OrdinalIgnoreCase) || !IsWithin(reports, runRoot) || SameDirectory(reports, userData))
            throw new InvalidOperationException("Fixture reports and GameData must belong to the same isolated run directory.");
        if (MySession.Static != null) throw new InvalidOperationException("Fixture harness refuses to replace an existing session.");
        string requested = Environment.GetEnvironmentVariable("SE_VULKAN_CREATIVE_BACKEND");
        if (!string.IsNullOrEmpty(requested) && !Enum.TryParse(requested, true, out expectedBackend)) throw new InvalidOperationException("Fixture backend must be Vulkan or Dx11.");
        string existingWorld = Environment.GetEnvironmentVariable("SE_VULKAN_CREATIVE_LOAD_WORLD");
        if (!string.IsNullOrWhiteSpace(existingWorld))
        {
            loadWorld = FullDirectory(existingWorld);
            string worldMarker = Path.Combine(loadWorld, ".se-vulkan-private-fixture-world");
            if (!IsWithin(loadWorld, userData) || !File.Exists(worldMarker) || File.ReadAllText(worldMarker).Trim() != WorldName || !File.Exists(Path.Combine(loadWorld, "Sandbox.sbc")))
                throw new InvalidOperationException("Baseline reload accepts only a marked private fixture save contained in this isolated GameData.");
        }
        int parsed;
        if (int.TryParse(Environment.GetEnvironmentVariable("SE_VULKAN_CREATIVE_DURATION_SECONDS"), out parsed) && parsed >= 120 && parsed <= 600) duration = parsed;
        transparencyMode=Environment.GetEnvironmentVariable("SE_VULKAN_TRANSPARENCY_FIXTURE")=="1";
        Directory.CreateDirectory(reports);
        RenderDocCapture.Initialize(reports);
        // This is a private process configuration. Never synchronize test saves or config.
        MySandboxGame.Config.EnableCloud = false;
        MySandboxGame.Config.WelcomScreenCurrentStatus = MyConfig.WelcomeScreenStatus.AlreadySeen;
        MySandboxGame.Config.GDPRConsent = false;
        MySandboxGame.Config.GoodBotHints = false;
        MySandboxGame.Config.ExperimentalMode = true; // The marked fixture enables spectator and unlimited Creative block limits.
        if (MySandboxGame.Config.UseCloudToSaveGame || MySandboxGame.Config.UseCloudToSaveConfig || MySandboxGame.Config.UseCloudToSaveLastSession)
            throw new InvalidOperationException("The isolated fixture requires local save/config storage.");
        MySession.LoadingStep += PreparePrivateSessionDirectory;
        MySandboxGame.Config.Save();
        File.WriteAllText(Path.Combine(reports, "harness-started.txt"), DateTime.UtcNow.ToString("u"));
        clock.Start();
        Report("Initialized isolated fixture gate. Waiting for the Vulkan menu and definitions.");
    }

    public void Update()
    {
        try
        {
            MyScreenManager.GetFirstScreenOfType<MyGuiScreenGDPR>()?.CloseScreenNow();
            MyScreenManager.GetFirstScreenOfType<MyGuiScreenWelcomeScreen>()?.CloseScreenNow();
            if (clock.Elapsed.TotalSeconds >= nextReport) { nextReport += 10; Report("Fixture progress"); }
            if (started && !spawned && MySession.Static != null) sawSession = true;
            if (started && !spawned && sawSession && MySession.Static == null) throw new InvalidOperationException("World loading returned to the menu; inspect the flushed isolated SpaceEngineers.log for the loading exception.");
            if (!failed && !started && clock.Elapsed.TotalSeconds >= 15 && MyDefinitionManager.Static != null && MyDefinitionManager.Static.GetScenarioDefinitions().Any())
                StartWorld();
            if (!failed && started && !spawned && MySession.Static != null && MySession.Static.Ready) { if (loadWorld == null) Spawn(); else Restore(); }
            if (!failed && spawned && !completed) Exercise();
            if (completed && !unloading && clock.Elapsed.TotalSeconds - completedAt >= 10)
            {
                unloading = true;
                Report("Unloading only the isolated fixture world.");
                MySessionLoader.UnloadAndExitToMenu();
                File.WriteAllText(Path.Combine(reports, "fixture-unloaded.txt"), DateTime.UtcNow.ToString("u"));
            }
            if (!exiting && ((unloading && clock.Elapsed.TotalSeconds - completedAt >= 20) || clock.Elapsed.TotalSeconds >= duration || failed))
            {
                exiting = true;
                if (!completed && !failed) Fail(new TimeoutException("Fixture did not finish before its bounded duration."));
                Report("Controlled test exit requested.");
                File.WriteAllText(Path.Combine(reports, "exit-requested.txt"), DateTime.UtcNow.ToString("u"));
                MySandboxGame.ExitThreadSafe();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void StartWorld()
    {
        if (BackendRuntime.ActiveBackend != expectedBackend) throw new InvalidOperationException("The isolated fixture requires the requested active backend: " + expectedBackend);
        if (MySession.Static != null) throw new InvalidOperationException("An unrelated session appeared; fixture creation was refused.");
        // Startup reloads the game config after plugin Init. Apply this only
        // after the menu is ready, immediately before loading the marked save.
        MySandboxGame.Config.ExperimentalMode = true;
        MySandboxGame.Config.Save();
        MyScenarioDefinition scenario = MyDefinitionManager.Static.GetScenarioDefinitions().FirstOrDefault(value => value.Id.SubtypeName == "EmptyWorld");
        if (scenario == null || scenario.HasPlanets) throw new InvalidOperationException("Installed stock EmptyWorld scenario was not found.");
        var settings = new MyObjectBuilder_SessionSettings {
            GameMode = MyGameModeEnum.Creative, OnlineMode = MyOnlineModeEnum.OFFLINE, AutoSaveInMinutes = 0,
            ProceduralDensity = 0, ProceduralSeed = 1, CargoShipsEnabled = false, EnableEncounters = false,
            EnableDrones = false, EnableEconomy = false, EnableRespawnShips = false, StartInRespawnScreen = false,
            // Current game versions also gate economy faction generation on these two fields.
            GlobalEncounterCap = 0, EnablePlanetaryEncounters = false,
            EnableSpectator = true, EnableSunRotation = false, EnvironmentHostility = MyEnvironmentHostilityEnum.SAFE,
            TrashRemovalEnabled = false, StationVoxelSupport = false, WeatherSystem = false
        };
        started = true;
        if (loadWorld != null)
        {
            File.WriteAllText(Path.Combine(reports, "world-load-requested.txt"), loadWorld);
            Report("Loading only the explicitly marked isolated fixture save for " + expectedBackend + " comparison.");
            MySessionLoader.LoadSingleplayerSession(loadWorld);
            return;
        }
        File.WriteAllText(Path.Combine(reports, "world-create-requested.txt"), "Stock EmptyWorld; Creative; offline; no mods, planets, asteroids or encounters.");
        VulkanDiagnostics.Set("Creative fixture acceptance", SubsystemState.Active, "Isolated test-only EmptyWorld loading; acceptance is awaiting gameplay and capture review.");
        Report("Starting a new stock EmptyWorld Creative session in isolated GameData.");
        if(Environment.GetEnvironmentVariable("SE_VULKAN_ANOMALY_PARITY")=="1"){settings.EnableOxygen=true;settings.EnableOxygenPressurization=true;}
        MySessionLoader.StartNewSession(WorldName, settings, new List<MyObjectBuilder_Checkpoint.ModItem>(), scenario, asteroidAmount: 0, encounterAmount: 0);
    }

    private void PreparePrivateSessionDirectory(LoadingProgress step)
    {
        if (!started || loadWorld != null || step != LoadingProgress.PROGRESS_STEP2) return;
        string path = MySession.Static?.CurrentPath;
        if (MySession.Static?.Name != WorldName || string.IsNullOrEmpty(path) || !IsWithin(path, userData))
            throw new InvalidOperationException("The new fixture session path must remain within isolated GameData.");
        // Start chooses a nonexistent path, then initializes grid storage before its first Save.
        Directory.CreateDirectory(path);
        // The public Start path predates these limits; normal checkpoint loading initializes them.
        MySession session = MySession.Static;
        session.NPCBlockLimits ??= new MyNPCBlockLimits(new MyBlockLimits(session.PiratePCU, 0), new MyBlockLimits(session.GlobalEncounterPCU, 0));
        session.GlobalBlockLimits ??= new MyBlockLimits(session.TotalPCU, 0);
        session.SessionBlockLimits ??= new MyBlockLimits(session.TotalPCU, 0);
    }

    private void Spawn()
    {
        if (!MySession.Static.CreativeMode || !IsWithin(MySession.Static.CurrentPath, userData))
            throw new InvalidOperationException("The created session is not the expected isolated Creative world.");
        long owner = MySession.Static.LocalHumanPlayer?.Identity.IdentityId ?? 0;
        var large = Grid("Vulkan large-grid platform", MyCubeSize.Large, new Vector3D(0, -2.5, 0), true);
        for (int x = -4; x <= 4; ++x)
            for (int z = -4; z <= 4; ++z)
            {
                var armor = Block<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock", new Vector3I(x, 0, z), owner);
                armor.ColorMaskHSV = new Vector3(((x + z + 12) % 3) * .2f, -.25f, -.1f);
                large.CubeBlocks.Add(armor);
            }
        var battery = Block<MyObjectBuilder_BatteryBlock>("LargeBlockBatteryBlock", new Vector3I(-4, 1, 2), owner);
        battery.CurrentStoredPower = 3; battery.MaxStoredPower = 3; battery.ChargeMode = 2; battery.OnlyDischargeEnabled = true;
        large.CubeBlocks.Add(battery);
        var gravity = Block<MyObjectBuilder_GravityGenerator>("", new Vector3I(3, 1, 3), owner);
        gravity.FieldSize = new Vector3(80); gravity.GravityAcceleration = 9.81f;
        large.CubeBlocks.Add(gravity);
        for (int i = 0; i < 2; ++i)
        {
            var light = Block<MyObjectBuilder_InteriorLight>("SmallLight", new Vector3I(i == 0 ? -3 : 3, 1, -3), owner);
            light.Radius = 20; light.Intensity = 3; light.Falloff = 1;
            light.ColorRed = i == 0 ? 1 : .2f; light.ColorGreen = .6f; light.ColorBlue = i == 0 ? .2f : 1;
            large.CubeBlocks.Add(light);
        }
        var lcd = Block<MyObjectBuilder_TextPanel>("LargeLCDPanel", new Vector3I(0, 1, -3), owner);
        lcd.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Backward, Base6Directions.Direction.Up);
        lcd.PublicDescription = "SE-VULKAN 1.4\nCREATIVE FIXTURE\nLarge + small grid\nAstronaut / lights / gravity";
        lcd.ContentType = ContentType.TEXT_AND_IMAGE; lcd.FontSize = .8f; lcd.BackgroundColor = new Color(0, 20, 40); lcd.FontColor = Color.Cyan;
        large.CubeBlocks.Add(lcd);
        var construction = Block<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock", new Vector3I(-1, 1, -1), owner);
        construction.BuildPercent = .25f; construction.IntegrityPercent = .25f;
        large.CubeBlocks.Add(construction);
        large.CubeBlocks.Add(Block<MyObjectBuilder_Door>("", new Vector3I(-1, 1, 2), owner));
        large.CubeBlocks.Add(Block<MyObjectBuilder_WindTurbine>("LargeBlockWindTurbine", new Vector3I(1, 1, 0), owner));
        large.CubeBlocks.Add(Block<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock", new Vector3I(-3, 1, 2), owner));
        var spot = Block<MyObjectBuilder_ReflectorLight>("LargeBlockFrontLight", new Vector3I(-3, 2, 2), owner);
        spot.Radius = 6; spot.ReflectorRadius = 40; spot.Intensity = 1; spot.Falloff = 1.5f;
        spot.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Down, Base6Directions.Direction.Forward);
        large.CubeBlocks.Add(spot);
        if(transparencyMode)for(int i=0;i<3;++i)large.CubeBlocks.Add(Block<MyObjectBuilder_CubeBlock>("Window1x1Flat",new Vector3I(i-1,2,-4),owner));
        platform = (MyCubeGrid)MyEntities.CreateFromObjectBuilderAndAdd(large, false);
        panel = platform.GetCubeBlock(new Vector3I(0, 1, -3))?.FatBlock as MyTextPanel;
        if (panel == null) throw new InvalidOperationException("The external LCD block did not instantiate.");
        door = platform.GetCubeBlock(new Vector3I(-1, 1, 2))?.FatBlock as MyDoor;
        turbine = platform.GetCubeBlock(new Vector3I(1, 1, 0))?.FatBlock as MyWindTurbine;
        if (door == null || turbine == null) throw new InvalidOperationException("The door and RenderDirect turbine fixture blocks did not instantiate.");
        var small = Grid("Vulkan small-grid fixture", MyCubeSize.Small, new Vector3D(5, 0, -4), false);
        for (int x = -1; x <= 1; ++x)
            for (int z = -1; z <= 1; ++z) small.CubeBlocks.Add(Block<MyObjectBuilder_CubeBlock>("SmallBlockArmorBlock", new Vector3I(x, 0, z), owner));
        small.Skeleton = new List<BoneInfo> { new BoneInfo { BonePosition = new Vector3I(0, 2, 0), BoneOffset = new Vector3UByte(160, 128, 128) } };
        smallGrid = (MyCubeGrid)MyEntities.CreateFromObjectBuilderAndAdd(small, false);
        astronaut = MyCharacter.CreateCharacter(MatrixD.CreateWorld(new Vector3D(2, 0, -1), Vector3D.Forward, Vector3D.Up), Vector3.Zero,
            "Vulkan fixture astronaut", "Default_Astronaut", new Vector3(.15f, -.2f, -.1f), null, findNearPos: false, useInventory: false, addDefaultItems: false);
        astronaut.JetpackComp?.TurnOnJetpack(false);
        if (MySession.Static.LocalCharacter != null)
        {
            MatrixD playerWorld = MatrixD.CreateWorld(new Vector3D(-3, 0, -1), Vector3D.Forward, Vector3D.Up);
            MySession.Static.LocalCharacter.PositionComp.SetWorldMatrix(ref playerWorld);
            MySession.Static.LocalCharacter.JetpackComp?.TurnOnJetpack(false);
        }
        BeginExercise();
        if(asteroidMode)
        {
            var providerType=typeof(MyWorldGenerator).Assembly.GetType("Sandbox.Game.World.Generator.MyCompositeShapeProvider",true);
            var shape=(VRage.Voxels.IMyStorageDataProvider)providerType.GetMethod("CreateAsteroidShape",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{43191,128f,0,null});
            var storage=new MyOctreeStorage(shape,new Vector3I(128));
            asteroid=MyWorldGenerator.AddVoxelMap("Vulkan asteroid fixture",storage,asteroidCorner);asteroidCenter=asteroid.PositionComp.WorldAABB.Center;
        }
        SavePrivateFixture(preserveBaseline: true);
        WriteFixture("Spawned; waiting for physics, GPU resources and LCD redraw.");
        Report("Spawned real large/small grids, astronaut, battery, gravity, local lights and external LCD.");
    }

    private void Restore()
    {
        if (!MySession.Static.CreativeMode || MySession.Static.Name != WorldName || !IsWithin(MySession.Static.CurrentPath, userData))
            throw new InvalidOperationException("Reloaded save is not the isolated Creative fixture.");
        platform = MyEntities.GetEntities().OfType<MyCubeGrid>().FirstOrDefault(grid => grid.DisplayName == "Vulkan large-grid platform");
        smallGrid = MyEntities.GetEntities().OfType<MyCubeGrid>().FirstOrDefault(grid => grid.DisplayName == "Vulkan small-grid fixture");
        astronaut = MyEntities.GetEntities().OfType<MyCharacter>().FirstOrDefault(character => character.DisplayNameText == "Vulkan fixture astronaut");
        panel = platform?.GetCubeBlock(new Vector3I(0, 1, -3))?.FatBlock as MyTextPanel;
        door = platform?.GetCubeBlock(new Vector3I(-1, 1, 2))?.FatBlock as MyDoor;
        turbine = platform?.GetCubeBlock(new Vector3I(1, 1, 0))?.FatBlock as MyWindTurbine;
        if (platform == null || smallGrid == null || astronaut == null || panel == null) throw new InvalidOperationException("The marked save did not contain every fixture entity.");
        var surface = (Sandbox.ModAPI.Ingame.IMyTextSurface)panel;
        surface.ContentType = ContentType.TEXT_AND_IMAGE;
        surface.WriteText("SE-VULKAN 1.4\nCREATIVE FIXTURE\nLarge + small grid\nAstronaut / lights / gravity");
        if(asteroidMode){asteroid=MyEntities.GetEntities().OfType<MyVoxelMap>().Single(v=>v.StorageName=="Vulkan asteroid fixture");asteroidCenter=asteroid.PositionComp.WorldAABB.Center;}
        BeginExercise();
        WriteFixture("Reloaded the private fixture for " + expectedBackend + " baseline capture.");
    }

    private void BeginExercise()
    {
        var surface = (Sandbox.ModAPI.Ingame.IMyTextSurface)panel;
        lcdTextureSize = surface.TextureSize; lcdSurfaceSize = surface.SurfaceSize;
        if (lcdTextureSize.X <= 0 || lcdTextureSize.Y <= 0 || lcdSurfaceSize.X <= 0 || lcdSurfaceSize.Y <= 0)
            throw new InvalidOperationException("The LCD did not expose valid texture and surface dimensions.");
        var preview = Grid("Vulkan construction preview", MyCubeSize.Large, new Vector3D(-5, 3, -5), true);
        preview.CreatePhysics = false;
        preview.CubeBlocks.Add(Block<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock", Vector3I.Zero, 0));
        previewGrid = (MyCubeGrid)MyEntities.CreateFromObjectBuilderAndAdd(preview, false);
        previewGrid.IsPreview = true; previewGrid.Save = false;
        foreach (var block in previewGrid.GetBlocks()) ((VRage.Game.ModAPI.IMySlimBlock)block).Dithering = .45f;
        Camera(new Vector3D(15, 11, -19), new Vector3D(0, 0, 0));
        spawned = true; spawnedAt = clock.Elapsed.TotalSeconds;
        parityLastExerciseTick=spawnedAt;
        BeginAnomalyParity();
    }

    private void SavePrivateFixture(bool preserveBaseline = false)
    {
        if (!IsWithin(MySession.Static.CurrentPath, userData) || !MySession.Static.Save()) throw new InvalidOperationException("Private fixture save failed.");
        File.WriteAllText(Path.Combine(MySession.Static.CurrentPath, ".se-vulkan-private-fixture-world"), WorldName);
        File.WriteAllText(Path.Combine(reports, "fixture-save-path.txt"), MySession.Static.CurrentPath);
        if (preserveBaseline)
        {
            baselineWorld = Path.Combine(userData, "FixtureBaseline");
            if (loadWorld != null || Directory.Exists(baselineWorld)) throw new InvalidOperationException("The initial private fixture snapshot must be created exactly once.");
            CopyPrivateSave(MySession.Static.CurrentPath, baselineWorld);
            File.WriteAllText(Path.Combine(baselineWorld, ".se-vulkan-initial-fixture-snapshot"), WorldName);
            File.WriteAllText(Path.Combine(reports, "fixture-initial-save-path.txt"), baselineWorld);
        }
    }

    private void CopyPrivateSave(string source, string destination)
    {
        if (!IsWithin(source, userData) || !IsWithin(destination, userData) || IsWithin(destination, source))
            throw new InvalidOperationException("The fixture snapshot must stay within private GameData and outside its source save.");
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Fixture snapshots reject linked files.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        }
        foreach (string directory in Directory.GetDirectories(source))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Fixture snapshots reject linked directories.");
            CopyPrivateSave(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private void Exercise()
    {
        // Submit through the same public game path as mods and stock effects.
        // Native counters cannot establish this acceptance without live packets.
        var square = MyStringId.GetOrCompute("Square");
        MyTransparentGeometry.AddPointBillboard(square, new Vector4(1, 0, 0, .5f), new Vector3D(5, 1, -8), .4f, 0, blendType: MyBillboard.BlendTypeEnum.Standard);
        MyTransparentGeometry.AddPointBillboard(square, new Vector4(0, 1, 0, .5f), new Vector3D(5.3, 1, -8), .4f, 0, blendType: MyBillboard.BlendTypeEnum.AdditiveBottom);
        MyTransparentGeometry.AddPointBillboard(square, new Vector4(0, 0, 1, .5f), new Vector3D(5.6, 1, -8), .4f, 0, blendType: MyBillboard.BlendTypeEnum.AdditiveTop);
        billboardCommands += 3;
        double seconds = clock.Elapsed.TotalSeconds - spawnedAt;
        if(anomalyParityMode) {
            // Cold shader compilation must not schedule several camera/capture
            // stages in one update. Keep wall time only for the outer timeout.
            double now=clock.Elapsed.TotalSeconds;
            parityExerciseSeconds+=Math.Min(.1,Math.Max(0,now-parityLastExerciseTick));
            parityLastExerciseTick=now;seconds=parityExerciseSeconds;
        }
        ExercisePostprocessing(seconds);if(transparencyMode)ExerciseTransparency(seconds);
        ExerciseAnomalyParity(seconds);
        if(asteroidMode)ExerciseAsteroid(seconds);
        if(planetMode)ExercisePlanet(seconds);
        if (door != null) doorMaximumOpenRatio = Math.Max(doorMaximumOpenRatio, door.OpenRatio);
        // Empty space has no wind; this test drives the real block's RenderDirect animator explicitly.
        if (turbine != null)
            foreach (var subpart in turbine.Subparts.Values)
                if (subpart.Render is MyRenderComponentWindTurbine.TurbineRenderComponent render)
                { render.SetSpeed(1.5f); animatorCommands++; }
        if (captureNames.Count == 0 && seconds >= 15) Capture("fixture-overview");
        if (!doorOpened && door != null && seconds >= 18) { doorOpened = true; ((Sandbox.ModAPI.Ingame.IMyDoor)door).OpenDoor(); }
        if (!textCameraSet && seconds >= 22)
        {
            textCameraSet = true;
            LcdCamera();
        }
        if (!textCaptured && seconds >= 25) { textCaptured = true; Capture("fixture-lcd-text"); }
        if (!animationCameraSet && door != null && seconds >= 29) { animationCameraSet = true; Camera(new Vector3D(15, 11, -19), Vector3D.Zero); }
        if (!animationCaptured && door != null && seconds >= 32) { animationCaptured = true; Capture("fixture-animation"); }
        // A cold render compile can advance wall time past both thresholds in
        // one Update. Preserve the actual open/close animation in that case.
        if (!doorClosed && door != null && seconds >= 34 && door.OpenRatio > .95f) { doorClosed = true; ((Sandbox.ModAPI.Ingame.IMyDoor)door).CloseDoor(); }
        if (!spritesPrepared && seconds >= 35)
        {
            spritesPrepared = true;
            LcdCamera();
            var surface = (Sandbox.ModAPI.Ingame.IMyTextSurface)panel;
            surface.ContentType = ContentType.SCRIPT; surface.Script = "";
            Vector2 center = lcdTextureSize * .5f;
            Vector2 visibleOrigin = (lcdTextureSize - lcdSurfaceSize) * .5f;
            Vector2 clipOrigin = visibleOrigin + lcdSurfaceSize * .125f;
            Vector2 clipSize = lcdSurfaceSize * .75f;
            using (var frame = surface.DrawFrame())
            {
                frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", center, lcdTextureSize, new Color(0, 20, 40)));
                using (frame.Clip((int)clipOrigin.X, (int)clipOrigin.Y, (int)clipSize.X, (int)clipSize.Y))
                {
                    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", center, lcdSurfaceSize * new Vector2(600f / 512, 120f / 512), Color.DarkCyan, rotation: .3f));
                    var text = MySprite.CreateText("VULKAN 1.4\nLCD SPRITES\nCLIPPING", "Debug", Color.White, 1.1f * Math.Min(lcdSurfaceSize.X, lcdSurfaceSize.Y) / 512f);
                    text.Position = visibleOrigin + lcdSurfaceSize * new Vector2(.5f, 180f / 512); frame.Add(text);
                }
            }
            MatrixD moved = MatrixD.CreateTranslation(5.5, 0, -3.5); smallGrid.PositionComp.SetWorldMatrix(ref moved);
            Report("Submitted real LCD text/sprites/clipping and moved the small grid.");
        }
        if (!spritesCaptured && seconds >= 42) { spritesCaptured = true; Capture("fixture-lcd-sprites"); }
        if (!lightsChanged && seconds >= 48)
        {
            var point = platform.GetCubeBlock(new Vector3I(3, 1, -3))?.FatBlock as MyLightingBlock;
            var spot = platform.GetCubeBlock(new Vector3I(-3, 2, 2))?.FatBlock as MyLightingBlock;
            if (point == null || spot == null) throw new InvalidOperationException("Fixture lights are missing.");
            point.Color = Color.Magenta; point.Radius = 8;
            spot.Color = Color.Green; spot.ReflectorRadius = 25;
            lightsChanged = true;
            File.WriteAllText(Path.Combine(reports, "lights-changed.txt"), "Point=magenta/radius8; spot=green/reflectorRadius25; stock block setters.");
        }
        if (!playerCameraSet && seconds >= 52)
        {
            playerCameraSet = true;
            var local = MySession.Static.LocalCharacter;
            if (local == null) throw new InvalidOperationException("The isolated Creative session has no local playable astronaut.");
            MySession.Static.SetCameraController(MyCameraControllerEnum.Entity, local);
        }
        if (!playerCaptured && seconds >= 55) { playerCaptured = true; Capture("fixture-player-hud"); }
        if (seconds >= 57 && seconds < 62)
        {
            if (!movementStarted) { movementStarted = true; movementStart = astronaut.PositionComp.GetPosition(); }
            // An uncontrolled fixture character does not consume ordinary player
            // indicators. Use the stock copied network-command queue so Simulate
            // executes movement, physics and animation through its normal path.
            Vector3 walk = new Vector3(0, 0, -.25f); Quaternion rotation = Quaternion.Identity;
            astronaut.CacheMove(ref walk, ref rotation);
            Vector3D delta = astronaut.PositionComp.GetPosition() - movementStart;
            astronautTravel = Math.Max(astronautTravel, Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z));
            if (seconds >= 59)
            {
                Vector3D target = astronaut.PositionComp.GetPosition() + Vector3D.Up;
                Camera(target + new Vector3D(4, 1, 4), target);
            }
            if (!walkingCaptured && seconds >= 60)
            {
                walkingCaptured = true;
                Capture("fixture-astronaut-walking");
            }
        }
        if (movementStarted && !movementStopped && seconds >= 62)
        {
            movementStopped = true;
            Vector3 stop = Vector3.Zero; Quaternion rotation = Quaternion.Identity;
            astronaut.CacheMove(ref stop, ref rotation);
            Report("Exercised walking/skinning in the marked fixture; travel=" + astronautTravel.ToString("F3", CultureInfo.InvariantCulture));
        }
        if (!smallGridDeletionRequested && seconds >= 65)
        {
            if (smallGrid.GetBlocks().Count != 9) throw new InvalidOperationException("The fixture small grid lost blocks before its lifecycle test.");
            smallGridDeletionRequested = true;
            smallGrid.Close();
        }
        if (seconds >= (planetMode?275:asteroidMode?175:transparencyMode?155:anomalyParityMode?150:72))
        {
            if(anomalyParityMode && !parityComplete) { if(seconds>240)throw new InvalidOperationException("Anomaly parity did not reach producer readiness/capture completion; inspect anomaly-parity.txt.");return; }
            Vector3 gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(new Vector3D(0, 0, 0));
            if (gravity.Length() < 1 || !panel.IsWorking || platform.GetBlocks().Count < 80 || !smallGrid.Closed || astronautTravel < .1)
                throw new InvalidOperationException("Fixture physics/power/lifecycle requirements failed: gravity=" + gravity + ", LCD working=" + panel.IsWorking
                    + ", small grid closed=" + smallGrid.Closed + ", astronaut horizontal travel=" + astronautTravel);
            if (turbine != null && animatorCommands == 0) throw new InvalidOperationException("The turbine did not expose its real RenderDirect animator subpart.");
            if (door != null && (!doorOpened || !doorClosed || doorMaximumOpenRatio <= .5f || door.OpenRatio > .05f))
                throw new InvalidOperationException("The powered fixture door did not complete an open/close cycle: maximum=" + doorMaximumOpenRatio + ", final=" + door.OpenRatio);
            foreach (string name in captureNames) if (!File.Exists(Path.Combine(reports, name + ".png")))
                throw new InvalidOperationException("A requested native fixture capture was not produced: " + name);
            SavePrivateFixture();
            RenderDocCapture.VerifyDelivery();
            if (!worldDisplayChecked)
            {
                var session = MySession.Static;
                var previous = BackendRuntime.RequestedBackend;
                var alternate = BackendRuntime.ActiveBackend == RendererBackend.Vulkan ? RendererBackend.Dx11 : RendererBackend.Vulkan;
                var cancel = new MyGuiScreenOptionsDisplay(); MyGuiSandbox.AddScreen(cancel);
                cancel.Controls.OfType<MyGuiControlCombobox>().Single(control => control.Name == "VulkanRendererSelector").SelectItemByKey((long)alternate);
                cancel.CloseScreenNow();
                if (BackendRuntime.RequestedBackend != previous) throw new InvalidOperationException("In-world Display Cancel committed its draft.");
                var accept = new MyGuiScreenOptionsDisplay(); MyGuiSandbox.AddScreen(accept);
                accept.Controls.OfType<MyGuiControlCombobox>().Single(control => control.Name == "VulkanRendererSelector").SelectItemByKey((long)alternate);
                accept.OnOkClick(null);
                if (BackendRuntime.RequestedBackend != alternate || !BackendRuntime.RestartRequired || !ReferenceEquals(session, MySession.Static) || !session.Ready)
                    throw new InvalidOperationException("In-world renderer selection exited the fixture or failed to request a restart.");
                worldDisplayChecked = true;
                File.WriteAllText(Path.Combine(reports, "world-display-passed.txt"), "Cancel discarded draft; OK requested=" + alternate + "; active=" + BackendRuntime.ActiveBackend + "; world remains ready.");
            }
            completed = true; completedAt = clock.Elapsed.TotalSeconds;
            WriteFixture("Simulation fixture and native capture delivery completed; visual parity requires capture review.");
            File.WriteAllText(Path.Combine(reports, "fixture-completed.txt"), DateTime.UtcNow.ToString("u"));
            Report("Fixture simulation checks and all requested screenshot deliveries completed. Visual acceptance remains under review.");
        }
    }

    private void ExercisePlanet(double seconds)
    {
        if(planetStage==0 && seconds>=75){
            var renderSettings=MyRenderProxy.Settings;renderSettings.User.GrassDrawDistance=250;renderSettings.User.GrassDensityFactor=1;MyRenderProxy.SwitchRenderSettings(renderSettings);
            planet=MyWorldGenerator.AddPlanet("Vulkan Earthlike fixture","Vulkan Earthlike fixture","EarthLike",new Vector3D(50000,30000,-60000),731,19000,true,userCreated:true);
            if(planet==null)throw new InvalidOperationException("The stock EarthLike planet could not be created.");
            planetCenter=planet.WorldMatrix.Translation;Vector3D normal=Vector3D.Normalize(new Vector3D(.3,.5,1));
            MySector.SunProperties.BaseSunDirectionNormalized=(Vector3)normal;MySector.SunProperties.SunDirectionNormalized=(Vector3)normal;
            Camera(planetCenter+normal*40000,planetCenter);planetStage=1;Report("Created stock EarthLike; orbit camera, atmosphere and clouds.");
        }
        if(planetStage==1 && seconds>=100){Capture("planet-orbit");planetStage=2;}
        if(planetStage==2 && seconds>=105){var normal=Vector3D.Normalize(new Vector3D(.3,.5,1));planetSurface=planet.GetClosestSurfacePointGlobal(planetCenter+normal*15000);Camera(planetSurface+normal*300,planetSurface);planetStage=3;}
        if(planetStage==3 && seconds>=130){Capture("planet-approach");planetStage=4;}
        if(planetStage==4 && seconds>=135){var normal=Vector3D.Normalize(planetSurface-planetCenter);var tangent=Vector3D.Normalize(Vector3D.Cross(normal,Vector3D.Right));Camera(planetSurface+normal*5-tangent*8,planetSurface+tangent*15);planetStage=5;}
        if(planetStage==5 && seconds>=180){Capture("planet-surface-foliage");planetStage=6;}
        if(planetStage==6 && seconds>=185){var weather=MySession.Static.GetComponent<MySectorWeatherComponent>();weather.FogDensityOverride=.03f;weather.FogMultiplierOverride=1;weather.FogColorOverride=new Vector3(.3f,.4f,.5f);weather.FogSkyboxOverride=.8f;weather.FogAtmoOverride=.5f;planetStage=7;}
        if(planetStage==7 && seconds>=200){Capture("planet-weather-fog");planetStage=8;}
        if(planetStage==8 && seconds>=205){var weather=MySession.Static.GetComponent<MySectorWeatherComponent>();weather.FogDensityOverride=null;weather.FogMultiplierOverride=null;weather.FogColorOverride=null;weather.FogSkyboxOverride=null;weather.FogAtmoOverride=null;
            Vector3D cut=planetSurface-Vector3D.Normalize(planetSurface-planetCenter)*3;bool changed=MyVoxelGenerator.CutOutSphereFast(planet,ref cut,8,out Vector3I min,out Vector3I max,true);if(!changed)throw new InvalidOperationException("Planet mining failed to change content.");planetStage=9;Report("Mined stock planet surface; clipmap invalidation and foliage replacement.");}
        if(planetStage==9 && seconds>=225){Capture("planet-after-mining");SavePrivateFixture();planetStage=10;}
        if(planetStage==10 && seconds>=230){var normal=Vector3D.Normalize(new Vector3D(.3,.5,1));Camera(planetCenter+normal*40000,planetCenter);var setting=planet.AtmosphereSettings;setting.RayleighHeight*=.7f;planet.AtmosphereSettings=setting;planetStage=11;}
        if(planetStage==11 && seconds>=250){Capture("planet-atmosphere-settings");planetStage=12;}
        if(planetStage==12 && seconds>=255){string status=VulkanDiagnostics.Report();if(expectedBackend==RendererBackend.Vulkan && (!status.Contains("Planet atmosphere/clouds: Active") || !status.Contains("Voxel foliage/wind: Active")))throw new InvalidOperationException("Planet fixture has no observed atmosphere/cloud/foliage draws.");File.WriteAllText(Path.Combine(reports,"planet-evidence.txt"),status);planetStage=13;}
    }

    private void ExerciseAsteroid(double seconds)
    {
        // Screenshot requests are consumed asynchronously by the render thread.
        // Hold the current camera briefly before starting the next LOD view.
        if(asteroidNextCamera.HasValue && seconds>=asteroidCameraAt){Camera(asteroidNextCamera.Value,asteroidCenter);asteroidNextCamera=null;}
        // Inspect the sun-facing surface so stock triplanar channels are visible.
        Vector3D towardSun=MySector.DirectionToSunNormalized;
        if(!towardSun.IsValid() || towardSun.LengthSquared()<.5)
        {
            // EmptyWorld can leave its disabled rotating-sun direction undefined.
            // Configure the private fixture's environment, never a player save.
            towardSun=Vector3D.Normalize(new Vector3D(.3,.5,1));
            MySector.SunProperties.BaseSunDirectionNormalized=(Vector3)towardSun;
            MySector.SunProperties.SunDirectionNormalized=(Vector3)towardSun;
        }
        if(!asteroidNear&&seconds>=75){asteroidNear=true;Camera(asteroidCenter+towardSun*180,asteroidCenter);}
        if(asteroidNear&&!asteroidMid&&seconds>=92){Capture("asteroid-near");asteroidBeforeStatus=VulkanDiagnostics.Report();asteroidMid=true;asteroidNextCamera=asteroidCenter+towardSun*1400;asteroidCameraAt=seconds+.5;}
        if(asteroidMid&&!asteroidFar&&seconds>=108){Capture("asteroid-middle-lod");asteroidFar=true;asteroidNextCamera=asteroidCenter+towardSun*6000;asteroidCameraAt=seconds+.5;}
        if(asteroidFar&&!asteroidReturn&&seconds>=121){Capture("asteroid-far-lod");asteroidReturn=true;asteroidNextCamera=asteroidCenter+towardSun*150;asteroidCameraAt=seconds+.5;}
        if(!asteroidBefore&&seconds>=129){asteroidBefore=true;Capture("asteroid-before-mining");}
        if(!asteroidMined&&seconds>=132)
        {
            Vector3D cut=asteroidCenter+towardSun*25;
            bool changed=MyVoxelGenerator.CutOutSphereFast(asteroid,ref cut,30,out Vector3I min,out Vector3I max,true);
            if(!changed)throw new InvalidOperationException("Asteroid mining did not change storage content.");
            asteroidMined=true;File.WriteAllText(Path.Combine(reports,"asteroid-mining.txt"),"Content operation changed=true; invalidation range="+min+".."+max+"; center="+cut);Report("Applied stock voxel mining operation and storage invalidation.");
        }
        if(!asteroidAfter&&seconds>=144){asteroidAfter=true;Capture("asteroid-after-mining");asteroidAfterStatus=VulkanDiagnostics.Report();}
        if(!asteroidReloaded&&seconds>=150)
        {
            SavePrivateFixture();string storageFile=Path.Combine(MySession.Static.CurrentPath,asteroid.StorageName+".vx2");
            if(!File.Exists(storageFile))throw new FileNotFoundException("Saved asteroid storage was not delivered.",storageFile);
            var restored=MyStorageBase.LoadFromFile(storageFile,cache:false);if(restored==null)throw new InvalidOperationException("Saved mined voxel storage did not reload.");
            asteroid.Close();asteroid=MyWorldGenerator.AddVoxelMap("Vulkan asteroid reloaded",restored,asteroidCorner);asteroidReloaded=true;
            File.WriteAllText(Path.Combine(reports,"asteroid-storage-reloaded.txt"),storageFile);Report("Reloaded the saved mined asteroid storage into a new render actor.");
        }
        if(!asteroidReloadCapture&&seconds>=168)
        {
            asteroidReloadCapture=true;Capture("asteroid-reloaded");
            string final=VulkanDiagnostics.Report();
            if(expectedBackend==RendererBackend.Vulkan && (!final.Contains("Voxels: Active") || !final.Contains("mesh updates=")))throw new InvalidOperationException("Asteroid fixture has no observed native voxel draws.");
            File.WriteAllText(Path.Combine(reports,"asteroid-evidence.txt"),"BEFORE LOD/MINING\n"+asteroidBeforeStatus+"\nAFTER MINING\n"+asteroidAfterStatus+"\nAFTER STORAGE RELOAD\n"+final);
        }
    }
    private static MyObjectBuilder_CubeGrid Grid(string name, MyCubeSize size, Vector3D position, bool isStatic) => new MyObjectBuilder_CubeGrid {
        DisplayName = name, GridSizeEnum = size, IsStatic = isStatic, CreatePhysics = true,
        PersistentFlags = MyPersistentEntityFlags2.Enabled | MyPersistentEntityFlags2.InScene,
        PositionAndOrientation = new MyPositionAndOrientation(position, Vector3.Forward, Vector3.Up)
    };
    private static T Block<T>(string subtype, Vector3I position, long owner) where T : MyObjectBuilder_CubeBlock, new() => new T {
        SubtypeName = subtype, Min = position, IntegrityPercent = 1, BuildPercent = 1, Owner = owner, BuiltBy = owner,
        BlockOrientation = SerializableBlockOrientation.Identity
    };
    private void Camera(Vector3D position, Vector3D target)
    {
        if(!position.IsValid() || !target.IsValid())throw new InvalidOperationException("Fixture camera must be finite.");
        MySession.Static.SetCameraController(MyCameraControllerEnum.Spectator, null, position);
        MySpectator.Static.Position = position; MySpectator.Static.SetTarget(target, Vector3D.Up);
        MySpectator.Static.SpectatorCameraMovement = MySpectatorCameraMovementEnum.None;
    }
    private void LcdCamera()
    {
        MatrixD world = panel.PositionComp.WorldMatrixRef;
        Camera(world.Translation - world.Forward * 3 + world.Up * .3, world.Translation);
    }
    private void Capture(string name)
    {
        captureNames.Add(name);
        RenderDocCapture.Request(name);
        MyRenderProxy.TakeScreenshot(Vector2.One, Path.Combine(reports, name + ".png"), debug: false, ignoreSprites: false, showNotification: false);
        File.AppendAllText(Path.Combine(reports, "capture-requests.txt"), name + " " + DateTime.UtcNow.ToString("u") + "\n");
        File.AppendAllText(Path.Combine(reports, "capture-camera.txt"), name + "; camera=" + MySector.MainCamera.Position
            + "; controller=" + MySession.Static.CameraController.GetType().FullName + "; astronaut=" + astronaut.PositionComp.GetPosition() + "\n");
    }
    private void WriteFixture(string phase)
    {
        new XDocument(new XElement("CreativeFixture", new XAttribute("Phase", phase), new XAttribute("VisualAcceptance", "PendingReview"),
            new XElement("World", MySession.Static?.CurrentPath ?? ""), new XElement("LargeGrid", platform?.EntityId ?? 0),
            new XElement("InitialBaseline", baselineWorld ?? loadWorld ?? ""),
            new XElement("LargeBlocks", platform?.GetBlocks().Count ?? 0), new XElement("SmallGrid", smallGrid?.EntityId ?? 0),
            new XElement("SmallGridDeleted", smallGrid?.Closed ?? false), new XElement("AstronautTravel", astronautTravel),
            new XElement("SmallBlocks", smallGrid?.GetBlocks().Count ?? 0), new XElement("Astronaut", astronaut?.EntityId ?? 0),
            new XElement("LocalPlayer", MySession.Static?.LocalCharacterEntityId ?? 0), new XElement("LcdWorking", panel?.IsWorking ?? false),
            new XElement("LcdTextureSize", lcdTextureSize.ToString()), new XElement("LcdSurfaceSize", lcdSurfaceSize.ToString()),
            new XElement("Preview", previewGrid?.EntityId ?? 0), new XElement("PreviewDither", .45f),
            new XElement("Door", door?.EntityId ?? 0), new XElement("DoorOpenRatio", door?.OpenRatio ?? 0), new XElement("DoorMaximumOpenRatio", doorMaximumOpenRatio),
            new XElement("RenderDirectTurbine", turbine?.EntityId ?? 0), new XElement("AnimatorCommands", animatorCommands),
            new XElement("BillboardCommands", billboardCommands), new XElement("LightsChanged", lightsChanged), new XElement("WorldDisplayChecked", worldDisplayChecked),
            new XElement("Gravity", MyGravityProviderSystem.CalculateTotalGravityInPoint(Vector3D.Zero).ToString()),
            new XElement("Captures", captureNames.Select(name => new XElement("File", name + ".png"))))).Save(Path.Combine(reports, "fixture.xml"));
    }
    private void Report(string message)
    {
        string text = "CreativeFixtureHarness " + clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s: " + message + "\n" + LoadingState() + "\n" + VulkanDiagnostics.Report();
        File.WriteAllText(Path.Combine(reports, "status-latest.txt"), text);
        File.AppendAllText(Path.Combine(reports, "status-history.txt"), text + "\n\n");
        MyLog.Default?.WriteLineAndConsole(text);
        MyLog.Default?.Flush();
    }
    private static string LoadingState()
    {
        var text = new StringBuilder("Game ready=" + MySandboxGame.IsGameReady + "; update ready=" + MySandboxGame.IsUpdateReady + "; render tasks finished=" + MySandboxGame.RenderTasksFinished + "; clipmaps ready=" + MySandboxGame.AreClipmapsReady);
        text.Append("; session=" + (MySession.Static?.Name ?? "none") + "; session ready=" + (MySession.Static?.Ready ?? false));
        object loading = MyGuiScreenLoading.Static;
        if (loading != null)
            foreach (string name in new[] { "m_runLoadEnded", "m_loadFinished", "m_exceptionDuringLoad", "m_loadInDrawFinished", "m_isStreamed", "m_progress" })
            {
                FieldInfo field = loading.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) text.Append("; " + name + "=" + field.GetValue(loading));
            }
        return text.ToString();
    }
    private void Fail(Exception error)
    {
        if (failed) return;
        failed = true;
        File.WriteAllText(Path.Combine(reports, "fixture-failed.txt"), error.ToString());
        VulkanDiagnostics.Set("Creative fixture acceptance", SubsystemState.Failed, error.Message);
        Report("Fixture failed: " + error);
    }
    public void Dispose() { MySession.LoadingStep -= PreparePrivateSessionDirectory; if (reports != null) { Report("Harness disposed."); File.WriteAllText(Path.Combine(reports, "harness-disposed.txt"), DateTime.UtcNow.ToString("u")); } }
    private static string FullDirectory(string path) => string.IsNullOrWhiteSpace(path) ? throw new InvalidOperationException("An explicit isolated directory is required.") : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static bool SameDirectory(string a, string b) => !string.IsNullOrWhiteSpace(a) && FullDirectory(a).Equals(FullDirectory(b), StringComparison.OrdinalIgnoreCase);
    private static bool IsWithin(string path, string root) => FullDirectory(path).StartsWith(FullDirectory(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

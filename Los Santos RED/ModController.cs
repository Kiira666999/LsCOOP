using LosSantosRED.lsr.Helper;
using LosSantosRED.lsr.Coop.Core;
using Rage;
using Rage.Native;
using RAGENativeUI;
using RAGENativeUI.Elements;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;

namespace LosSantosRED.lsr
{
    public class ModController
    {
        public List<ModTaskGroup> TaskGroups { get; private set; } = new List<ModTaskGroup>();
        private Civilians Civilians;
        private Debug Debug;
        private Dispatcher Dispatcher;
        private Input Input;   
        private PedSwap PedSwap;   
        private Mod.Player Player;
        private Police Police;  
        private Mod.Tasker Tasker;
        private Mod.Time Time;
        private UI UI;
        private VanillaManager VanillaManager;
        private NAudioPlayer NAudioPlayer;
        private NAudioPlayer NAudioPlayer2;
        private WeatherReporting Weather;
        private Mod.World World;
        private Mod.Crafting Crafting;
        public LsrCoopCharacterManager CoopCharacterManager { get; private set; }
        public CoopPermissionService CoopPermissionService { get; private set; }
        public ModDataFileManager ModDataFileManager { get; private set; }
        private WeatherManager WeatherManager;

        public ModController()
        {
            ModDataFileManager = new ModDataFileManager();//test
        }
        public bool IsRunning { get; private set; }
        public bool RunUI { get; set; } = true;
        public bool RunInput { get; set; } = true;
        public bool RunOther { get; set; } = true;
        public bool RunVanilla { get; set; } = true;
        public bool RunMenuOnly { get; set; } = true;
        public bool IsDisplayingAlertScreen { get; set; } = false;
        public bool IsBootstrapOnly { get; private set; }
        public void Setup()
        {
            IsRunning = true;
            IsBootstrapOnly = false;
            EntryPoint.IsLoadingAltConfig = false;
            while (Game.IsLoading)
            {
                GameFiber.Yield();
            }
            Game.FadeScreenOut(500, true);


            ModDataFileManager = new ModDataFileManager();
            ModDataFileManager.Setup();
            GameFiber.Yield();

            NAudioPlayer = new NAudioPlayer(ModDataFileManager.Settings);
            NAudioPlayer.Setup();
            GameFiber.Yield();
            NAudioPlayer2 = new NAudioPlayer(ModDataFileManager.Settings);
            NAudioPlayer2.Setup();
            GameFiber.Yield();
            Time = new Mod.Time(ModDataFileManager.Settings);
            Time.Setup();
            GameFiber.Yield();
            World = CreateWorld();
            CoopCharacterStartupSnapshot startupSnapshot = CoopCharacterSnapshotStartupBridge.TryReadReadySnapshot();
            CoopCharacterSnapshotStartupBridge.ApplyModelBeforeCreatePlayer(startupSnapshot);
            Player = CreatePlayer(World);
            World.Setup(Player, Player);
            GameFiber.Yield();
            Player.Setup();
            CoopCharacterSnapshotStartupBridge.ApplyAppearanceAfterPlayerSetup(startupSnapshot, Player);
            CoopCharacterSnapshotStartupBridge.ApplyProfileHydrationAfterPlayerSetup(startupSnapshot, Player, ModDataFileManager.ModItems, ModDataFileManager.Crimes);
            GameFiber.Yield();
            CoopCharacterManager = new LsrCoopCharacterManager();
            LocalCoopCharacter localCoopCharacter = CoopCharacterManager.RegisterLocalCharacter(Player);
            CoopCharacterSnapshotStartupBridge.HydrateLocalCharacter(localCoopCharacter, startupSnapshot);
            CoopPermissionService = new CoopPermissionService();
            ConfigureCoopPermissions(true, LsrCoopMode.ActiveHost);
            GameFiber.Yield();
            Police = new Police(World, Player, Player, ModDataFileManager.Settings, Player, Time);
            GameFiber.Yield();
            Civilians = new Civilians(World, Player, Player, ModDataFileManager.Settings, ModDataFileManager.Gangs);
            GameFiber.Yield();
            PedSwap = CreatePedSwap();
            GameFiber.Yield();
            Player.PedSwap = PedSwap;
            Tasker = new Mod.Tasker(World, Player, ModDataFileManager.Weapons, ModDataFileManager.Settings, ModDataFileManager.PlacesOfInterest);
            Tasker.Setup();
            GameFiber.Yield();
            Weather = new WeatherReporting(NAudioPlayer, ModDataFileManager.Settings, Time, Player);
            Weather.Setup();
            GameFiber.Yield();
            Dispatcher = new Dispatcher(World, Player, ModDataFileManager.Agencies, ModDataFileManager.Settings, ModDataFileManager.Streets, ModDataFileManager.Zones, ModDataFileManager.Jurisdictions, ModDataFileManager.Weapons, ModDataFileManager.Names, ModDataFileManager.Crimes,
                ModDataFileManager.RelationshipGroups, ModDataFileManager.Gangs, ModDataFileManager.GangTerritories, ModDataFileManager.ShopMenus, ModDataFileManager.PlacesOfInterest, Weather, 
                Time, ModDataFileManager.ModItems, ModDataFileManager.Organizations, ModDataFileManager.Interiors, ModDataFileManager.DispatchablePeople, ModDataFileManager.DispatchableVehicles, Player);
            Dispatcher.Setup();
            Player.Dispatcher = Dispatcher;
            Player.Weather = Weather;
            GameFiber.Yield();
            Crafting = new Mod.Crafting(Player, ModDataFileManager.CraftableItems, ModDataFileManager.ModItems, ModDataFileManager.Settings,ModDataFileManager.Weapons);
            Crafting.Setup();
            GameFiber.Yield();
            UI = new UI(Player, ModDataFileManager.Settings, ModDataFileManager.Jurisdictions, PedSwap, ModDataFileManager.PlacesOfInterest, Player, Player, Player, ModDataFileManager.Weapons, ModDataFileManager.RadioStations, ModDataFileManager.GameSaves, World, Player, Player, Tasker, Player, 
                ModDataFileManager.ModItems, Time, Player, ModDataFileManager.Gangs, ModDataFileManager.GangTerritories, ModDataFileManager.Zones, ModDataFileManager.Streets, ModDataFileManager.Interiors, Dispatcher, ModDataFileManager.Agencies, Player, ModDataFileManager.DanceList, ModDataFileManager.GestureList, 
                ModDataFileManager.ShopMenus, Player, ModDataFileManager.Crimes, ModDataFileManager.LocationTypes, ModDataFileManager.Intoxicants, ModDataFileManager.PlateTypes, ModDataFileManager.Names, ModDataFileManager, Player, Crafting, Player, CoopPermissionService);
            UI.Setup();
            GameFiber.Yield();
            Input = new Input(Player, ModDataFileManager.Settings, UI, PedSwap);
            GameFiber.Yield();
            VanillaManager = new VanillaManager(ModDataFileManager.Settings, ModDataFileManager.PlacesOfInterest, ModDataFileManager.SpawnBlocks);
            VanillaManager.Setup();
            GameFiber.Yield();
            WeatherManager = new WeatherManager(ModDataFileManager.Settings, Time, ModDataFileManager.WeatherForecasts);
            WeatherManager.Setup();
            GameFiber.Yield();
            Debug = new Debug(ModDataFileManager.PlateTypes, World, Player, ModDataFileManager.Streets, Dispatcher, ModDataFileManager.Zones, ModDataFileManager.Crimes, this, ModDataFileManager.Settings, Tasker, Time, ModDataFileManager.Agencies, ModDataFileManager.Weapons, ModDataFileManager.ModItems, Weather,
                ModDataFileManager.PlacesOfInterest, ModDataFileManager.Interiors, ModDataFileManager.Gangs, Input, ModDataFileManager.ShopMenus, ModDataFileManager);
            Debug.Setup();
            GameFiber.Yield();
            PedSwap.Setup();
            GameFiber.Yield();

            SetTaskGroups();
            GameFiber.Yield();
            StartCoreLogic();
            GameFiber.Yield();
            StartUILogic();
            GameFiber.Yield();
            StartInputLogic();
            GameFiber.Yield();
#if DEBUG
            StartDebugLogic();
            GameFiber.Yield();
#endif
            
            UI.SetupDebugMenu();
            LoadDefaultMap();
            Game.FadeScreenIn(500, true);
            DisplayLoadSuccessfulMessage();

            //string ConfigName = string.IsNullOrEmpty(Config.ConfigName) ? "No Config" : Config.ConfigName + " Config";
            

            //if (!string.IsNullOrEmpty(Config.ConfigName))
            //{
            //    Game.DisplayNotification($"~s~Los Santos ~r~RED~s~ {ConfigName} Loaded");
            //    EntryPoint.WriteToConsole($"Loaded {ConfigName}", 0); 
            //}
        }
        public void SetupBootstrapOnly()
        {
            IsRunning = true;
            IsBootstrapOnly = true;
            EntryPoint.IsLoadingAltConfig = false;
            while (Game.IsLoading)
            {
                GameFiber.Yield();
            }

            Game.FadeScreenOut(500, true);
            ModDataFileManager = new ModDataFileManager();
            ModDataFileManager.Setup();
            GameFiber.Yield();

            NAudioPlayer = new NAudioPlayer(ModDataFileManager.Settings);
            NAudioPlayer.Setup();
            NAudioPlayer2 = new NAudioPlayer(ModDataFileManager.Settings);
            NAudioPlayer2.Setup();
            Time = new Mod.Time(ModDataFileManager.Settings);
            Time.Setup();
            World = CreateWorld();
            Player = CreatePlayer(World);
            Player.Setup();
            CoopCharacterManager = new LsrCoopCharacterManager();
            CoopCharacterManager.RegisterLocalCharacter(Player);
            CoopPermissionService = new CoopPermissionService();
            ConfigureCoopPermissions(false, LsrCoopMode.BootstrapOnly);
            PedSwap = CreatePedSwap();
            Player.PedSwap = PedSwap;
            PedSwap.Setup();
            NativeFunction.Natives.FREEZE_ENTITY_POSITION(Game.LocalPlayer.Character, false);
            Game.FadeScreenIn(500, true);
            Game.DisplayNotification("LSR co-op character creation mode");
            EntryPoint.WriteToConsole("Co-op BootstrapOnly character creation mode started", 0);
            PedSwap.BecomeCreatorPed();
        }
        public void SetupClientMode()
        {
            IsRunning = true;
            IsBootstrapOnly = false;
            EntryPoint.IsLoadingAltConfig = false;
            while (Game.IsLoading)
            {
                GameFiber.Yield();
            }

            Game.FadeScreenOut(500, true);
            ModDataFileManager = new ModDataFileManager();
            ModDataFileManager.Setup();
            GameFiber.Yield();

            NAudioPlayer = new NAudioPlayer(ModDataFileManager.Settings);
            NAudioPlayer.Setup();
            NAudioPlayer2 = new NAudioPlayer(ModDataFileManager.Settings);
            NAudioPlayer2.Setup();
            Time = new Mod.Time(ModDataFileManager.Settings);
            Time.Setup();
            World = CreateWorld();
            CoopCharacterStartupSnapshot startupSnapshot = CoopCharacterSnapshotStartupBridge.TryReadReadySnapshot();
            CoopCharacterSnapshotStartupBridge.ApplyModelBeforeCreatePlayer(startupSnapshot);
            Player = CreatePlayer(World);
            Player.Setup();
            CoopCharacterSnapshotStartupBridge.ApplyAppearanceAfterPlayerSetup(startupSnapshot, Player);
            CoopCharacterSnapshotStartupBridge.ApplyProfileHydrationAfterPlayerSetup(startupSnapshot, Player, ModDataFileManager.ModItems, ModDataFileManager.Crimes);
            CoopCharacterManager = new LsrCoopCharacterManager();
            LocalCoopCharacter localCoopCharacter = CoopCharacterManager.RegisterLocalCharacter(Player);
            CoopCharacterSnapshotStartupBridge.HydrateLocalCharacter(localCoopCharacter, startupSnapshot);
            CoopPermissionService = new CoopPermissionService();
            ConfigureCoopPermissions(true, LsrCoopMode.Client);
            PedSwap = CreatePedSwap();
            Player.PedSwap = PedSwap;
            PedSwap.Setup();
            NativeFunction.Natives.FREEZE_ENTITY_POSITION(Game.LocalPlayer.Character, false);
            Game.FadeScreenIn(500, true);
            Game.DisplayNotification("LSR co-op client mode");
            EntryPoint.WriteToConsole("Co-op ClientMode started without full simulation systems", 0);
        }
        public void SetupFileOnly()
        {
            while (Game.IsLoading)
            {
                GameFiber.Yield();
            }
            ModDataFileManager = new ModDataFileManager();
            ModDataFileManager.Setup();
        }
        public void Dispose(bool restoreInitialPedModel = true)
        {
            IsRunning = false;
            Player?.Dispose();
            World?.Dispose();
            PedSwap?.Dispose(restoreInitialPedModel);
            Dispatcher?.Dispose();
            VanillaManager?.Dispose();
            UI?.Dispose();
            Time?.Dispose();
            Weather?.Dispose();
            Debug?.Dispose();
            WeatherManager?.Dispose();



            Game.DisplayNotification("~s~Los Santos ~r~RED ~s~Deactivated");
            EntryPoint.WriteToConsole($"Has Been Deactivated", 0);
        }
        public void CrashUnload()
        {
            DisplayCrashMessage();
            Dispose();
        }
        private void SetTaskGroups()
        {
            TaskGroups = new List<ModTaskGroup>
            {
                new ModTaskGroup("RG1:Player", new List<ModTask>()
                {
                     new ModTask(100, "Player.Update", Player.Update, 0),//1
                     new ModTask(250, "UI.UpdateData", UI.CacheData, 1),//500
                }),
                new ModTaskGroup("RG2:Player Gen", new List<ModTask>()
                {
                    new ModTask(250, "Player.Violations.Update", Player.Violations.Update, 0),
                    new ModTask(250, "Player.CurrentPoliceResponse.Update", Player.PoliceResponse.Update, 1),
                    new ModTask(250, "Player.Investigation.Update", Player.Investigation.Update, 2),
                    new ModTask(250, "Player.SearchModeUpdate", Player.SearchMode.Update, 3),

                    new ModTask(250, "Player.TrafficViolationsUpdate", Player.Violations.TrafficViolations.Update, 4),
                    new ModTask(250, "Player.LocationUpdate", Player.LocationUpdate, 5),
                    new ModTask(250, "Player.ArrestWarrantUpdate",Player.CriminalHistory.Update, 6),//these were all 500
                }),
                new ModTaskGroup("RG3:World Gen", new List<ModTask>()//something in here is causing a hang on some crapola computers
                {
                    new ModTask(1000, "World.PrunePedestrians", World.Pedestrians.Prune, 0),

                    //THIS IS THE ISSUE!
                    new ModTask(500, "World.CreateNewPedestrians", World.Pedestrians.CreateNew, 1), //this is the freezer, what the fucko

                    new ModTask(1000, "World.PruneVehicles", World.Vehicles.Prune, 2),//500
                    new ModTask(500, "World.CreateNewVehicles", World.Vehicles.CreateNew, 3),//1000 //very bad performance   
                    new ModTask(1000, "World.UpdateVehiclePlates", World.Vehicles.PlateController.UpdatePlates, 5),

                    new ModTask(1500, "Player.ScannerUpdate", Player.Scanner.Update, 6),
                    new ModTask(1000, "World.Pedestrians.UpdateDead", World.Pedestrians.UpdateDead, 7),
                }),
                new ModTaskGroup("RG4:Dispatch", new List<ModTask>()
                {
                    new ModTask(500, "Dispatcher.Recall", Dispatcher.Recall, 0),//1500
                    new ModTask(500, "Dispatcher.Dispatch", Dispatcher.Dispatch, 1),//1500//1000
                }),
                new ModTaskGroup("RG5:Police Update", new List<ModTask>()
                {
                    new ModTask(250, "Police.Update", Police.Update,0),//250
                }),
                new ModTaskGroup("RG6:Civilian Update", new List<ModTask>()
                {
                    new ModTask(250, "Civilians.Update", Civilians.UpdateCivilians, 0),//500//250
                }),
                new ModTaskGroup("RG:7 GangMember Update", new List<ModTask>()
                {
                    new ModTask(250, "Civilians.UpdateGangMembers", Civilians.UpdateGangMembers, 0),//500//250
                }),
                new ModTaskGroup("RG8:Merchant Update", new List<ModTask>()
                {
                    new ModTask(250, "Civilians.UpdateMerchants", Civilians.UpdateMerchants, 0),//500//250
                }),
                new ModTaskGroup("RG9:EMT Update", new List<ModTask>()
                {
                    new ModTask(250, "Civilians.UpdateEMTs", Civilians.UpdateEMTs, 0),//500//250
                    new ModTask(200, "Civilians.UpdateTotalWanted", Civilians.UpdateTotalWanted, 1),//500//250
                    new ModTask(250, "Civilians.UpdateSecurityGuards", Civilians.UpdateSecurityGuards, 2),//500//250
                    new ModTask(250, "Civilians.UpdateFirefighters", Civilians.UpdateFirefighters, 3),//500//250
                }),
                new ModTaskGroup("RG10:World LowPri", new List<ModTask>()
                {
                    new ModTask(1000, "World.ActiveNearLocations", World.Places.ActivateLocations, 0),//1000 //????MAYBE BAD?

                    new ModTask(4000, "Weather.Update", Weather.Update, 1),//1000
                    new ModTask(2000,"WeatherManager.Update",WeatherManager.Update,2),

                    new ModTask(1000, "World.UpdateNear", World.Places.UpdateLocations, 3),//500//1000//????MAYBE BAD?

                    new ModTask(2000, "Player.GangRelationshipsUpdate", Player.RelationshipManager.GangRelationships.Update, 4),//might become a priority...
                    new ModTask(5000, "Player.Properties.Update", Player.Properties.Update, 5),//might become a priority...

                    new ModTask(1000, "World.Update", World.Update, 6),///????MAYBE BAD?
                }),
                new ModTaskGroup("RG11:TaskerUpdate", new List<ModTask>()
                {
                    new ModTask(500, "Tasker.UpdatePolice", Tasker.UpdatePolice, 0),
                    new ModTask(500, "Tasker.UpdateCivilians", Tasker.UpdateCivilians, 1),
                }),
            };
        }
        private Mod.World CreateWorld()
        {
            return new Mod.World(ModDataFileManager.Agencies, ModDataFileManager.Zones, ModDataFileManager.Jurisdictions, ModDataFileManager.Settings, ModDataFileManager.PlacesOfInterest, ModDataFileManager.PlateTypes,
                ModDataFileManager.Names, ModDataFileManager.RelationshipGroups, ModDataFileManager.Weapons, ModDataFileManager.Crimes, Time, ModDataFileManager.ShopMenus, ModDataFileManager.Interiors, NAudioPlayer,
                ModDataFileManager.Gangs, ModDataFileManager.GangTerritories, ModDataFileManager.Streets, ModDataFileManager.ModItems, ModDataFileManager.RelationshipGroups, ModDataFileManager.LocationTypes,
                ModDataFileManager.Organizations, ModDataFileManager.Contacts, ModDataFileManager);
        }

        private Mod.Player CreatePlayer(Mod.World world)
        {
            return new Mod.Player(Game.LocalPlayer.Character.Model.Name, Game.LocalPlayer.Character.IsMale, ModDataFileManager.Names.GetRandomName(Game.LocalPlayer.Character.Model.Name, Game.LocalPlayer.Character.IsMale), world, Time,
                ModDataFileManager.Streets, ModDataFileManager.Zones, ModDataFileManager.Settings, ModDataFileManager.Weapons, ModDataFileManager.RadioStations, ModDataFileManager.Scenarios, ModDataFileManager.Crimes, NAudioPlayer,
                NAudioPlayer2, ModDataFileManager.PlacesOfInterest, ModDataFileManager.Interiors, ModDataFileManager.ModItems, ModDataFileManager.Intoxicants, ModDataFileManager.Gangs, ModDataFileManager.Jurisdictions,
                ModDataFileManager.GangTerritories, ModDataFileManager.GameSaves, ModDataFileManager.Names, ModDataFileManager.ShopMenus, ModDataFileManager.RelationshipGroups, ModDataFileManager.DanceList, ModDataFileManager.SpeechList,
                ModDataFileManager.Seats, ModDataFileManager.Agencies, ModDataFileManager.SavedOutfits, ModDataFileManager.VehicleSeatDoorData, ModDataFileManager.Cellphones, ModDataFileManager.Contacts, ModDataFileManager.VehicleRaces, ModDataFileManager.DispatchableVehicles, ModDataFileManager.DispatchablePeople);
        }

        private PedSwap CreatePedSwap()
        {
            return new PedSwap(Time, Player, ModDataFileManager.Settings, World, ModDataFileManager.Weapons, ModDataFileManager.Crimes, ModDataFileManager.Names, ModDataFileManager.ModItems, World, ModDataFileManager.RelationshipGroups,
                ModDataFileManager.ShopMenus, ModDataFileManager.DispatchablePeople, ModDataFileManager.Heads, ModDataFileManager.ClothesNames, ModDataFileManager.Gangs, ModDataFileManager.Agencies, ModDataFileManager.TattooNames, ModDataFileManager.GameSaves, ModDataFileManager.SavedOutfits, Player, Player, CoopPermissionService);
        }

        private void ConfigureCoopPermissions(bool localCharacterExists, LsrCoopMode mode)
        {
            CoopPermissionService.SetLocalCharacterExists(localCharacterExists);
            CoopPermissionService.SetSessionState(new CoopSessionState
            {
                Mode = CoopStartupBridge.IsCoopEnabled ? mode : LsrCoopMode.Disabled,
                WorldId = new CoopWorldId(CoopStartupBridge.WorldId),
                LocalCharacterId = new CoopCharacterId(CoopStartupBridge.LocalProfileId),
                ActiveHostCharacterId = new CoopCharacterId(CoopStartupBridge.ActiveHostProfileId),
            });
        }
        private void StartCoreLogic()
        {
            foreach(ModTaskGroup modTaskGroup in TaskGroups)
            {
                //Start a new gamefiber to loop our tasks
                GameFiber.StartNew(delegate
                {
                    try
                    {
                        while (IsRunning)
                        {
                            modTaskGroup.Update();
                            GameFiber.Yield();
                        }
                    }
                    catch (Exception e)
                    {
                        EntryPoint.WriteToConsole("Error: " + e.Message + " : " + e.StackTrace, 0);
                        DisplayCrashMessage();
                        Dispose();
                    }
                }, $"Run Logic {modTaskGroup.Name}");
            } 
            GameFiber.Yield();
        }
        private void StartInputLogic()
        {
            GameFiber.StartNew(delegate
            {
                try
                {
                    while (IsRunning)
                    {
                        if(RunInput)
                        {
                            Input.Tick();
                        }
                        GameFiber.Yield();
                    }
                }
                catch (Exception e)
                {
                    EntryPoint.WriteToConsole("Error: " + e.Message + " : " + e.StackTrace, 0);
                    DisplayCrashMessage();
                    Dispose();
                }
            }, "Run Input Logic");

            GameFiber.StartNew(delegate
            {
                try
                {
                    while (IsRunning)
                    {
                        if (RunVanilla)
                        {
                            VanillaManager.Tick();
                        }
                        GameFiber.Yield();
                    }
                }
                catch (Exception e)
                {
                    EntryPoint.WriteToConsole("Error: " + e.Message + " : " + e.StackTrace, 0);
                    DisplayCrashMessage();
                    Dispose();
                }
            }, "Run Vanilla Manager Logic");
        }
        private void StartUILogic()
        {
            GameFiber.StartNew(delegate
            {
                try
                {
                    while (IsRunning)
                    {
                        if (RunUI)
                        {
                            UI.Tick1();
                        }
                        else if(RunMenuOnly)
                        {
                            UI.MenuOnly();
                        }
                        GameFiber.Yield();
                    }
                }
                catch (Exception e)
                {
                    EntryPoint.WriteToConsole("Error: " + e.Message + " : " + e.StackTrace, 0);
                    DisplayCrashMessage();
                    Dispose();
                }
            }, "Run UI Logic");
            GameFiber.StartNew(delegate
            {
                try
                {
                    while (IsRunning)
                    {
                        if (RunUI)
                        {
                            UI.Tick2();
                        }
                        GameFiber.Yield();
                    }
                }
                catch (Exception e)
                {
                    EntryPoint.WriteToConsole("Error: " + e.Message + " : " + e.StackTrace, 0);
                    DisplayCrashMessage();
                    Dispose();
                }
            }, "Run UI Logic 2");
            GameFiber.StartNew(delegate
            {
                try
                {
                    while (IsRunning)
                    {
                        if (RunUI)
                        {
                            UI.Tick3();
                            GameFiber.Yield();
                            Time.Tick();//this was below before, but shouldnt be any different
                        }
                        GameFiber.Yield();
                    }
                }
                catch (Exception e)
                {
                    EntryPoint.WriteToConsole("Error: " + e.Message + " : " + e.StackTrace, 0);
                    DisplayCrashMessage();
                    Dispose();
                }
            }, "Run UI Logic 3");
        }
        private void StartDebugLogic()
        {
            GameFiber.StartNew(delegate
            {
                try
                {
                    while (IsRunning)
                    {
                        if (RunOther)
                        {
                            Debug.Update();
                        }
                        GameFiber.Yield();
                    }
                }
                catch (Exception e)
                {
                    Game.DisplayNotification("CHAR_BLANK_ENTRY", "CHAR_BLANK_ENTRY", "~o~Error", "Los Santos ~r~RED", "Los Santos ~r~RED ~s~has crashed and needs to be restarted");
                    EntryPoint.WriteToConsole("Error: " + e.Message + " : " + e.StackTrace, 0);
                    Dispose();
                }
            }, "Run Debug Logic");
        }
        private void LoadDefaultMap()
        {
            if(ModDataFileManager.Settings.SettingsManager.WorldSettings.DefaultToMPMap) 
            {
                World.LoadMPMap();
            }
        }
        private void LoadRevealMap()
        {
            if (ModDataFileManager.Settings.SettingsManager.WorldSettings.LoadRevealMap)
            {
                NativeFunction.Natives.SET_MINIMAP_HIDE_FOW(true);
            };
        }
        private void DisplayCrashMessage()
        {
            Game.DisplayNotification("CHAR_BLANK_ENTRY", "CHAR_BLANK_ENTRY", "~o~Error", "Los Santos ~r~RED", "Los Santos ~r~RED ~s~has crashed and needs to be restarted");
        }
        private void DisplayLoadSuccessfulMessage()
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            if (EntryPoint.NotificationID != 0)
            {
                Game.RemoveNotification(EntryPoint.NotificationID);
            }
            Game.DisplayNotification(
                $"~s~Los Santos ~r~RED ~s~v{fvi.FileVersion} " +
                $"~n~By ~g~Greskrendtregk ~s~Has Loaded Successfully.");

            string controlString =
                $"Main Menu (Keyboard): ~{ModDataFileManager.Settings.SettingsManager.KeySettings.MenuKey.GetInstructionalId()}~" +
                $"~n~Action Wheel (Mouse): {FormatKeys(ModDataFileManager.Settings.SettingsManager.KeySettings.ActionPopUpDisplayKeyModifier, ModDataFileManager.Settings.SettingsManager.KeySettings.ActionPopUpDisplayKey)}" +
                $"~n~Action Wheel (Keyboard): {FormatKeys(ModDataFileManager.Settings.SettingsManager.KeySettings.AltActionPopUpDisplayKeyModifier, ModDataFileManager.Settings.SettingsManager.KeySettings.AltActionPopUpDisplayKey)}";
            controlString += $"~n~Action Wheel + Menu (Controller): ";
            if (ModDataFileManager.Settings.SettingsManager.KeySettings.ControllerActionModifier == -1 && ModDataFileManager.Settings.SettingsManager.KeySettings.ControllerAction == 236)
            {
                controlString += FormatButtons(ControllerButtons.None, ControllerButtons.Back);
            }
            else
            {
                controlString += FormatControls(ModDataFileManager.Settings.SettingsManager.KeySettings.ControllerActionModifier, ModDataFileManager.Settings.SettingsManager.KeySettings.ControllerAction);
            }
            Game.DisplayHelp(controlString);
            EntryPoint.WriteToConsole($"Has Loaded Successfully",0);
        }
        public string FormatButtons(ControllerButtons modifier, ControllerButtons key)
        {
            if (modifier != ControllerButtons.None)
            {
                return $"~{modifier.GetInstructionalId()}~ + ~{key.GetInstructionalId()}~";
            }
            else
            {
                return $"~{key.GetInstructionalId()}~";
            }
        }
        public string FormatKeys(Keys modifier, Keys key)
        {
            if(modifier != Keys.None)
            {
                return $"~{modifier.GetInstructionalId()}~ + ~{key.GetInstructionalId()}~";
            }
            else
            {
                return $"~{key.GetInstructionalId()}~";
            }
        }
        public string FormatControls(int modifier, int control)
        {
            if (modifier != -1)
            {
                return $"~{InstructionalButton.GetButtonId((GameControl)modifier)}~ + ~{InstructionalButton.GetButtonId((GameControl)control)}~";
            }
            else
            {
                return $"~{InstructionalButton.GetButtonId((GameControl)control)}~";
            }
        }
        public void AddSpawnError(SpawnError spawnError)
        {
            if(spawnError == null)
            {
                return;
            }

            int Totalvehicles = World.Vehicles.AllVehicleList.Count();
            int TotalPeds = World.Pedestrians.PedExts.Count();
            EntryPoint.WriteToConsole($"ADDED NEW SPAWN ERROR {spawnError.ModelHash} {spawnError.SpawnLocation} {spawnError.GameTimeSpawned} Totalvehicles {Totalvehicles} TotalPeds {TotalPeds}");
            World.SpawnErrors.Add(spawnError);
        }
    }
}

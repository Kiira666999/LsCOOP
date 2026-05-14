using LosSantosRED.lsr.Coop.Core;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Linq;

public static class AmmuNationDoorDiagnostics
{
    private const float NearbyAmmuDistance = 35.0f;
    private const float NearbyLocationDistance = 50.0f;
    private const uint SnapshotInterval = 30000;
    private const uint ActionInterval = 10000;
    private static readonly bool EnableVerboseDiagnostics = false;

    private static readonly List<AmmuDoorDefinition> KnownAmmuDoors = new List<AmmuDoorDefinition>()
    {
        new AmmuDoorDefinition(-555, "Ammunation Vespucci Boulevard", -8873588, new Vector3(842.7685f, -1024.539f, 28.34478f)),
        new AmmuDoorDefinition(-555, "Ammunation Vespucci Boulevard", 97297972, new Vector3(845.3694f, -1024.539f, 28.34478f)),
        new AmmuDoorDefinition(-554, "Ammunation Lindsay Circus", -8873588, new Vector3(-662.6415f, -944.3256f, 21.97915f)),
        new AmmuDoorDefinition(-554, "Ammunation Lindsay Circus", 97297972, new Vector3(-665.2424f, -944.3256f, 21.97915f)),
        new AmmuDoorDefinition(29698, "Ammu Nation Vinewood Plaza", -8873588, new Vector3(243.8379f, -46.52324f, 70.09098f)),
        new AmmuDoorDefinition(29698, "Ammu Nation Vinewood Plaza", 97297972, new Vector3(244.7275f, -44.07911f, 70.09098f)),
    };

    private static readonly Dictionary<string, uint> LastLoggedByKey = new Dictionary<string, uint>();
    private static bool hasLoggedMissingDefinitions;
    private static bool lastKnownVanillaShopControllerActive = true;
    private static bool lastKnownTerminateVanillaShops;
    private static string lastAmmuDoorLockCall = "<none>";
    private static string lastAmmuDoorUnlockCall = "<none>";
    private static readonly HashSet<string> LsrManagedKnownAmmuDoors = new HashSet<string>();

    public static void LogInteriorDoorAction(string phase, InteriorDoor door)
    {
        if (door == null || !IsNearAmmuContext(door.Position) && !IsKnownAmmuDoor(door.ModelHash, door.Position))
        {
            return;
        }

        string doorSummary = string.Format(
            "{0} Hash:{1} Pos:{2} LsrIsLocked:{3}",
            phase,
            door.ModelHash,
            FormatVector(door.Position),
            door.IsLocked);
        LsrManagedKnownAmmuDoors.Add(GetDoorKey(door.ModelHash, door.Position));
        if (phase.IndexOf("UnLockDoor", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            lastAmmuDoorUnlockCall = doorSummary;
        }
        else if (phase.IndexOf("LockDoor", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            lastAmmuDoorLockCall = doorSummary;
        }

        if (!EnableVerboseDiagnostics)
        {
            return;
        }

        string key = "DoorAction:" + phase + ":" + door.ModelHash + ":" + door.Position;
        if (!ShouldLog(key, ActionInterval))
        {
            return;
        }

        AmmuDoorDefinition nearestDoor = GetNearestDoor(GetPlayerPosition());
        string doorObjectState = GetDoorObjectState(door.ModelHash, door.Position);
        Write(
            phase,
            string.Format(
                "InteriorDoor ModelHash:{0} Position:{1} LsrIsLocked:{2} LockWhenClosed:{3} NeedsDefaultUnlock:{4} ForceRotateOpen:{5} DoorObject:{6} NearestKnownDoor:{7}",
                door.ModelHash,
                FormatVector(door.Position),
                door.IsLocked,
                door.LockWhenClosed,
                door.NeedsDefaultUnlock,
                door.ForceRotateOpen,
                doorObjectState,
                FormatDoor(nearestDoor)));
    }

    public static void LogInteriorState(string phase, Interior interior, bool isOpen)
    {
        if (!EnableVerboseDiagnostics)
        {
            return;
        }

        if (interior == null || !IsAmmuInterior(interior) && !IsNearAmmuContext(GetPlayerPosition()))
        {
            return;
        }

        string key = "Interior:" + phase + ":" + interior.LocalID;
        if (!ShouldLog(key, ActionInterval))
        {
            return;
        }

        Write(
            phase,
            string.Format(
                "Interior LocalID:{0} Name:{1} IsOpenArg:{2} DoorCount:{3} GameLocation:{4}",
                interior.LocalID,
                interior.Name,
                isOpen,
                interior.Doors == null ? 0 : interior.Doors.Count,
                interior.GameLocation == null ? "<none>" : FormatLocation(interior.GameLocation, null, null)));
    }

    public static void LogGameLocationState(string phase, GameLocation location, int currentHour, bool isOpen)
    {
        if (!EnableVerboseDiagnostics)
        {
            return;
        }

        if (location == null || !IsNearAmmuLocation(location))
        {
            return;
        }

        string key = "Location:" + phase + ":" + location.Name;
        if (!ShouldLog(key, SnapshotInterval))
        {
            return;
        }

        Write(
            phase,
            string.Format(
                "GameLocation {0}",
                FormatLocation(location, currentHour, isOpen)));
    }

    public static void LogCurrentSnapshot(string phase)
    {
        if (!EnableVerboseDiagnostics)
        {
            return;
        }

        if (!IsNearAmmuContext(GetPlayerPosition()))
        {
            return;
        }

        string key = "Snapshot:" + phase;
        if (!ShouldLog(key, SnapshotInterval))
        {
            return;
        }

        Write(phase, "Snapshot " + BuildSnapshotDetails());
    }

    public static void LogInteriorTracking(string phase, int previousInteriorId, int currentInteriorId, Interior currentInterior)
    {
        if (!EnableVerboseDiagnostics)
        {
            return;
        }

        if (!IsNearAmmuContext(GetPlayerPosition()) && !IsAmmuInterior(currentInterior))
        {
            return;
        }

        string key = "InteriorTracking:" + phase + ":" + previousInteriorId + ":" + currentInteriorId;
        if (!ShouldLog(key, ActionInterval))
        {
            return;
        }

        Write(
            phase,
            string.Format(
                "InteriorTracking PreviousNativeInteriorID:{0} CurrentNativeInteriorID:{1} CurrentInterior:{2}",
                previousInteriorId,
                currentInteriorId,
                currentInterior == null ? "<none>" : currentInterior.LocalID + " " + currentInterior.Name));
    }

    public static void LogVanillaShopControllerState(string phase, bool isActive, bool terminateVanillaShops)
    {
        lastKnownVanillaShopControllerActive = isActive;
        lastKnownTerminateVanillaShops = terminateVanillaShops;

        if (!EnableVerboseDiagnostics)
        {
            return;
        }

        if (!IsNearAmmuContext(GetPlayerPosition()))
        {
            return;
        }

        string key = "VanillaShopController:" + phase + ":" + isActive + ":" + terminateVanillaShops;
        if (!ShouldLog(key, SnapshotInterval))
        {
            return;
        }

        Write(
            phase,
            string.Format(
                "VanillaShopController IsActive:{0} TerminateVanillaShopsSetting:{1}",
                isActive,
                terminateVanillaShops));
    }

    public static bool IsLsrManagingKnownAmmuDoor(long modelHash, Vector3 position)
    {
        return LsrManagedKnownAmmuDoors.Contains(GetDoorKey(modelHash, position));
    }

    private static string BuildSnapshotDetails()
    {
        Ped playerPed = GetPlayerPed();
        string modelName = playerPed != null && playerPed.Exists() ? playerPed.Model.Name : "<missing>";
        ulong modelHash = playerPed != null && playerPed.Exists() ? playerPed.Model.Hash : 0;
        bool isPrimary = IsPrimaryModel(modelName);
        int currentInteriorId = playerPed != null && playerPed.Exists() ? NativeFunction.Natives.GET_INTERIOR_FROM_ENTITY<int>(playerPed) : 0;
        AmmuDoorDefinition nearestDoor = GetNearestDoor(GetPlayerPosition());
        string startupMode = GetCoopStartupModeText();

        return string.Format(
            "PlayerModel:{0} ModelHash:{1} CharacterModelIsPrimaryCharacter:{2} CoopEnabled:{3} StartupMode:{4} CurrentNativeInteriorID:{5} NearestAmmuInterior:{6} KnownDoors:{7} LastLsrAmmuLockCall:{8} LastLsrAmmuUnLockCall:{9} VanillaShopControllerActive:{10} TerminateVanillaShopsSetting:{11}",
            modelName,
            modelHash,
            isPrimary,
            CoopStartupBridge.IsCoopEnabled,
            startupMode,
            currentInteriorId,
            nearestDoor == null ? "<none>" : nearestDoor.LocalInteriorId + " " + nearestDoor.InteriorName,
            FormatNearbyDoors(GetPlayerPosition()),
            lastAmmuDoorLockCall,
            lastAmmuDoorUnlockCall,
            lastKnownVanillaShopControllerActive,
            lastKnownTerminateVanillaShops);
    }

    private static string GetDoorObjectState(long modelHash, Vector3 position)
    {
        try
        {
            Rage.Object doorObject = NativeFunction.Natives.GET_CLOSEST_OBJECT_OF_TYPE<Rage.Object>(position.X, position.Y, position.Z, 3.0f, modelHash, true, false, true);
            if (!doorObject.Exists())
            {
                return "Missing";
            }
            return string.Format("Exists Pos:{0} Heading:{1}", FormatVector(doorObject.Position), doorObject.Heading);
        }
        catch (Exception ex)
        {
            return "Unreadable:" + ex.Message;
        }
    }

    private static string FormatNearbyDoors(Vector3 playerPosition)
    {
        List<string> details = new List<string>();
        foreach (AmmuDoorDefinition door in KnownAmmuDoors.OrderBy(x => x.Position.DistanceTo(playerPosition)).Take(4))
        {
            details.Add(FormatDoor(door));
        }
        return string.Join(" | ", details);
    }

    private static string FormatDoor(AmmuDoorDefinition door)
    {
        if (door == null)
        {
            return "<none>";
        }

        Vector3 playerPosition = GetPlayerPosition();
        return string.Format(
            "{0} LocalID:{1} Hash:{2} Pos:{3} Dist:{4:0.0}",
            door.InteriorName,
            door.LocalInteriorId,
            door.ModelHash,
            FormatVector(door.Position),
            door.Position.DistanceTo(playerPosition));
    }

    private static string FormatLocation(GameLocation location, int? currentHour, bool? isOpen)
    {
        return string.Format(
            "Name:{0} Type:{1} InteriorID:{2} HasInterior:{3} IsActivated:{4} IsEnabled:{5} Distance:{6:0.0} OpenHour:{7} IsOpen:{8} Entrance:{9}",
            location.Name,
            location.TypeName,
            location.InteriorID,
            location.HasInterior,
            location.IsActivated,
            location.IsEnabled,
            location.EntrancePosition.DistanceTo(GetPlayerPosition()),
            currentHour.HasValue ? currentHour.Value.ToString() : "<unknown>",
            isOpen.HasValue ? isOpen.Value.ToString() : "<unknown>",
            FormatVector(location.EntrancePosition));
    }

    private static bool IsNearAmmuLocation(GameLocation location)
    {
        if (location == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(location.Name) && location.Name.IndexOf("ammu", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (location.Interior != null && IsAmmuInterior(location.Interior))
        {
            return true;
        }

        return KnownAmmuDoors.Any(x => x.Position.DistanceTo(location.EntrancePosition) <= NearbyLocationDistance);
    }

    private static bool IsAmmuInterior(Interior interior)
    {
        if (interior == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(interior.Name) && interior.Name.IndexOf("ammu", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return KnownAmmuDoors.Any(x => x.LocalInteriorId == interior.LocalID);
    }

    private static bool IsKnownAmmuDoor(long modelHash, Vector3 position)
    {
        return KnownAmmuDoors.Any(x => x.ModelHash == modelHash && x.Position.DistanceTo(position) <= 0.5f);
    }

    private static string GetDoorKey(long modelHash, Vector3 position)
    {
        AmmuDoorDefinition knownDoor = KnownAmmuDoors.FirstOrDefault(x => x.ModelHash == modelHash && x.Position.DistanceTo(position) <= 0.5f);
        if (knownDoor != null)
        {
            return knownDoor.ModelHash + ":" + knownDoor.Position;
        }

        return modelHash + ":" + position;
    }

    private static bool IsNearAmmuContext(Vector3 position)
    {
        if (position == Vector3.Zero)
        {
            return false;
        }

        return KnownAmmuDoors.Any(x => x.Position.DistanceTo(position) <= NearbyAmmuDistance);
    }

    private static AmmuDoorDefinition GetNearestDoor(Vector3 position)
    {
        if (!KnownAmmuDoors.Any())
        {
            if (!hasLoggedMissingDefinitions)
            {
                EntryPoint.WriteToConsole("AMMU DIAG missing known Ammu door definitions", 0);
                hasLoggedMissingDefinitions = true;
            }
            return null;
        }

        return KnownAmmuDoors.OrderBy(x => x.Position.DistanceTo(position)).FirstOrDefault();
    }

    private static string GetCoopStartupModeText()
    {
        try
        {
            string blockedReason;
            CoopStartupMode startupMode = CoopStartupBridge.GetStartupMode(out blockedReason);
            return string.IsNullOrEmpty(blockedReason) ? startupMode.ToString() : startupMode + " BlockedReason:" + blockedReason;
        }
        catch (Exception ex)
        {
            return "Unreadable:" + ex.Message;
        }
    }

    private static bool IsPrimaryModel(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return false;
        }

        string lower = modelName.ToLower();
        return lower == "player_zero" || lower == "player_one" || lower == "player_two";
    }

    private static bool ShouldLog(string key, uint interval)
    {
        uint now = Game.GameTime;
        uint lastLogged;
        if (LastLoggedByKey.TryGetValue(key, out lastLogged) && now - lastLogged < interval)
        {
            return false;
        }

        LastLoggedByKey[key] = now;
        return true;
    }

    private static Ped GetPlayerPed()
    {
        try
        {
            return Game.LocalPlayer.Character;
        }
        catch
        {
            return null;
        }
    }

    private static Vector3 GetPlayerPosition()
    {
        Ped ped = GetPlayerPed();
        if (ped == null || !ped.Exists())
        {
            return Vector3.Zero;
        }

        return ped.Position;
    }

    private static string FormatVector(Vector3 vector)
    {
        return string.Format("{0:0.00},{1:0.00},{2:0.00}", vector.X, vector.Y, vector.Z);
    }

    private static void Write(string phase, string details)
    {
        EntryPoint.WriteToConsole("AMMU DIAG " + phase + " " + details, 0);
    }

    private sealed class AmmuDoorDefinition
    {
        public AmmuDoorDefinition(int localInteriorId, string interiorName, long modelHash, Vector3 position)
        {
            LocalInteriorId = localInteriorId;
            InteriorName = interiorName;
            ModelHash = modelHash;
            Position = position;
        }

        public int LocalInteriorId { get; private set; }
        public string InteriorName { get; private set; }
        public long ModelHash { get; private set; }
        public Vector3 Position { get; private set; }
    }
}

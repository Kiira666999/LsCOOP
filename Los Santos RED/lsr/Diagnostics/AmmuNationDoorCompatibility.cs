using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Linq;

public static class AmmuNationDoorCompatibility
{
    private const float DoorUnlockDistance = 8.0f;
    private const uint UnlockLogInterval = 10000;

    private static readonly List<AmmuDoorDefinition> KnownAmmuDoors = new List<AmmuDoorDefinition>()
    {
        new AmmuDoorDefinition(-555, "Ammunation Vespucci Boulevard", -8873588, new Vector3(842.7685f, -1024.539f, 28.34478f)),
        new AmmuDoorDefinition(-555, "Ammunation Vespucci Boulevard", 97297972, new Vector3(845.3694f, -1024.539f, 28.34478f)),
        new AmmuDoorDefinition(-554, "Ammunation Lindsay Circus", -8873588, new Vector3(-662.6415f, -944.3256f, 21.97915f)),
        new AmmuDoorDefinition(-554, "Ammunation Lindsay Circus", 97297972, new Vector3(-665.2424f, -944.3256f, 21.97915f)),
        new AmmuDoorDefinition(29698, "Ammu Nation Vinewood Plaza", -8873588, new Vector3(243.8379f, -46.52324f, 70.09098f)),
        new AmmuDoorDefinition(29698, "Ammu Nation Vinewood Plaza", 97297972, new Vector3(244.7275f, -44.07911f, 70.09098f)),
    };

    private static readonly Dictionary<string, uint> LastUnlockLogByDoor = new Dictionary<string, uint>();

    public static void Tick(bool isVanillaShopsActive, bool terminateVanillaShops)
    {
        if (!isVanillaShopsActive || terminateVanillaShops)
        {
            return;
        }

        Ped playerPed = GetPlayerPed();
        if (playerPed == null || !playerPed.Exists())
        {
            return;
        }

        string modelName = playerPed.Model.Name;
        if (IsPrimaryModel(modelName))
        {
            return;
        }

        Vector3 playerPosition = playerPed.Position;
        foreach (AmmuDoorDefinition door in KnownAmmuDoors.Where(x => x.Position.DistanceTo(playerPosition) <= DoorUnlockDistance))
        {
            if (AmmuNationDoorDiagnostics.IsLsrManagingKnownAmmuDoor(door.ModelHash, door.Position))
            {
                continue;
            }

            NativeFunction.Natives.x9B12F9A24FABEDB0(door.ModelHash, door.Position.X, door.Position.Y, door.Position.Z, false, 1.0f);
            LogUnlock(door, modelName, door.Position.DistanceTo(playerPosition));
        }
    }

    private static void LogUnlock(AmmuDoorDefinition door, string modelName, float distance)
    {
        string key = door.ModelHash + ":" + door.Position;
        uint now = Game.GameTime;
        uint lastLogged;
        if (LastUnlockLogByDoor.TryGetValue(key, out lastLogged) && now - lastLogged < UnlockLogInterval)
        {
            return;
        }

        LastUnlockLogByDoor[key] = now;
        EntryPoint.WriteToConsole(
            string.Format(
                "AMMU COMPAT Unlock Location:{0} LocalInteriorID:{1} PlayerModel:{2} DoorHash:{3} Distance:{4:0.0} Reason:non-primary Ammu compatibility",
                door.InteriorName,
                door.LocalInteriorId,
                modelName,
                door.ModelHash,
                distance),
            0);
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

    private static bool IsPrimaryModel(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return false;
        }

        string lower = modelName.ToLower();
        return lower == "player_zero" || lower == "player_one" || lower == "player_two";
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

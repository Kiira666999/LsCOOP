using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopRemoteActorRegistry
    {
        public static CoopRemoteActorRegistry Current { get; } = new CoopRemoteActorRegistry();

        private static readonly TimeSpan ActorTtl = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private readonly Dictionary<string, CoopRemoteActorState> actorsByProfile = new Dictionary<string, CoopRemoteActorState>(StringComparer.OrdinalIgnoreCase);
        private DateTimeOffset lastLogUtc = DateTimeOffset.MinValue;

        public bool UpdateFromBridge(string serializedActors, int localPedHandle, int localVehicleHandle)
        {
            if (!CoopStartupBridge.IsCoopEnabled || !CoopStartupBridge.IsLocalActiveHost)
            {
                return false;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            HashSet<string> seenProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CoopRemoteActorState actor in ParseActors(serializedActors))
            {
                if (!IsUsableActor(actor, localPedHandle, localVehicleHandle))
                {
                    continue;
                }

                actorsByProfile[actor.ProfileId] = actor;
                seenProfiles.Add(actor.ProfileId);
            }

            foreach (string profileId in actorsByProfile.Keys.ToList())
            {
                CoopRemoteActorState actor = actorsByProfile[profileId];
                if (!seenProfiles.Contains(profileId) && now - actor.UpdatedUtc > ActorTtl)
                {
                    actorsByProfile.Remove(profileId);
                }
            }

            if (now - lastLogUtc >= LogInterval)
            {
                lastLogUtc = now;
                EntryPoint.WriteToConsole($"Co-op remote actor registry updated Count:{actorsByProfile.Count}", 0);
            }

            return true;
        }

        public bool TryResolveHandle(int handle, int localPedHandle, int localVehicleHandle, out CoopRemoteActorState actor)
        {
            actor = null;
            if (!CanResolveHandle(handle, localPedHandle, localVehicleHandle))
            {
                return false;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            actor = actorsByProfile.Values
                .Where(x => IsFresh(x, now))
                .FirstOrDefault(x => x.PedHandle == handle || x.VehicleHandle == handle);
            return actor != null;
        }

        public bool TryResolveDamageSource(Ped victimPed, int localPedHandle, int localVehicleHandle, out CoopRemoteActorState actor)
        {
            actor = null;
            if (victimPed == null || !victimPed.Exists())
            {
                return false;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (CoopRemoteActorState candidate in actorsByProfile.Values.Where(x => IsFresh(x, now)))
            {
                if (CanResolveHandle(candidate.PedHandle, localPedHandle, localVehicleHandle)
                    && WasDamagedBy(victimPed, candidate.PedHandle))
                {
                    actor = candidate;
                    return true;
                }

                if (CanResolveHandle(candidate.VehicleHandle, localPedHandle, localVehicleHandle)
                    && WasDamagedBy(victimPed, candidate.VehicleHandle))
                {
                    actor = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool WasDamagedBy(Ped victimPed, int actorHandle)
        {
            try
            {
                return NativeFunction.CallByName<bool>("HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY", victimPed, actorHandle, true);
            }
            catch
            {
                return false;
            }
        }

        private static bool CanResolveHandle(int handle, int localPedHandle, int localVehicleHandle)
        {
            return handle != 0
                && handle != localPedHandle
                && (localVehicleHandle == 0 || handle != localVehicleHandle);
        }

        private static bool IsFresh(CoopRemoteActorState actor, DateTimeOffset now)
        {
            return actor != null
                && !string.IsNullOrWhiteSpace(actor.ProfileId)
                && now - actor.UpdatedUtc <= ActorTtl;
        }

        private static bool IsUsableActor(CoopRemoteActorState actor, int localPedHandle, int localVehicleHandle)
        {
            return actor != null
                && !string.IsNullOrWhiteSpace(actor.ProfileId)
                && CanResolveHandle(actor.PedHandle, localPedHandle, localVehicleHandle);
        }

        private static IEnumerable<CoopRemoteActorState> ParseActors(string serializedActors)
        {
            if (string.IsNullOrWhiteSpace(serializedActors))
            {
                yield break;
            }

            foreach (string rawActor in serializedActors.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = rawActor.Split('|');
                if (parts.Length < 9)
                {
                    continue;
                }

                CoopRemoteActorState actor = new CoopRemoteActorState
                {
                    ProfileId = Unescape(parts[0]),
                    CharacterId = Unescape(parts[1]),
                    RageCoopPlayerId = Unescape(parts[2]),
                    PedHandle = ParseInt(parts[3]),
                    VehicleHandle = ParseInt(parts[4]),
                    Position = new Vector3(ParseFloat(parts[5]), ParseFloat(parts[6]), ParseFloat(parts[7])),
                    UpdatedUtc = ParseDateTimeOffset(parts[8]),
                };

                if (actor.UpdatedUtc == default(DateTimeOffset))
                {
                    actor.UpdatedUtc = DateTimeOffset.UtcNow;
                }

                yield return actor;
            }
        }

        private static string Unescape(string value)
        {
            return Uri.UnescapeDataString(value ?? string.Empty);
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static float ParseFloat(string value)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0.0f;
        }

        private static DateTimeOffset ParseDateTimeOffset(string value)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed)
                ? parsed
                : default(DateTimeOffset);
        }
    }
}

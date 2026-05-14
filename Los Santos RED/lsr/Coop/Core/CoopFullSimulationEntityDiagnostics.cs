using LSR.Vehicles;
using Rage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopFullSimulationEntityDiagnostics
    {
        private const int DurationSeconds = 10;
        private static readonly HashSet<PoolHandle> PreviousVehicleHandles = new HashSet<PoolHandle>();
        private static readonly HashSet<PoolHandle> PreviousPedHandles = new HashSet<PoolHandle>();
        private static readonly HashSet<string> LoggedTaskTicks = new HashSet<string>(StringComparer.Ordinal);
        private static uint gameTimeStarted;
        private static bool isRunning;

        public static void Begin(Mod.World world)
        {
            if (!ShouldLog() || world == null)
            {
                return;
            }

            PreviousVehicleHandles.Clear();
            PreviousPedHandles.Clear();
            LoggedTaskTicks.Clear();
            gameTimeStarted = Game.GameTime;
            isRunning = true;

            EntryPoint.WriteToConsole("Co-op entity diag start Mode:FullSimulation ActiveHost:True", 5);
            GameFiber.StartNew(delegate
            {
                try
                {
                    for (int second = 0; second < DurationSeconds && isRunning && ShouldLog(); second++)
                    {
                        LogSnapshot(world, second);
                        GameFiber.Sleep(1000);
                    }
                }
                catch (Exception ex)
                {
                    EntryPoint.WriteToConsole("Co-op entity diag error " + ex.Message, 5);
                }
                finally
                {
                    isRunning = false;
                }
            }, "Co-op FullSimulation Entity Diagnostics");
        }

        public static void End()
        {
            isRunning = false;
        }

        public static void RecordTaskTick(string taskName)
        {
            if (!isRunning || string.IsNullOrWhiteSpace(taskName) || !ShouldLog() || !LoggedTaskTicks.Add(taskName))
            {
                return;
            }

            int elapsedSeconds = gameTimeStarted == 0 ? 0 : Math.Max(0, (int)((Game.GameTime - gameTimeStarted) / 1000));
            if (elapsedSeconds > DurationSeconds)
            {
                return;
            }

            EntryPoint.WriteToConsole("Co-op entity diag task "
                + "s:" + elapsedSeconds.ToString(CultureInfo.InvariantCulture)
                + " Name:" + taskName, 5);
        }

        private static bool ShouldLog()
        {
            string blockedReason;
            return CoopStartupBridge.IsCoopEnabled
                && CoopStartupBridge.IsLocalActiveHost
                && CoopStartupBridge.GetStartupMode(out blockedReason) == CoopStartupMode.FullSimulation;
        }

        private static void LogSnapshot(Mod.World world, int elapsedSeconds)
        {
            List<Vehicle> vehicles = Rage.World.GetAllVehicles().Where(x => x.Exists()).ToList();
            List<Ped> peds = Rage.World.GetAllPeds().Where(x => x.Exists()).ToList();

            HashSet<PoolHandle> currentVehicleHandles = new HashSet<PoolHandle>(vehicles.Select(x => x.Handle));
            HashSet<PoolHandle> currentPedHandles = new HashSet<PoolHandle>(peds.Select(x => x.Handle));
            List<Vehicle> newVehicles = vehicles.Where(x => !PreviousVehicleHandles.Contains(x.Handle)).ToList();
            int newPedCount = currentPedHandles.Count(x => !PreviousPedHandles.Contains(x));

            EntryPoint.WriteToConsole("Co-op entity diag "
                + "s:" + elapsedSeconds.ToString(CultureInfo.InvariantCulture)
                + " Mode:FullSimulation"
                + " ActiveHost:" + CoopStartupBridge.IsLocalActiveHost.ToString(CultureInfo.InvariantCulture)
                + " RageVeh:" + vehicles.Count.ToString(CultureInfo.InvariantCulture)
                + " RagePeds:" + peds.Count.ToString(CultureInfo.InvariantCulture)
                + " LsrVeh:" + GetLsrVehicleSummary(world)
                + " LsrPeds:" + GetLsrPedSummary(world)
                + " SpawnedEntities:" + EntryPoint.SpawnedEntities.Count(x => x.Exists()).ToString(CultureInfo.InvariantCulture)
                + " NewVeh:" + newVehicles.Count.ToString(CultureInfo.InvariantCulture)
                + " NewPeds:" + newPedCount.ToString(CultureInfo.InvariantCulture)
                + " NewVehTop:" + GetNewVehicleSummary(newVehicles), 5);

            PreviousVehicleHandles.Clear();
            foreach (PoolHandle handle in currentVehicleHandles)
            {
                PreviousVehicleHandles.Add(handle);
            }

            PreviousPedHandles.Clear();
            foreach (PoolHandle handle in currentPedHandles)
            {
                PreviousPedHandles.Add(handle);
            }
        }

        private static string GetLsrVehicleSummary(Mod.World world)
        {
            if (world?.Vehicles == null)
            {
                return "n/a";
            }

            Vehicles vehicles = world.Vehicles;
            int total = CountVehicles(vehicles.AllVehicleList);
            int modSpawned = vehicles.AllVehicleList.Count(x => x != null && x.WasModSpawned && x.Vehicle.Exists());
            return total.ToString(CultureInfo.InvariantCulture)
                + "(mod:" + modSpawned.ToString(CultureInfo.InvariantCulture)
                + ",pol:" + CountVehicles(vehicles.PoliceVehicles).ToString(CultureInfo.InvariantCulture)
                + ",civ:" + CountVehicles(vehicles.CivilianVehicles).ToString(CultureInfo.InvariantCulture)
                + ",taxi:" + CountVehicles(vehicles.TaxiVehicles).ToString(CultureInfo.InvariantCulture)
                + ",gang:" + CountVehicles(vehicles.GangVehicles).ToString(CultureInfo.InvariantCulture)
                + ",svc:" + CountVehicles(vehicles.ServiceVehicles).ToString(CultureInfo.InvariantCulture)
                + ")";
        }

        private static int CountVehicles(IEnumerable<VehicleExt> vehicles)
        {
            return vehicles == null ? 0 : vehicles.Count(x => x != null && x.Vehicle.Exists());
        }

        private static string GetLsrPedSummary(Mod.World world)
        {
            if (world?.Pedestrians == null)
            {
                return "n/a";
            }

            Pedestrians peds = world.Pedestrians;
            int total = CountPeds(peds.PedExts);
            int modSpawned = peds.PedExts.Count(x => x != null && x.WasModSpawned && x.Pedestrian.Exists());
            return total.ToString(CultureInfo.InvariantCulture)
                + "(mod:" + modSpawned.ToString(CultureInfo.InvariantCulture)
                + ",pol:" + CountPeds(peds.PoliceList).ToString(CultureInfo.InvariantCulture)
                + ",civ:" + CountPeds(peds.CivilianList).ToString(CultureInfo.InvariantCulture)
                + ",gang:" + CountPeds(peds.GangMemberList).ToString(CultureInfo.InvariantCulture)
                + ",svc:" + CountPeds(peds.ServiceWorkers).ToString(CultureInfo.InvariantCulture)
                + ")";
        }

        private static int CountPeds(IEnumerable<PedExt> peds)
        {
            return peds == null ? 0 : peds.Count(x => x != null && x.Pedestrian.Exists());
        }

        private static string GetNewVehicleSummary(List<Vehicle> newVehicles)
        {
            if (newVehicles == null || !newVehicles.Any())
            {
                return "none";
            }

            return string.Join("|", newVehicles.Take(3).Select(x => x.Model.Name + "@"
                + x.Position.X.ToString("0", CultureInfo.InvariantCulture) + ","
                + x.Position.Y.ToString("0", CultureInfo.InvariantCulture) + ","
                + x.Position.Z.ToString("0", CultureInfo.InvariantCulture)));
        }
    }
}

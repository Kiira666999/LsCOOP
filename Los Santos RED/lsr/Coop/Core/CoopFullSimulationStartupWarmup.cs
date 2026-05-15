using Rage;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopFullSimulationStartupWarmup
    {
        private const int WarmupMilliseconds = 15000;
        private const int StaggerMilliseconds = 15000;
        private const int WarmupEntityTaskIntervalMilliseconds = 2000;
        private const int StaggerLocationTaskIntervalMilliseconds = 3000;
        private static readonly Dictionary<string, uint> LastAllowedTaskRun = new Dictionary<string, uint>(StringComparer.Ordinal);
        private static readonly Dictionary<string, uint> NextDelayLog = new Dictionary<string, uint>(StringComparer.Ordinal);
        private static uint gameTimeStarted;
        private static bool isRunning;
        private static bool loggedWarmupEnd;
        private static bool loggedStaggerEnd;
        private static bool loggedFirstLocationDispatch;
        private static bool loggedFirstVendorDispatch;

        public static bool IsActive => isRunning && ShouldApply();
        public static bool IsInWarmup => IsActive && ElapsedMilliseconds < WarmupMilliseconds;
        public static bool IsInStagger => IsActive && ElapsedMilliseconds >= WarmupMilliseconds && ElapsedMilliseconds < WarmupMilliseconds + StaggerMilliseconds;
        public static int ElapsedMilliseconds => gameTimeStarted == 0 ? 0 : Math.Max(0, (int)(Game.GameTime - gameTimeStarted));
        public static int WarmupRemainingSeconds => Math.Max(0, (int)Math.Ceiling((WarmupMilliseconds - ElapsedMilliseconds) / 1000.0));

        public static void Begin()
        {
            if (!ShouldApply())
            {
                return;
            }

            gameTimeStarted = Game.GameTime;
            isRunning = true;
            loggedWarmupEnd = false;
            loggedStaggerEnd = false;
            loggedFirstLocationDispatch = false;
            loggedFirstVendorDispatch = false;
            LastAllowedTaskRun.Clear();
            NextDelayLog.Clear();

            GameFiber.StartNew(delegate
            {
                try
                {
                    while (isRunning && ShouldApply() && !loggedStaggerEnd)
                    {
                        LogLifecycle();
                        GameFiber.Sleep(1000);
                    }
                }
                catch (Exception ex)
                {
                    EntryPoint.WriteToConsole("Co-op FullSimulation spawn warmup log error " + ex.Message, 5);
                }
            }, "Co-op FullSimulation Spawn Warmup");
        }

        public static void End()
        {
            isRunning = false;
            LastAllowedTaskRun.Clear();
            NextDelayLog.Clear();
        }

        public static bool ShouldRunEntityRegistrationTask(string taskName)
        {
            if (!IsInWarmup)
            {
                return true;
            }

            return ShouldRunThrottled(taskName, WarmupEntityTaskIntervalMilliseconds, "warmup throttle");
        }

        public static bool ShouldRunLocationActivationTask()
        {
            const string taskName = "World.ActiveNearLocations";
            if (IsInWarmup)
            {
                LogDelayed(taskName, "warmup");
                return false;
            }

            if (IsInStagger)
            {
                return ShouldRunThrottled(taskName, StaggerLocationTaskIntervalMilliseconds, "stagger throttle");
            }

            return true;
        }

        public static bool ShouldRunDispatcherTask()
        {
            const string taskName = "Dispatcher.Dispatch";
            if (IsInWarmup)
            {
                LogDelayed(taskName, "warmup");
                return false;
            }

            return true;
        }

        public static int LocationDispatchLimit => IsInStagger ? 1 : int.MaxValue;

        public static bool ShouldDelayLocationDispatch()
        {
            if (!IsInWarmup)
            {
                return false;
            }

            LogDelayed("LocationDispatcher.Dispatch", "warmup");
            return true;
        }

        public static void OnLocationDispatchAllowed(string locationName)
        {
            if (!IsActive || loggedFirstLocationDispatch)
            {
                return;
            }

            loggedFirstLocationDispatch = true;
        }

        public static void OnVendorDispatchAllowed(string locationName)
        {
            if (!IsActive || loggedFirstVendorDispatch)
            {
                return;
            }

            loggedFirstVendorDispatch = true;
        }

        public static void LogLocationDispatchDeferred(int remaining)
        {
            if (remaining <= 0 || !IsInStagger)
            {
                return;
            }

            LogDelayed("LocationDispatcher.Dispatch", "stagger deferred " + remaining.ToString(CultureInfo.InvariantCulture) + " locations");
        }

        private static bool ShouldRunThrottled(string taskName, int intervalMilliseconds, string reason)
        {
            uint lastRun;
            if (LastAllowedTaskRun.TryGetValue(taskName, out lastRun) && Game.GameTime - lastRun < intervalMilliseconds)
            {
                LogDelayed(taskName, reason);
                return false;
            }

            LastAllowedTaskRun[taskName] = Game.GameTime;
            return true;
        }

        private static void LogLifecycle()
        {
            if (IsInWarmup)
            {
                return;
            }

            if (!loggedWarmupEnd && IsInStagger)
            {
                loggedWarmupEnd = true;
                return;
            }

            if (!loggedStaggerEnd && IsActive && ElapsedMilliseconds >= WarmupMilliseconds + StaggerMilliseconds)
            {
                loggedStaggerEnd = true;
            }
        }

        private static void LogDelayed(string taskName, string reason)
        {
            uint nextLog;
            if (NextDelayLog.TryGetValue(taskName, out nextLog) && Game.GameTime < nextLog)
            {
                return;
            }

            NextDelayLog[taskName] = Game.GameTime + 5000;
        }

        private static bool ShouldApply()
        {
            string blockedReason;
            return CoopStartupBridge.IsCoopEnabled
                && CoopStartupBridge.IsLocalActiveHost
                && CoopStartupBridge.GetStartupMode(out blockedReason) == CoopStartupMode.FullSimulation;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }
    }
}

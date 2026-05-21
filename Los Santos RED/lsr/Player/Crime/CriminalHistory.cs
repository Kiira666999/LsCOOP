using LosSantosRED.lsr;
using LosSantosRED.lsr.Coop.Core;
using LosSantosRED.lsr.Interface;
using LSR.Vehicles;
using Rage;
using Rage.Native;
using RAGENativeUI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LosSantosRED.lsr
{
    public class CriminalHistory
    {
        private BOLO CurrentHistory;
        private BOLO ReactivatedPersistentApbHistory;
        private IPoliceRespondable Player;
        private ISettingsProvideable Settings;
        private ITimeReportable Time;
        private Blip CriminalHistoryBlip;
        private string NextCoopClearReason;
        private float PlayerDistanceToLastSeen = 9999f;
        private bool HasLoggedApbExpirationExtended;
        private bool HasLoggedApbExpirationSkipped;
        private Color blipColor => IsNearLastSeenLocation ? Color.Orange : Color.Yellow;
        public bool IsNearLastSeenLocation { get; set; }
        public bool IsWithinMarshalDistance => HasHistory && PlayerDistanceToLastSeen <= SearchRadius + Settings.SettingsManager.PoliceSettings.MarshalsAPBResponseExtraRadiusDistance;
        public CriminalHistory(IPoliceRespondable currentPlayer, ISettingsProvideable settings, ITimeReportable time)
        {
            Player = currentPlayer;
            Settings = settings;
            Time = time;
        }
        private CriminalHistorySettings CriminalHistorySettings => Settings?.SettingsManager?.CriminalHistorySettings;
        private ApbPersistenceMode CurrentApbPersistenceMode => CriminalHistorySettings?.ApbPersistenceMode ?? ApbPersistenceMode.Normal;
        private int LastWantedMaxLevel => CurrentHistory == null ? 0 : CurrentHistory.WantedLevel;
        private float SearchRadius => LastWantedMaxLevel > 0 ? LastWantedMaxLevel * Settings.SettingsManager.CriminalHistorySettings.SearchRadiusIncrement : Settings.SettingsManager.CriminalHistorySettings.MinimumSearchRadius;// 400f;
        public bool HasHistory => CurrentHistory != null;
        public bool HasDeadlyHistory => IsApb(CurrentHistory);
        public int MaxWantedLevel => LastWantedMaxLevel;
        public List<Crime> WantedCrimes => CurrentHistory?.Crimes.Select(x => x.AssociatedCrime).ToList();
        public void Dispose()
        {
            if (CriminalHistoryBlip.Exists())
            {
                CriminalHistoryBlip.Delete();
            }
        }
        public void OnSuspectEluded(List<CrimeEvent> CrimesAssociated,Vector3 PlaceLastSeen)
        {
            if (CrimesAssociated != null && PlaceLastSeen != Vector3.Zero && Player.PoliceResponse.HasPlayerBeenIdentified )
            {
                CurrentHistory = new BOLO(PlaceLastSeen,  CrimesAssociated, Player.WantedLevel);
                ReactivatedPersistentApbHistory = null;
                ResetApbPersistenceDiagnostics();
                LogApbPersistence($"created Source:Eluded IsAPB:{IsApb(CurrentHistory)} Mode:{CurrentApbPersistenceMode} DeadlyCrimes:{DeadlyCrimeNames(CurrentHistory)} Wanted:{CurrentHistory.WantedLevel}");
                CoopCriminalJusticeStateAdapter.Current.NotifyLocalCriminalHistoryChanged();
            }
        }
        public void OnLostWanted()
        {
            //clear criminal history?
        }
        public void Update()
        {
            UpdateData();
            //GameFiber.Yield();//TR 05
            UpdateBlip();
        }
        public void Reset()
        {
            Clear(NextCoopClearReason);
        }
        public void SetNextCoopClearReason(string clearReason)
        {
            NextCoopClearReason = clearReason ?? string.Empty;
        }
        public void Clear()
        {
            Clear(null);
        }
        public void Clear(string clearReason)
        {
            string appliedClearReason = clearReason ?? NextCoopClearReason ?? string.Empty;
            if (ShouldPreservePersistentApbOnClear(appliedClearReason))
            {
                RestoreReactivatedPersistentApbIfNeeded();
                NextCoopClearReason = string.Empty;
                LogApbPersistence($"clear skipped Reason:{appliedClearReason} Mode:{CurrentApbPersistenceMode} DeadlyCrimes:{DeadlyCrimeNames(CurrentHistory)}");
                CoopCriminalJusticeStateAdapter.Current.NotifyLocalCriminalHistoryChanged();
                return;
            }

            if (HasPersistentApbToProtect)
            {
                LogApbPersistence($"clear allowed Reason:{appliedClearReason} Mode:{CurrentApbPersistenceMode} DeadlyCrimes:{DeadlyCrimeNames(CurrentHistory ?? ReactivatedPersistentApbHistory)}");
            }

            CurrentHistory = null;
            ReactivatedPersistentApbHistory = null;
            ResetApbPersistenceDiagnostics();
            NextCoopClearReason = appliedClearReason;
            CoopCriminalJusticeStateAdapter.Current.NotifyLocalCriminalHistoryChanged();
            NextCoopClearReason = string.Empty;
            EntryPoint.WriteToConsole($" PLAYER EVENT: Criminal History Clear Reason:{appliedClearReason}", 5);
        }
        public void ResolvePersistentApb(string clearReason)
        {
            if (!HasPersistentApbToProtect)
            {
                return;
            }

            Clear(clearReason);
        }
        public void AddCrime(Crime crime)
        {
            Player.PoliceResponse.OnLostWanted();
            if (CurrentHistory == null)
            {
                CurrentHistory = new BOLO(Vector3.Zero,new List<CrimeEvent>() { new CrimeEvent(crime,null) }, crime.ResultingWantedLevel);
                ResetApbPersistenceDiagnostics();
            }
            else
            {
                if (!CurrentHistory.Crimes.Any(x => x.AssociatedCrime != null && x.AssociatedCrime.Name == crime.Name))
                {
                    CurrentHistory.Crimes.Add(new CrimeEvent(crime, null));
                    ResetApbPersistenceDiagnostics();
                }
            }
            CoopCriminalJusticeStateAdapter.Current.NotifyLocalCriminalHistoryChanged();
        }
        public CoopCriminalHistoryState CreateCoopState(CoopWorldId worldId, CoopProfileId profileId, CoopCharacterId characterId)
        {
            BOLO historyForState = CurrentHistory ?? ReactivatedPersistentApbHistory;
            CoopCriminalHistoryState state = new CoopCriminalHistoryState
            {
                WorldId = worldId,
                ProfileId = profileId,
                CharacterId = characterId,
                HasHistory = historyForState != null,
                WantedLevel = historyForState?.WantedLevel ?? 0,
                LastSeenX = historyForState?.LastSeenLocation.X ?? 0.0f,
                LastSeenY = historyForState?.LastSeenLocation.Y ?? 0.0f,
                LastSeenZ = historyForState?.LastSeenLocation.Z ?? 0.0f,
                DateTimeLastWantedEnded = ToCoopDateTimeOffset(GetCoopWantedEndedAnchor(historyForState)),
                UpdatedUtc = DateTimeOffset.UtcNow,
                ClearReason = historyForState == null ? NextCoopClearReason ?? string.Empty : string.Empty,
            };

            if (historyForState?.Crimes != null)
            {
                foreach (CrimeEvent crimeEvent in historyForState.Crimes.Where(x => x?.AssociatedCrime != null))
                {
                    state.Crimes.Add(new CoopCriminalHistoryCrimeRecord
                    {
                        CrimeId = crimeEvent.AssociatedCrime.ID,
                        CrimeName = crimeEvent.AssociatedCrime.Name,
                        Instances = crimeEvent.Instances,
                        ResultingWantedLevel = crimeEvent.AssociatedCrime.ResultingWantedLevel,
                        Priority = crimeEvent.AssociatedCrime.Priority,
                        ResultsInLethalForce = crimeEvent.AssociatedCrime.ResultsInLethalForce,
                    });
                }
            }

            LogCoopExpirationDiagnostics("Capture", state.WantedLevel, ToDateTime(state.DateTimeLastWantedEnded), string.Empty);
            return state;
        }
        public void ApplyCoopState(CoopCriminalHistoryState state, ICrimes crimes)
        {
            if (state == null || !state.HasHistory)
            {
                CurrentHistory = null;
                ReactivatedPersistentApbHistory = null;
                ResetApbPersistenceDiagnostics();
                return;
            }

            List<CrimeEvent> restoredCrimes = new List<CrimeEvent>();
            foreach (CoopCriminalHistoryCrimeRecord crimeRecord in state.Crimes)
            {
                Crime crime = crimes?.GetCrime(crimeRecord.CrimeId);
                if (crime == null)
                {
                    continue;
                }

                restoredCrimes.Add(new CrimeEvent(crime, null) { Instances = crimeRecord.Instances });
            }

            DateTime restoredWantedEnded = ToDateTime(state.DateTimeLastWantedEnded);
            Player.PoliceResponse.RestoreCoopCriminalHistoryWantedEndedAnchor(restoredWantedEnded);
            CurrentHistory = new BOLO(new Vector3(state.LastSeenX, state.LastSeenY, state.LastSeenZ), restoredCrimes, state.WantedLevel);
            ReactivatedPersistentApbHistory = null;
            ResetApbPersistenceDiagnostics();
            LogCoopExpirationDiagnostics("Hydrate", state.WantedLevel, restoredWantedEnded, string.Empty);
        }
        public string PrintCriminalHistory()
        {
            if(CurrentHistory != null)
            {
                string CrimeString = "";
                foreach (CrimeEvent MyCrime in CurrentHistory.Crimes.Where(x=> x.AssociatedCrime != null).OrderBy(x => x.AssociatedCrime.Priority).Take(3))
                {
                    CrimeString += string.Format("~n~{0}~s~", MyCrime.AssociatedCrime.Name);
                }
                return CrimeString;
            }
            return "";
        }
        private void UpdateData()
        {
            if (!Player.IsAliveAndFree || !HasHistory)
            {
                PlayerDistanceToLastSeen = 9999f;
                IsNearLastSeenLocation = false;
                return;
            }
            IsNearLastSeenLocation = UpdateLastSeenDistance();
            if (Player.AnyPoliceCanRecognizePlayer)
            {
                if (Player.IsWanted)
                {
                    ApplyLastWantedStats();
                    GameFiber.Yield();//TR 05
                    //EntryPoint.WriteToConsole("CRIMINAL HISTORY EVENT: Became Wanted");
                }
                else if (IsNearLastSeenLocation && Player.PoliceResponse.HasBeenNotWantedFor >= 5000)//move the second one OUT
                {
                    ApplyLastWantedStats();
                    GameFiber.Yield();//TR 05
                    //EntryPoint.WriteToConsole("CRIMINAL HISTORY EVENT: Near Last Location");
                }
                else if (Player.IsInVehicle && Player.CurrentVehicle != null && Player.CurrentVehicle.IsWanted)//.CopsRecognizeAsStolen)
                {
                    ApplyLastWantedStats();
                    GameFiber.Yield();//TR 05
                    //EntryPoint.WriteToConsole("CRIMINAL HISTORY EVENT: Recognized Vehicle");
                }
            }
            double expirationMultiplier = GetApbExpirationMultiplier(CurrentHistory);
            if (IsPersistentApb(CurrentHistory))
            {
                LogApbExpirationSkippedOnce();
            }
            else
            {
                if (Player.PoliceResponse.HasBeenNotWantedFor >= (Settings.SettingsManager.CriminalHistorySettings.RealTimeExpireWantedMultiplier * LastWantedMaxLevel * expirationMultiplier))// 120000)
                {
                    Clear("ExpiredRealTime");
                    return;
                    //EntryPoint.WriteToConsole("CRIMINAL HISTORY EVENT: History Expired (Real Time)");
                }

                LogApbExpirationExtendedOnce(expirationMultiplier);
                DateTime expirationTime = Player.PoliceResponse.DateTimeLastWantedEnded.AddHours(LastWantedMaxLevel * Settings.SettingsManager.CriminalHistorySettings.CalendarTimeExpireWantedMultiplier * expirationMultiplier);
                if (DateTime.Compare(expirationTime, Time.CurrentDateTime) < 0)
                {
                    //EntryPoint.WriteToConsole($"POLICE RESPONSE: Lost Wanted ToExpire: {Player.PoliceResponse.DateTimeLastWantedEnded.AddHours(LastWantedMaxLevel * Settings.SettingsManager.CriminalHistorySettings.CalendarTimeExpireWantedMultiplier)} Current: {Time.CurrentDateTime}");
                    LogCoopExpirationDiagnostics("Clear", LastWantedMaxLevel, Player.PoliceResponse.DateTimeLastWantedEnded, "ExpiredCalendarTime");
                    Clear("ExpiredCalendarTime");
                    return;
                    //EntryPoint.WriteToConsole("CRIMINAL HISTORY EVENT: History Expired (Calendar Time)");
                }
            }

            if(Player.IsWanted && Player.PoliceResponse.WantedLevelHasBeenRadioedIn && HasHistory)
            {
                if (IsPersistentApb(CurrentHistory))
                {
                    ReactivatedPersistentApbHistory = CloneHistory(CurrentHistory);
                    LogApbPersistence($"reactivated into active wanted Mode:{CurrentApbPersistenceMode} DeadlyCrimes:{DeadlyCrimeNames(CurrentHistory)} Wanted:{CurrentHistory.WantedLevel}");
                }
                CurrentHistory = null;
            }
        }
        private bool UpdateLastSeenDistance()
        {
            if(CurrentHistory == null)
            {
                PlayerDistanceToLastSeen = 9999f;
                return false;
            }
            PlayerDistanceToLastSeen = Player.Position.DistanceTo2D(CurrentHistory.LastSeenLocation);
            if (PlayerDistanceToLastSeen <= SearchRadius)
            {
                return true;
            }
            return false;
        }
        private void ApplyLastWantedStats()
        {
            if(CurrentHistory == null)
            {
                return;
            }
            foreach(CrimeEvent crime in CurrentHistory.Crimes)
            {
                //EntryPoint.WriteToConsole($"PLAYER EVENT: APPLYING WANTED STATS: ADDING CRIME: {crime.Name}");
                Player.AddCrime(crime.AssociatedCrime, true, Player.Position, Player.CurrentSeenVehicle, Player.WeaponEquipment.CurrentSeenWeapon, true,false, true, false);
                CrimeEvent addedCrime = Player.PoliceResponse.CrimesObserved.Where(x => x.AssociatedCrime?.ID == crime.AssociatedCrime.ID).FirstOrDefault();
                if(addedCrime != null)
                {
                    addedCrime.Instances = crime.Instances;
                }
            }
            int highestWantedLevel = CurrentHistory.WantedLevel;
            //CurrentHistory = null;
            Player.OnAppliedWantedStats(highestWantedLevel);        
        }
        private void UpdateBlip()
        {
            if (HasHistory && Player.IsNotWanted && Settings.SettingsManager.CriminalHistorySettings.CreateBlip)
            {
                CreateBlip();
            }
            else
            {
                RemoveBlip();
            }
        }
        private void CreateBlip()
        {
            if (!CriminalHistoryBlip.Exists())
            {
                CriminalHistoryBlip = new Blip(CurrentHistory.LastSeenLocation, SearchRadius)
                {
                    Name = "APB Center",
                    Color = blipColor,//Color.Yellow,
                    Alpha = 0.25f
                };
                NativeFunction.Natives.BEGIN_TEXT_COMMAND_SET_BLIP_NAME("STRING");
                NativeFunction.Natives.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME("APB Center");
                NativeFunction.Natives.END_TEXT_COMMAND_SET_BLIP_NAME(CriminalHistoryBlip);
                NativeFunction.Natives.SET_BLIP_AS_SHORT_RANGE((uint)CriminalHistoryBlip.Handle, true);

                EntryPoint.WriteToConsole($"CRIMINAL HISORY BLIP CREATED");
                //GameFiber.Yield();//TR Yield RemovedTest 1
            }
            else
            {
                CriminalHistoryBlip.Position = CurrentHistory.LastSeenLocation;
                CriminalHistoryBlip.Color = blipColor;
            }
        }
        private void RemoveBlip()
        {
            if (CriminalHistoryBlip.Exists())
            {
                CriminalHistoryBlip.Delete();
            }
        }

        private DateTimeOffset ToCoopDateTimeOffset(DateTime value)
        {
            if (value == DateTime.MinValue)
            {
                return DateTimeOffset.MinValue;
            }

            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        private DateTime GetCoopWantedEndedAnchor(BOLO historyForState)
        {
            DateTime anchor = Player.PoliceResponse.DateTimeLastWantedEnded;
            return historyForState != null && anchor == DateTime.MinValue ? Time.CurrentDateTime : anchor;
        }

        private DateTime ToDateTime(DateTimeOffset value)
        {
            if (value == DateTimeOffset.MinValue)
            {
                return DateTime.MinValue;
            }

            return DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);
        }

        private void LogCoopExpirationDiagnostics(string stage, int wantedLevel, DateTime dateTimeLastWantedEnded, string clearReason)
        {
            if (!CoopStartupBridge.IsCoopEnabled
                || Settings?.SettingsManager?.DebugSettings?.LogCoopPersistenceDiagnostics != true)
            {
                return;
            }

            DateTime expirationTime = dateTimeLastWantedEnded == DateTime.MinValue
                ? DateTime.MinValue
                : dateTimeLastWantedEnded.AddHours(wantedLevel * Settings.SettingsManager.CriminalHistorySettings.CalendarTimeExpireWantedMultiplier);
            double remainingHours = expirationTime == DateTime.MinValue ? 0.0d : (expirationTime - Time.CurrentDateTime).TotalHours;
            EntryPoint.WriteToConsole($"Co-op criminal history expiration {stage} Wanted:{wantedLevel} DateTimeLastWantedEnded:{dateTimeLastWantedEnded:O} Expires:{expirationTime:O} Current:{Time.CurrentDateTime:O} RemainingHours:{remainingHours:0.00} ClearReason:{clearReason}", 5);
        }

        private bool IsApb(BOLO history)
        {
            return history?.Crimes != null && history.Crimes.Any(x => x?.AssociatedCrime?.ResultsInLethalForce == true);
        }

        private bool IsRecognizedApb(BOLO history)
        {
            return IsApb(history) && history.LastSeenLocation != Vector3.Zero;
        }

        private bool IsPersistentApb(BOLO history)
        {
            return CurrentApbPersistenceMode == ApbPersistenceMode.UntilResolved && IsRecognizedApb(history);
        }

        private bool HasPersistentApbToProtect => IsPersistentApb(CurrentHistory) || IsPersistentApb(ReactivatedPersistentApbHistory);

        private bool ShouldPreservePersistentApbOnClear(string clearReason)
        {
            return HasPersistentApbToProtect && !IsPersistentApbClearAllowed(clearReason);
        }

        private bool IsPersistentApbClearAllowed(string clearReason)
        {
            if (string.IsNullOrWhiteSpace(clearReason))
            {
                return true;
            }

            return string.Equals(clearReason, "ArrestResolved", StringComparison.OrdinalIgnoreCase)
                || string.Equals(clearReason, "DeathResolved", StringComparison.OrdinalIgnoreCase)
                || string.Equals(clearReason, "IdentityChanged", StringComparison.OrdinalIgnoreCase)
                || string.Equals(clearReason, "ForgerIdentityChanged", StringComparison.OrdinalIgnoreCase)
                || string.Equals(clearReason, "AdminReset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(clearReason, "DebugReset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(clearReason, "NewPlayerReset", StringComparison.OrdinalIgnoreCase);
        }

        private double GetApbExpirationMultiplier(BOLO history)
        {
            if (CurrentApbPersistenceMode != ApbPersistenceMode.Extended || !IsRecognizedApb(history))
            {
                return 1.0d;
            }

            float configuredMultiplier = CriminalHistorySettings?.ExtendedApbExpirationMultiplier ?? 1.0f;
            return Math.Max(1.0d, configuredMultiplier);
        }

        private void RestoreReactivatedPersistentApbIfNeeded()
        {
            if (CurrentHistory == null && ReactivatedPersistentApbHistory != null)
            {
                CurrentHistory = CloneHistory(ReactivatedPersistentApbHistory);
                ReactivatedPersistentApbHistory = null;
                ResetApbPersistenceDiagnostics();
            }
        }

        private BOLO CloneHistory(BOLO history)
        {
            if (history == null)
            {
                return null;
            }

            List<CrimeEvent> crimes = history.Crimes?
                .Where(x => x?.AssociatedCrime != null)
                .Select(x => new CrimeEvent(x.AssociatedCrime, x.CurrentInformation) { Instances = x.Instances })
                .ToList() ?? new List<CrimeEvent>();
            return new BOLO(history.LastSeenLocation, crimes, history.WantedLevel);
        }

        private void ResetApbPersistenceDiagnostics()
        {
            HasLoggedApbExpirationExtended = false;
            HasLoggedApbExpirationSkipped = false;
        }

        private void LogApbExpirationSkippedOnce()
        {
            if (HasLoggedApbExpirationSkipped)
            {
                return;
            }

            HasLoggedApbExpirationSkipped = true;
            LogApbPersistence($"expiry skipped Mode:{CurrentApbPersistenceMode} DeadlyCrimes:{DeadlyCrimeNames(CurrentHistory)} Wanted:{LastWantedMaxLevel}");
        }

        private void LogApbExpirationExtendedOnce(double expirationMultiplier)
        {
            if (HasLoggedApbExpirationExtended || expirationMultiplier <= 1.0d || !IsRecognizedApb(CurrentHistory))
            {
                return;
            }

            HasLoggedApbExpirationExtended = true;
            LogApbPersistence($"expiry extended Mode:{CurrentApbPersistenceMode} Multiplier:{expirationMultiplier:0.##} DeadlyCrimes:{DeadlyCrimeNames(CurrentHistory)} Wanted:{LastWantedMaxLevel}");
        }

        private string DeadlyCrimeNames(BOLO history)
        {
            if (history?.Crimes == null)
            {
                return string.Empty;
            }

            return string.Join(",", history.Crimes
                .Where(x => x?.AssociatedCrime?.ResultsInLethalForce == true)
                .Select(x => x.AssociatedCrime.ID)
                .Distinct());
        }

        private void LogApbPersistence(string message)
        {
            EntryPoint.WriteToConsole($"APB persistence {message}", 3);
        }

    }


}

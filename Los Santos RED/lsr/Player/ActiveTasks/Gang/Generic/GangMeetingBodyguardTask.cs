using ExtensionsMethods;
using LosSantosRED.lsr.Interface;
using Rage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LosSantosRED.lsr.Player.ActiveTasks
{
    public class GangMeetingBodyguardTask : GangTask, IPlayerTask
    {
        private const uint TransactionDurationMs = 20000;

        private GangDen HiringGangDen;
        private Gang MeetingGang;
        private GameLocation MeetingLocation;
        private bool HasArrivedNearMeetup;
        private bool HasStartedTransaction;
        private bool HasSetViolent;
        private bool HasFailedMeeting;
        private bool IsAmbush;
        private uint TransactionStartGameTime;
        private readonly List<GangMember> HiringMembers = new List<GangMember>();
        private readonly List<GangMember> MeetingMembers = new List<GangMember>();
        private GangMember PrimaryMeetingMember;

        public GangMeetingBodyguardTask(ITaskAssignable player, ITimeReportable time, IGangs gangs, IPlacesOfInterest placesOfInterest, ISettingsProvideable settings, IEntityProvideable world,
            ICrimes crimes, IWeapons weapons, INameProvideable names, IPedGroups pedGroups, IShopMenus shopMenus, IModItems modItems, PlayerTasks playerTasks, GangTasks gangTasks,
            PhoneContact hiringContact, Gang hiringGang, Gang meetingGang, GameLocation meetingLocation) : base(player, time, gangs, placesOfInterest, settings, world, crimes, weapons, names, pedGroups, shopMenus, modItems, playerTasks, gangTasks, hiringContact, hiringGang)
        {
            DebugName = "Meeting Bodyguard";
            RepOnCompletion = 1000;
            DebtOnFail = 0;
            RepOnFail = -500;
            DaysToComplete = 2;
            MeetingGang = meetingGang;
            MeetingLocation = meetingLocation;
        }

        public override void Dispose()
        {
            CleanupPeds();
            if (MeetingLocation != null)
            {
                MeetingLocation.IsPlayerInterestedInLocation = false;
            }
            base.Dispose();
        }

        protected override bool GetTaskData()
        {
            HiringGangDen = PlacesOfInterest.GetMainDen(HiringGang.ID, World.IsMPMapLoaded, Player.CurrentLocation);
            if (HiringGangDen == null)
            {
                return false;
            }
            if (MeetingGang == null)
            {
                MeetingGang = GetMeetingGang();
            }
            if (MeetingGang == null || MeetingGang.ID == HiringGang.ID)
            {
                return false;
            }
            if (MeetingLocation == null)
            {
                MeetingLocation = GetMeetingLocation();
            }
            if (MeetingLocation == null)
            {
                return false;
            }
            GangReputation reputation = Player.RelationshipManager.GangRelationships.GetReputation(MeetingGang);
            if (reputation == null || reputation.IsMember)
            {
                return false;
            }
            if (reputation.IsEnemy || reputation.GangRelationship == GangRespect.Hostile)
            {
                IsAmbush = true;
            }
            else if (reputation.GangRelationship == GangRespect.Neutral)
            {
                IsAmbush = RandomItems.RandomPercent(Settings.SettingsManager.TaskSettings.DrugMeetAmbushPercentageNeutral);
            }
            else if (reputation.GangRelationship == GangRespect.Friendly)
            {
                IsAmbush = RandomItems.RandomPercent(Settings.SettingsManager.TaskSettings.DrugMeetAmbushPercentageFriendly);
            }
            return true;
        }

        protected override void GetPayment()
        {
            if (HiringGang.MeetingBodyguardPaymentMax > HiringGang.MeetingBodyguardPaymentMin)
            {
                PaymentAmount = RandomItems.GetRandomNumberInt(HiringGang.MeetingBodyguardPaymentMin, HiringGang.MeetingBodyguardPaymentMax).Round(500);
            }
            else
            {
                PaymentAmount = HiringGang.MeetingBodyguardPaymentMin.Round(500);
            }
            if (PaymentAmount <= 0)
            {
                PaymentAmount = 500;
            }
        }

        protected override void SendInitialInstructionsMessage()
        {
            List<string> replies = new List<string>()
            {
                $"We have a sit-down with {MeetingGang.ColorPrefix}{MeetingGang.ShortName}~s~ around {MeetingLocation.FullStreetAddress}. Go stand in, keep it calm, and come back to {HiringGang.DenName} on {HiringGangDen.FullStreetAddress}. ${PaymentAmount} when it is done.",
                $"Need you watching a meet with {MeetingGang.ColorPrefix}{MeetingGang.ShortName}~s~ near {MeetingLocation.FullStreetAddress}. Stay close until business is finished, then return to {HiringGang.DenName}. ${PaymentAmount}.",
                $"A meet is set with {MeetingGang.ColorPrefix}{MeetingGang.ShortName}~s~ at {MeetingLocation.FullStreetAddress}. Be there, make sure our people walk away, then come see me at {HiringGangDen.FullStreetAddress} for ${PaymentAmount}."
            };
            Player.CellPhone.AddPhoneResponse(HiringGang.Contact.Name, HiringGang.Contact.IconName, replies.PickRandom());
        }

        protected override void AddTask()
        {
            if (MeetingLocation != null)
            {
                MeetingLocation.IsPlayerInterestedInLocation = true;
            }
            base.AddTask();
        }

        protected override void Loop()
        {
            EntryPoint.WriteToConsole("Meeting Bodyguard Loop Start");
            while (true)
            {
                CurrentTask = PlayerTasks.GetTask(HiringGang.ContactName);
                if (CurrentTask == null || !CurrentTask.IsActive)
                {
                    break;
                }
                if (!HasArrivedNearMeetup && MeetingLocation.DistanceToPlayer <= 200f)
                {
                    OnArrivedNearMeetup();
                }
                if (HasArrivedNearMeetup && MeetingLocation.DistanceToPlayer >= 275f)
                {
                    OnWentAwayFromMeetup();
                }
                if (HasFailedMeeting)
                {
                    break;
                }
                if (HasArrivedNearMeetup && PlayerHasHurtHiringGang())
                {
                    HasFailedMeeting = true;
                    break;
                }
                if (HasArrivedNearMeetup && !HasAnyHiringMemberAlive())
                {
                    HasFailedMeeting = true;
                    break;
                }
                if (IsAmbush && HasArrivedNearMeetup && HasMeetingGangBeenCleared())
                {
                    CurrentTask.OnReadyForPayment(true);
                    break;
                }
                if (IsAmbush && !HasSetViolent && PrimaryMeetingMember != null && PrimaryMeetingMember.PlayerPerception.CanRecognizeTarget)
                {
                    HasSetViolent = true;
                    OnSetMeetingGangViolent();
                }
                if (!IsAmbush && HasArrivedNearMeetup && PlayerHasHurtMeetingGang())
                {
                    HasFailedMeeting = true;
                    break;
                }
                if (!IsAmbush && HasStartedTransaction && Game.GameTime - TransactionStartGameTime >= TransactionDurationMs)
                {
                    CurrentTask.OnReadyForPayment(true);
                    break;
                }
                GameFiber.Sleep(1000);
            }
        }

        protected override void FinishTask()
        {
            if (CurrentTask != null && CurrentTask.IsActive && CurrentTask.IsReadyForPayment)
            {
                SendMoneyPickupMessage();
                OnTaskCompletedOrFailed();
            }
            else if (CurrentTask != null && CurrentTask.IsActive)
            {
                SetFailed();
            }
            else
            {
                Dispose();
            }
        }

        private Gang GetMeetingGang()
        {
            return Gangs.AllGangs.Where(x => x != null && x.ID != HiringGang.ID && x.Contact != null && x.GetRandomPed(0, "") != null).ToList().PickRandom();
        }

        private GameLocation GetMeetingLocation()
        {
            return PlacesOfInterest.PossibleLocations.DrugMeetLocations()
                .Where(x => x.IsCorrectMap(World.IsMPMapLoaded) && x.IsSameState(Player.CurrentLocation?.CurrentZone?.GameState))
                .ToList()
                .PickRandom();
        }

        private void OnArrivedNearMeetup()
        {
            EntryPoint.WriteToConsole($"MEETING BODYGUARD PLAYER NEAR LOCATION Ambush:{IsAmbush}");
            HasArrivedNearMeetup = true;
            if (HasStartedTransaction || HasSetViolent)
            {
                return;
            }
            if (!SpawnMeetingMembers())
            {
                HasFailedMeeting = true;
                return;
            }
            HasStartedTransaction = true;
            TransactionStartGameTime = Game.GameTime;
            Game.DisplaySubtitle($"Stay close while {HiringGang.ColorPrefix}{HiringGang.ShortName}~s~ meet with {MeetingGang.ColorPrefix}{MeetingGang.ShortName}~s~.");
        }

        private void OnWentAwayFromMeetup()
        {
            EntryPoint.WriteToConsole("MEETING BODYGUARD PLAYER LEFT LOCATION");
            HasArrivedNearMeetup = false;
            if (HasStartedTransaction || HasSetViolent)
            {
                HasFailedMeeting = true;
                return;
            }
            CleanupPeds();
        }

        private bool SpawnMeetingMembers()
        {
            CleanupPeds();
            SpawnGangMembers(HiringGang, false, HiringMembers);
            SpawnGangMembers(MeetingGang, IsAmbush, MeetingMembers);
            PrimaryMeetingMember = MeetingMembers.FirstOrDefault();
            return HiringMembers.Any() && MeetingMembers.Any() && PrimaryMeetingMember != null;
        }

        private void SpawnGangMembers(Gang gang, bool isHitSquad, List<GangMember> members)
        {
            members.Clear();
            SpawnLocation spawnLocation = new SpawnLocation(MeetingLocation.EntrancePosition.Around2D(3f));
            spawnLocation.GetClosestStreet(false);
            spawnLocation.GetClosestSidewalk();
            GangSpawnTask gangSpawnTask = new GangSpawnTask(gang, spawnLocation, null, gang.GetRandomPed(0, ""), true, Settings, Weapons, Names, false, Crimes, PedGroups, ShopMenus, World, ModItems, true, true, true);
            gangSpawnTask.PlacePedOnGround = true;
            gangSpawnTask.AllowAnySpawn = true;
            gangSpawnTask.AllowBuddySpawn = true;
            gangSpawnTask.IsHitSquad = isHitSquad;
            gangSpawnTask.SpawnRequirement = TaskRequirements.Guard;
            gangSpawnTask.AttemptSpawn();
            foreach (GangMember gangMember in gangSpawnTask.CreatedPeople.OfType<GangMember>())
            {
                gangMember.IsHitSquad = isHitSquad;
                members.Add(gangMember);
            }
        }

        private bool HasMeetingGangBeenCleared()
        {
            return MeetingMembers.Any() && MeetingMembers.All(IsDeadOrGone);
        }

        private bool HasAnyHiringMemberAlive()
        {
            return HiringMembers.Any() && HiringMembers.Any(x => !IsDeadOrGone(x));
        }

        private bool PlayerHasHurtHiringGang()
        {
            return HiringMembers.Any(x => x.WasKilledByPlayer || x.HasBeenHurtByPlayer);
        }

        private bool PlayerHasHurtMeetingGang()
        {
            return MeetingMembers.Any(x => x.WasKilledByPlayer || x.HasBeenHurtByPlayer);
        }

        private bool IsDeadOrGone(GangMember gangMember)
        {
            return gangMember == null || gangMember.IsDead || !gangMember.Pedestrian.Exists();
        }

        private void OnSetMeetingGangViolent()
        {
            foreach (GangMember gangMember in MeetingMembers)
            {
                gangMember.IsHitSquad = true;
                gangMember.WillFight = true;
                gangMember.WillAlwaysFightPolice = true;
                gangMember.WillFightPolice = true;
            }
            foreach (GangMember gangMember in HiringMembers)
            {
                gangMember.WillFight = true;
                gangMember.WillAlwaysFightPolice = true;
                gangMember.WillFightPolice = true;
            }
            Player.Dispatcher.GangDispatcher.DispatchHitSquad(MeetingGang, true);
            Game.DisplaySubtitle("Ambush. Keep their people off the meet.");
            EntryPoint.WriteToConsole("MEETING BODYGUARD AMBUSH TRIGGERED");
        }

        private void SendMoneyPickupMessage()
        {
            List<string> replies = new List<string>() {
                $"Meeting wrapped. Come back to {HiringGang.DenName} on {HiringGangDen.FullStreetAddress} and collect ${PaymentAmount}.",
                $"You handled that meet. {HiringGangDen.FullStreetAddress}. We owe you ${PaymentAmount}.",
                $"Good work at the sit-down. Get back to {HiringGang.DenName} on {HiringGangDen.FullStreetAddress} for ${PaymentAmount}."
            };
            Player.CellPhone.AddScheduledText(HiringContact, replies.PickRandom(), 1, false);
        }

        private void CleanupPeds()
        {
            foreach (GangMember gangMember in HiringMembers.Concat(MeetingMembers))
            {
                if (gangMember?.Pedestrian.Exists() == true)
                {
                    gangMember.Pedestrian.IsPersistent = false;
                }
            }
            HiringMembers.Clear();
            MeetingMembers.Clear();
            PrimaryMeetingMember = null;
        }

        protected override void OnTaskCompletedOrFailed()
        {
            CleanupPeds();
            if (MeetingLocation != null)
            {
                MeetingLocation.IsPlayerInterestedInLocation = false;
            }
        }
    }
}

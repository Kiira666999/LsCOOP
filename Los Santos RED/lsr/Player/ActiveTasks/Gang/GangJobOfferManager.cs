using ExtensionsMethods;
using LosSantosRED.lsr.Coop.Core;
using LosSantosRED.lsr.Interface;
using Rage;
using RAGENativeUI;
using RAGENativeUI.Elements;
using System;
using System.Collections.Generic;
using System.Linq;

public enum GangJobOfferType
{
    MoneyPickup,
    Arson,
    GangTheft,
    GangHit,
    GangAmbush,
    MeetingBodyguard,
    Wheelman,
    ImpoundTheft,
    VehicleDisposal,
    Bribery,
}

public class GangJobOfferDefinition
{
    public GangJobOfferDefinition(GangJobOfferType offerType, string displayName, string description, bool friendlyTier, bool enabled)
    {
        OfferType = offerType;
        DisplayName = displayName;
        Description = description;
        FriendlyTier = friendlyTier;
        Enabled = enabled;
    }

    public GangJobOfferType OfferType { get; private set; }
    public string DisplayName { get; private set; }
    public string Description { get; private set; }
    public bool FriendlyTier { get; private set; }
    public bool Enabled { get; private set; }
}

public class PendingGangJobOffer
{
    public Gang Gang { get; set; }
    public GangJobOfferDefinition Definition { get; set; }
    public string Message { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ExpiresDate { get; set; }
    public bool IsFriendlyOffer { get; set; }

    public bool Matches(PhoneText text)
    {
        return text != null
            && Gang != null
            && Definition != null
            && string.Equals(text.ContactName, Gang.ContactName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(text.Message, Message, StringComparison.Ordinal);
    }
}

public class GangJobOfferManager
{
    private const float DailyOfferChance = 30f;
    private const float FriendlyBucketChance = 70f;
    private const int OfferResponseWindowHours = 6;

    private readonly ITaskAssignable Player;
    private readonly PlayerTasks PlayerTasks;
    private readonly ITimeReportable Time;
    private readonly IGangs Gangs;
    private readonly IPlacesOfInterest PlacesOfInterest;
    private readonly ISettingsProvideable Settings;
    private readonly IEntityProvideable World;
    private readonly List<GangJobOfferDefinition> OfferDefinitions;

    private PendingGangJobOffer pendingOffer;
    private DateTime nextRollTime = DateTime.MinValue;
    private DateTime lastRollDate = DateTime.MinValue;
    private bool isOfferMenuOpen;

    public GangJobOfferManager(ITaskAssignable player, PlayerTasks playerTasks, ITimeReportable time, IGangs gangs, IPlacesOfInterest placesOfInterest, ISettingsProvideable settings, IEntityProvideable world)
    {
        Player = player;
        PlayerTasks = playerTasks;
        Time = time;
        Gangs = gangs;
        PlacesOfInterest = placesOfInterest;
        Settings = settings;
        World = world;
        OfferDefinitions = BuildOfferDefinitions();
    }

    public void Update()
    {
        if (!CanRunOfferSystem())
        {
            return;
        }

        ClearExpiredOffer();
        EnsureNextRollTime();

        if (DateTime.Compare(Time.CurrentDateTime, nextRollTime) >= 0 && lastRollDate.Date != nextRollTime.Date)
        {
            DateTime rolledDate = nextRollTime.Date;
            lastRollDate = rolledDate;
            ScheduleNextRoll(rolledDate.AddDays(1));
            TryRollOffer();
        }
    }

    public bool TryOpenOfferFromText(PhoneText text)
    {
        if (!IsPendingOfferText(text))
        {
            return false;
        }

        if (IsOfferExpired())
        {
            ClearPendingOffer();
            return false;
        }

        if (isOfferMenuOpen)
        {
            return true;
        }

        OpenOfferMenu(pendingOffer);
        return true;
    }

    public bool IsPendingOfferText(PhoneText text)
    {
        return pendingOffer != null && pendingOffer.Matches(text);
    }

    public void ClearOfferFromText(PhoneText text)
    {
        if (IsPendingOfferText(text))
        {
            ClearPendingOffer();
        }
    }

    public void Clear()
    {
        pendingOffer = null;
        isOfferMenuOpen = false;
        nextRollTime = DateTime.MinValue;
        lastRollDate = DateTime.MinValue;
    }

    private List<GangJobOfferDefinition> BuildOfferDefinitions()
    {
        return new List<GangJobOfferDefinition>
        {
            new GangJobOfferDefinition(GangJobOfferType.MoneyPickup, "Money Pickup", "Pick up cash from a dead drop and bring it back.", false, true),
            new GangJobOfferDefinition(GangJobOfferType.Arson, "Arson", "Burn a target location for the gang.", false, false),
            new GangJobOfferDefinition(GangJobOfferType.GangTheft, "Gang Theft", "Steal an enemy gang car.", true, true),
            new GangJobOfferDefinition(GangJobOfferType.Wheelman, "Wheelman", "Drive for a crew during a robbery.", true, true),
            new GangJobOfferDefinition(GangJobOfferType.GangHit, "Gang Hit", "Hit members of a rival gang.", true, true),
            new GangJobOfferDefinition(GangJobOfferType.GangAmbush, "Ambush", "Set up an ambush against a rival gang.", true, true),
            new GangJobOfferDefinition(GangJobOfferType.MeetingBodyguard, "Meeting Bodyguard", "Attend a gang meet and keep the sit-down under control.", true, true),

            new GangJobOfferDefinition(GangJobOfferType.ImpoundTheft, "Impound Theft", "Steal a gang car from impound.", true, false),
            new GangJobOfferDefinition(GangJobOfferType.VehicleDisposal, "Vehicle Disposal", "Dispose of a dirty vehicle.", true, false),
            new GangJobOfferDefinition(GangJobOfferType.Bribery, "Pay Bribe", "Make a discreet payment for the gang.", true, false),
        };
    }

    private bool CanRunOfferSystem()
    {
        if (!CoopStartupBridge.IsCoopEnabled)
        {
            return true;
        }

        string blockedReason;
        return CoopStartupBridge.GetStartupMode(out blockedReason) == CoopStartupMode.FullSimulation && CoopStartupBridge.IsLocalActiveHost;
    }

    private void EnsureNextRollTime()
    {
        if (nextRollTime == DateTime.MinValue)
        {
            ScheduleNextRoll(Time.CurrentDateTime.Date);
            if (DateTime.Compare(nextRollTime, Time.CurrentDateTime) <= 0)
            {
                ScheduleNextRoll(Time.CurrentDateTime.Date.AddDays(1));
            }
            return;
        }

        if (Time.CurrentDateTime.Date > nextRollTime.Date && lastRollDate.Date < Time.CurrentDateTime.Date)
        {
            ScheduleNextRoll(Time.CurrentDateTime.Date);
            if (DateTime.Compare(nextRollTime, Time.CurrentDateTime) <= 0)
            {
                ScheduleNextRoll(Time.CurrentDateTime.Date.AddDays(1));
            }
        }
    }

    private void ScheduleNextRoll(DateTime date)
    {
        int minutes = RandomItems.GetRandomNumberInt(0, 24 * 60);
        nextRollTime = date.Date.AddMinutes(minutes);
    }

    private void TryRollOffer()
    {
        if (pendingOffer != null || !RandomItems.RandomPercent(DailyOfferChance))
        {
            return;
        }

        GangReputation selectedReputation = PickGangReputation();
        if (selectedReputation == null)
        {
            return;
        }

        bool isFriendlyOffer = IsFriendly(selectedReputation);
        GangJobOfferDefinition selectedJob = PickJob(selectedReputation.Gang, isFriendlyOffer);
        if (selectedJob == null)
        {
            return;
        }

        SendOfferText(selectedReputation.Gang, selectedJob, isFriendlyOffer);
    }

    private GangReputation PickGangReputation()
    {
        List<GangReputation> candidates = Player.RelationshipManager.GangRelationships.GangReputations
            .Where(x => x != null
                && x.Gang != null
                && x.Gang.Contact != null
                && x.ReputationLevel > 0
                && !x.IsEnemy
                && x.GangRelationship != GangRespect.Hostile
                && !IsExcludedGang(x.Gang))
            .ToList();

        List<GangReputation> friendly = candidates.Where(IsFriendly).ToList();
        List<GangReputation> neutral = candidates.Where(x => !IsFriendly(x)).ToList();

        if (friendly.Any() && neutral.Any())
        {
            return RandomItems.RandomPercent(FriendlyBucketChance) ? friendly.PickRandom() : neutral.PickRandom();
        }
        if (friendly.Any())
        {
            return friendly.PickRandom();
        }
        if (neutral.Any())
        {
            return neutral.PickRandom();
        }
        return null;
    }

    private bool IsFriendly(GangReputation gangReputation)
    {
        return gangReputation != null
            && (gangReputation.IsMember
                || gangReputation.GangRelationship == GangRespect.Member
                || gangReputation.GangRelationship == GangRespect.Friendly);
    }

    private bool IsExcludedGang(Gang gang)
    {
        string combined = NormalizeGangKey((gang.ID ?? "") + " " + (gang.FullName ?? "") + " " + (gang.ShortName ?? "") + " " + (gang.ContactName ?? ""));
        return combined.Contains("corruptpolice")
            || combined.Contains("corruptcop")
            || combined.Contains("corruptbank")
            || combined.Contains("customer");
    }

    private string NormalizeGangKey(string value)
    {
        return value.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
    }

    private GangJobOfferDefinition PickJob(Gang gang, bool isFriendlyOffer)
    {
        List<GangJobOfferDefinition> jobs = OfferDefinitions
            .Where(x => x.Enabled && x.FriendlyTier == isFriendlyOffer && CanOfferJob(gang, x))
            .ToList();
        return jobs.Any() ? jobs.PickRandom() : null;
    }

    private bool CanOfferJob(Gang gang, GangJobOfferDefinition definition)
    {
        if (gang == null || definition == null)
        {
            return false;
        }

        if (definition.OfferType == GangJobOfferType.GangHit || definition.OfferType == GangJobOfferType.GangAmbush)
        {
            return GetTargetGang(gang) != null;
        }
        if (definition.OfferType == GangJobOfferType.GangTheft)
        {
            Gang targetGang = GetTargetGang(gang);
            return targetGang != null && GetTargetGangVehicle(targetGang) != null;
        }
        if (definition.OfferType == GangJobOfferType.MeetingBodyguard)
        {
            return HasPaymentRange(gang.MeetingBodyguardPaymentMin, gang.MeetingBodyguardPaymentMax)
                && GetMeetingTargetGang(gang) != null
                && GetMeetingLocation() != null;
        }

        return true;
    }

    private bool HasPaymentRange(int min, int max)
    {
        return max > 0 && max >= min;
    }

    private void SendOfferText(Gang gang, GangJobOfferDefinition job, bool isFriendlyOffer)
    {
        string message = GetOfferMessage(job, isFriendlyOffer);
        pendingOffer = new PendingGangJobOffer
        {
            Gang = gang,
            Definition = job,
            Message = message,
            CreatedDate = Time.CurrentDateTime,
            ExpiresDate = Time.CurrentDateTime.AddHours(OfferResponseWindowHours),
            IsFriendlyOffer = isFriendlyOffer,
        };
        Player.CellPhone.AddTextWithoutAddingContact(gang.Contact, message, true);
    }

    private string GetOfferMessage(GangJobOfferDefinition job, bool isFriendlyOffer)
    {
        List<string> friendlyMessages = new List<string>
        {
            $"Need somebody reliable for {job.DisplayName}. Pay is worth the noise. Open this if you are in.",
            $"Got real work for you: {job.DisplayName}. You know how this goes. Confirm it if you want the job.",
            $"A job came up and your name made the list. {job.DisplayName}. Open this and answer.",
            $"We have a higher-risk piece of work: {job.DisplayName}. Do it right and everybody eats.",
            $"You have done enough with us to get this offer. {job.DisplayName}. Confirm if you are moving.",
        };

        List<string> neutralMessages = new List<string>
        {
            $"You did business with us once. Got a small thing: {job.DisplayName}. Open this if you want in.",
            $"Somebody said you can handle yourself. Simple work on the table: {job.DisplayName}.",
            $"We are not friends, but you are not unknown. {job.DisplayName}. Confirm if you want the work.",
            $"There is a small job if you want to earn standing: {job.DisplayName}. Open this and answer.",
            $"You have been around enough to get a chance. {job.DisplayName}. Take it or leave it.",
            $"One job, no promises after. {job.DisplayName}. Confirm if you are serious.",
        };

        return (isFriendlyOffer ? friendlyMessages : neutralMessages).PickRandom();
    }

    private bool IsOfferExpired()
    {
        return pendingOffer != null && DateTime.Compare(Time.CurrentDateTime, pendingOffer.ExpiresDate) >= 0;
    }

    private void ClearExpiredOffer()
    {
        if (IsOfferExpired())
        {
            ClearPendingOffer();
        }
    }

    private void OpenOfferMenu(PendingGangJobOffer offer)
    {
        if (offer == null || offer.Gang == null || offer.Definition == null)
        {
            return;
        }

        isOfferMenuOpen = true;
        GameFiber.StartNew(delegate
        {
            bool handled = false;
            MenuPool menuPool = new MenuPool();
            UIMenu offerMenu = new UIMenu(offer.Gang.ContactName, "Job Offer");
            menuPool.Add(offerMenu);

            UIMenuItem accept = new UIMenuItem("Accept", offer.Definition.Description) { RightLabel = GetPaymentLabel(offer.Gang, offer.Definition) };
            UIMenuItem decline = new UIMenuItem("Decline", "Pass on this offer.");

            accept.Activated += (sender, selectedItem) =>
            {
                handled = true;
                AcceptOffer(offer);
                sender.Visible = false;
            };
            decline.Activated += (sender, selectedItem) =>
            {
                handled = true;
                ClearPendingOffer();
                sender.Visible = false;
            };

            offerMenu.AddItem(new UIMenuItem(offer.Definition.DisplayName, offer.Definition.Description) { RightLabel = GetPaymentLabel(offer.Gang, offer.Definition), Enabled = false });
            offerMenu.AddItem(accept);
            offerMenu.AddItem(decline);
            offerMenu.Visible = true;

            while (menuPool.IsAnyMenuOpen())
            {
                menuPool.ProcessMenus();
                GameFiber.Yield();
            }

            if (!handled)
            {
                ClearPendingOffer();
            }

            isOfferMenuOpen = false;
        }, "GangJobOfferMenu");
    }

    private void AcceptOffer(PendingGangJobOffer offer)
    {
        if (pendingOffer != offer || IsOfferExpired())
        {
            ClearPendingOffer();
            return;
        }

        ClearPendingOffer();
        StartJob(offer);
    }

    private void ClearPendingOffer()
    {
        pendingOffer = null;
    }

    private void StartJob(PendingGangJobOffer offer)
    {
        Gang gang = offer.Gang;
        GangContact contact = gang.Contact;

        switch (offer.Definition.OfferType)
        {
            case GangJobOfferType.MoneyPickup:
                PlayerTasks.GangTasks.StartGangPickup(gang, contact);
                break;
            case GangJobOfferType.Arson:
                PlayerTasks.GangTasks.StartGangArson(gang, contact);
                break;
            case GangJobOfferType.GangTheft:
                Gang theftTarget = GetTargetGang(gang);
                if (theftTarget != null)
                {
                    DispatchableVehicle vehicle = GetTargetGangVehicle(theftTarget);
                    if (vehicle != null)
                    {
                        PlayerTasks.GangTasks.StartGangVehicleTheft(gang, contact, theftTarget, vehicle.ModelName, vehicle.ModelName);
                    }
                }
                break;
            case GangJobOfferType.GangHit:
                Gang hitTarget = GetTargetGang(gang);
                if (hitTarget != null)
                {
                    PlayerTasks.GangTasks.StartGangHit(gang, 1, contact, hitTarget);
                }
                break;
            case GangJobOfferType.GangAmbush:
                Gang ambushTarget = GetTargetGang(gang);
                if (ambushTarget != null)
                {
                    PlayerTasks.GangTasks.StartGangAmbush(gang, 1, contact, ambushTarget);
                }
                break;
            case GangJobOfferType.MeetingBodyguard:
                PlayerTasks.GangTasks.StartGangMeetingBodyguard(gang, contact, GetMeetingTargetGang(gang), GetMeetingLocation());
                break;
            case GangJobOfferType.Wheelman:
                PlayerTasks.GangTasks.StartGangWheelman(gang, contact, 1, "Random", true);
                break;
            case GangJobOfferType.ImpoundTheft:
                PlayerTasks.GangTasks.StartImpoundTheft(gang, contact);
                break;
            case GangJobOfferType.VehicleDisposal:
                PlayerTasks.GangTasks.StartGangBodyDisposal(gang, contact);
                break;
            case GangJobOfferType.Bribery:
                PlayerTasks.GangTasks.StartGangBribery(gang, contact);
                break;
        }
    }

    private Gang GetTargetGang(Gang hiringGang)
    {
        if (hiringGang == null)
        {
            return null;
        }

        List<string> enemyGangIds = hiringGang.EnemyGangs ?? new List<string>();
        List<Gang> possibleTargets = Settings.SettingsManager.GangSettings.AllowNonEnemyTargets
            ? Gangs.AllGangs.Where(x => x.ID != hiringGang.ID).ToList()
            : Gangs.AllGangs.Where(x => x.ID != hiringGang.ID && enemyGangIds.Contains(x.ID)).ToList();

        return possibleTargets.Any() ? possibleTargets.PickRandom() : null;
    }

    private Gang GetMeetingTargetGang(Gang hiringGang)
    {
        if (hiringGang == null)
        {
            return null;
        }

        List<Gang> possibleTargets = Gangs.AllGangs
            .Where(x => x != null
                && x.ID != hiringGang.ID
                && x.Contact != null
                && x.GetRandomPed(0, "") != null
                && !IsExcludedGang(x))
            .Where(x =>
            {
                GangReputation reputation = Player.RelationshipManager.GangRelationships.GetReputation(x);
                return reputation != null && !reputation.IsMember;
            })
            .ToList();

        return possibleTargets.Any() ? possibleTargets.PickRandom() : null;
    }

    private GameLocation GetMeetingLocation()
    {
        if (PlacesOfInterest == null)
        {
            return null;
        }

        List<GameLocation> locations = PlacesOfInterest.PossibleLocations.DrugMeetLocations()
            .Where(x => x.IsCorrectMap(World.IsMPMapLoaded) && x.IsSameState(Player.CurrentLocation?.CurrentZone?.GameState))
            .ToList();

        return locations.Any() ? locations.PickRandom() : null;
    }

    private DispatchableVehicle GetTargetGangVehicle(Gang targetGang)
    {
        if (targetGang?.Vehicles == null)
        {
            return null;
        }

        List<DispatchableVehicle> vehicles = targetGang.Vehicles
            .Where(x => !x.RequiresDLC || Settings.SettingsManager.PlayerOtherSettings.AllowDLCVehicles)
            .ToList();

        return vehicles.Any() ? vehicles.PickRandom() : null;
    }

    private string GetPaymentLabel(Gang gang, GangJobOfferDefinition definition)
    {
        switch (definition.OfferType)
        {
            case GangJobOfferType.MoneyPickup:
                return $"~HUD_COLOUR_GREENDARK~{gang.PickupPaymentMin:C0}-{gang.PickupPaymentMax:C0}~s~";
            case GangJobOfferType.Arson:
                return $"~HUD_COLOUR_GREENDARK~{gang.ArsonPaymentMin:C0}-{gang.ArsonPaymentMax:C0}~s~";
            case GangJobOfferType.GangHit:
                return $"~HUD_COLOUR_GREENDARK~{gang.HitPaymentMin:C0}-{gang.HitPaymentMax:C0}~s~";
            case GangJobOfferType.GangAmbush:
                return $"~HUD_COLOUR_GREENDARK~{gang.AmbushPaymentMin:C0}-{gang.AmbushPaymentMax:C0}~s~";
            case GangJobOfferType.MeetingBodyguard:
                return $"~HUD_COLOUR_GREENDARK~{gang.MeetingBodyguardPaymentMin:C0}-{gang.MeetingBodyguardPaymentMax:C0}~s~";
            case GangJobOfferType.Wheelman:
                return $"~HUD_COLOUR_GREENDARK~{gang.WheelmanPaymentMin:C0}-{gang.WheelmanPaymentMax:C0}~s~";
            case GangJobOfferType.GangTheft:
                return $"~HUD_COLOUR_GREENDARK~{gang.TheftPaymentMin:C0}-{gang.TheftPaymentMax:C0}~s~";
            default:
                return "";
        }
    }
}

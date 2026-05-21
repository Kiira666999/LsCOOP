using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class CriminalHistorySettings : ISettingsDefaultable
{
    [Description("Game time in milliseconds required for the bolo/apb to expire for each wanted level. Ex a value of 60000 would take 120 seconds or real game time to expire a 2 star bolo/apb")]
    public uint RealTimeExpireWantedMultiplier { get; set; }
    [Description("In-game calendar hours required for the bolo/apb to expire for each wanted level. Ex a value of 12 would take 24 hours of in-game calendar time expire a 2 star bolo/apb")]
    public int CalendarTimeExpireWantedMultiplier { get; set; }
    [Description("Create a blip to show the area the bolo/apb encompasses")]
    public bool CreateBlip { get; set; }
    [Description("Minimum size of the bolo/apb area. Ex. bolo for a one star wanted")]
    public float MinimumSearchRadius { get; set; }
    [Description("Additional radius added for each wanted level. Ex. At SearchRadiusIncrement = 400 a 3 star wanted level would result in a 1.2 km bolo/apb radius ")]
    public float SearchRadiusIncrement { get; set; }
    [Description("Controls expiration behavior for APBs, defined as criminal history containing at least one crime with ResultsInLethalForce=true. Normal preserves existing behavior, Extended multiplies existing expiration timers, UntilResolved disables automatic expiration until a resolving clear path.")]
    public ApbPersistenceMode ApbPersistenceMode { get; set; }
    [Description("Multiplier applied to existing APB real-time and calendar-time expiration when ApbPersistenceMode is Extended.")]
    public float ExtendedApbExpirationMultiplier { get; set; }
    public CriminalHistorySettings()
    {
        SetDefault();
    }
    public void SetDefault()
    {
        RealTimeExpireWantedMultiplier = 60000;
        CalendarTimeExpireWantedMultiplier = 12;
        CreateBlip = true;
        MinimumSearchRadius = 400f;
        SearchRadiusIncrement = 400f;
        ApbPersistenceMode = ApbPersistenceMode.UntilResolved;
        ExtendedApbExpirationMultiplier = 5.0f;
    }

}

public enum ApbPersistenceMode
{
    Normal = 0,
    Extended = 1,
    UntilResolved = 2,
}

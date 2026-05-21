using LosSantosRED.lsr.Helper;
using LosSantosRED.lsr.Interface;
using LosSantosRED.lsr.Locations;
using LSR.Vehicles;
using Rage;
using Rage.Native;
using RAGENativeUI.Elements;
using RAGENativeUI.PauseMenu;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

public class PlayerInfoMenu
{
    private IGangs Gangs;
    private IGangTerritories GangTerritories;
    private IInteriors Interiors;
    private IPlacesOfInterest PlacesOfInterest;
    private IGangRelateable Player;
    private IStreets Streets;
    private TabView tabView;
    private ITimeReportable Time;
    private IEntityProvideable World;
    private IZones Zones;
    private IShopMenus ShopMenus;
    private IModItems ModItems;
    private IWeapons Weapons;

    private LocationsTab LocationsTab;
    private VehiclesTab VehiclesTab;
    private LicensesTab LicensesTab;
    private CrimesTab CrimesTab;
    private GangTab GangTab;
    private ZonesTab ZonesTab;

    private ISettingsProvideable Settings;
    private ILocationTypes LocationTypes;
    private bool isDisposed = true;
    private bool isRawFrameRenderSubscribed;
    private bool hasLoggedTextureDrawError;
    private bool isTextureDrawingDisabled;

    public PlayerInfoMenu(IGangRelateable player, ITimeReportable time, IPlacesOfInterest placesOfInterest, IGangs gangs, IGangTerritories gangTerritories, IZones zones, 
        IStreets streets, IInteriors interiors, IEntityProvideable world, IShopMenus shopMenus, IModItems modItems, IWeapons weapons, ISettingsProvideable settings, ILocationTypes locationTypes)
    {
        Player = player;
        Time = time;
        PlacesOfInterest = placesOfInterest;
        Gangs = gangs;
        GangTerritories = gangTerritories;
        Zones = zones;
        Streets = streets;
        Interiors = interiors;
        World = world;
        ShopMenus = shopMenus;
        ModItems = modItems;
        Weapons = weapons;
        Settings = settings;
        LocationTypes = locationTypes;
    }
    public void Setup()
    {
        Dispose();
        isDisposed = false;
        hasLoggedTextureDrawError = false;
        isTextureDrawingDisabled = false;
        tabView = new TabView("Los Santos ~r~RED~s~ Information");
        tabView.Tabs.Clear();
        tabView.ScrollTabs = true;
        tabView.OnMenuClose += (s, e) =>
        {
            Game.IsPaused = false;
        };

        SubscribeRawFrameRender();


        LocationsTab = new LocationsTab(Player, PlacesOfInterest, Time, Settings, tabView, World);
        VehiclesTab = new VehiclesTab(Player, Streets, Zones, Interiors, tabView, Settings);
        LicensesTab = new LicensesTab(Player, Time, tabView, LocationTypes);
        CrimesTab = new CrimesTab(Player, tabView);
        GangTab = new GangTab(Player,PlacesOfInterest,ShopMenus,ModItems,Weapons,GangTerritories,Zones, tabView, Time, Settings, World);
        ZonesTab = new ZonesTab(Player, PlacesOfInterest, ShopMenus, ModItems, Zones, tabView, GangTerritories, Settings, World);
    }
    public void Toggle()
    {
        try
        {
            if (isDisposed || tabView == null)
            {
                return;
            }
            if (!TabView.IsAnyPauseMenuVisible)
            {
                if (!tabView.Visible)
                {
                    UpdateMenu();
                    Game.IsPaused = true;
                }
                tabView.Visible = !tabView.Visible;
            }
        }
        catch(Exception ex)
        {
            if (tabView != null && tabView.Visible)
            {
                tabView.Visible = false;
            }
            Game.IsPaused = false;
            Game.DisplayNotification("Error Opening Menu");
            EntryPoint.WriteToConsole($"Player Info Menu Toggle Error: {ex.Message} STACKTRACE:{ex.StackTrace}",0);
        }
    }
    public void Update()
    {
        if (isDisposed || tabView == null)
        {
            return;
        }
        tabView.Update();
        if (tabView.Visible)
        {
            tabView.Money = Time.CurrentDateTime.ToString("ddd, dd MMM yyyy hh:mm tt");
        }
    }
    public void Dispose()
    {
        if (isDisposed && !isRawFrameRenderSubscribed)
        {
            return;
        }

        bool wasVisible = tabView != null && tabView.Visible;
        isDisposed = true;
        UnsubscribeRawFrameRender();

        if (tabView != null)
        {
            tabView.Visible = false;
        }
        if (wasVisible)
        {
            Game.IsPaused = false;
        }
    }
    private void UpdateMenu()
    {
        if (isDisposed || tabView == null)
        {
            return;
        }
        tabView.MoneySubtitle = Player.BankAccounts.TotalMoney.ToString("C0");
        tabView.Name = Player.PlayerName;
        tabView.Money = Time.CurrentTime;
        tabView.Tabs.Clear();

        VehiclesTab.AddItems();
        LicensesTab.AddItems();
        CrimesTab.AddItems();
        GangTab.AddItems();
        ZonesTab.AddItems();
        LocationsTab.AddItems();

        tabView.RefreshIndex();
        tabView.ShowInstructionalButtons();
    }
    private void SubscribeRawFrameRender()
    {
        if (isRawFrameRenderSubscribed || isTextureDrawingDisabled)
        {
            return;
        }
        Game.RawFrameRender += OnRawFrameRender;
        isRawFrameRenderSubscribed = true;
    }
    private void UnsubscribeRawFrameRender()
    {
        if (!isRawFrameRenderSubscribed)
        {
            return;
        }
        Game.RawFrameRender -= OnRawFrameRender;
        isRawFrameRenderSubscribed = false;
    }
    private void OnRawFrameRender(object sender, GraphicsEventArgs e)
    {
        if (isDisposed || isTextureDrawingDisabled || tabView == null || !tabView.Visible)
        {
            return;
        }

        try
        {
            tabView.DrawTextures(e.Graphics);
        }
        catch (Exception ex)
        {
            if (!hasLoggedTextureDrawError)
            {
                hasLoggedTextureDrawError = true;
                EntryPoint.WriteToConsole($"Player Info Menu Texture Draw Error: {ex.Message} STACKTRACE:{ex.StackTrace}", 0);
            }
            isTextureDrawingDisabled = true;
            UnsubscribeRawFrameRender();
            if (tabView != null)
            {
                tabView.Visible = false;
            }
            Game.IsPaused = false;
        }
    }
}

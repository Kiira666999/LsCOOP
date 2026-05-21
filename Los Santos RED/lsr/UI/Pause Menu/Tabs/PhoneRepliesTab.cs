using LosSantosRED.lsr.Interface;
using Rage;
using RAGENativeUI.PauseMenu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public class PhoneRepliesTab : ITabbableMenu
{
    private IGangRelateable Player;
    private TabView TabView;
    private PhoneMessageLocationResolver PhoneMessageLocationResolver;

    public PhoneRepliesTab(IGangRelateable player, TabView tabView, IPlacesOfInterest placesOfInterest, IEntityProvideable world)
    {
        Player = player;
        TabView = tabView;
        PhoneMessageLocationResolver = new PhoneMessageLocationResolver(player, placesOfInterest, world);
    }

    public void AddItems()
    {
        List<TabItem> items = new List<TabItem>();
        bool addedItems = false;
        foreach (PhoneResponse text in Player.CellPhone.PhoneResponseList.OrderByDescending(x => x.TimeReceived).Take(15))
        {
            string TimeReceived = text.TimeReceived.ToString("HH:mm");// text.HourSent.ToString("00") + ":" + text.MinuteSent.ToString("00");// string.Format("{0:D2}h:{1:D2}m",text.HourSent,text.MinuteSent);
            string DescriptionText = "";
            DescriptionText += $"~n~Received At: {TimeReceived}";  //+ gr.ToStringBare();
            DescriptionText += $"~n~{text.Message}";
            //DescriptionText += $"~n~~n~Select to ~r~Delete Response~s~";
            string ListEntryItem = $"{text.ContactName} {TimeReceived}";
            string DescriptionHeaderText = $"{text.ContactName}";
            GameLocation routeLocation = PhoneMessageLocationResolver.FindLocation(text.Message);
            if (routeLocation != null)
            {
                DescriptionText += $"~n~~n~Select to set GPS to ~o~{routeLocation.Name}~s~";
            }
            TabItem tItem = new TabTextItem(ListEntryItem, DescriptionHeaderText, DescriptionText);
            if (routeLocation != null)
            {
                tItem.Activated += (s, e) => PhoneMessageLocationResolver.AddGPSRoute(routeLocation);
            }
            //tItem.Activated += (s, e) =>
            //{
            //    if (text != null)
            //    {
            //        Player.CellPhone.DeletePhoneRespone(text);
            //        PhoneRepliesSubMenu.Items.Remove(tItem);
            //        PhoneRepliesSubMenu.RefreshIndex();
            //        EntryPoint.WriteToConsole($"Phone Reply deleted {text.ContactName} {text.Message}");
            //    }
            //};
            items.Add(tItem);
            addedItems = true;
        }
        TabItem ClearPhoneResponses = new TabTextItem("Clear Phone Responses", "Clear Phone Responses", "Select to clear all ~o~Phone Responses~s~");//TabItem tabItem = new TabTextItem($"{gr.Gang.ColorPrefix}{gr.Gang.FullName}~s~ {gr.ToBlip()}~s~", $"{gr.Gang.ColorPrefix}{gr.Gang.FullName}~s~", DescriptionText);
        ClearPhoneResponses.Activated += (s, e) =>
        {
            TabView.Visible = false;
            Game.IsPaused = false;
            Player.CellPhone.ClearPhoneResponses();
        };
        items.Add(ClearPhoneResponses);
        if (addedItems)
        {
            TabView.AddTab(new TabSubmenuItem("Replies", items));
        }
        else
        {
            TabView.AddTab(new TabItem("Replies"));
        }
    }
}


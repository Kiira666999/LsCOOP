using LosSantosRED.lsr.Interface;
using Rage;
using RAGENativeUI.PauseMenu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public class MessagesTab : ITabbableMenu
{
    private IGangRelateable Player;
    private TabView TabView;
    private PhoneMessageLocationResolver PhoneMessageLocationResolver;

    public MessagesTab(IGangRelateable player, TabView tabView, IPlacesOfInterest placesOfInterest, IEntityProvideable world)
    {
        Player = player;
        TabView = tabView;
        PhoneMessageLocationResolver = new PhoneMessageLocationResolver(player, placesOfInterest, world);
    }

    public void AddItems()
    {
        List<TabItem> items = new List<TabItem>();
        bool addedItems = false;

        List<Tuple<string, DateTime>> MessageTimes = new List<Tuple<string, DateTime>>();

        MessageTimes.AddRange(Player.CellPhone.PhoneResponseList.OrderByDescending(x => x.TimeReceived).Take(15).Select(x => new Tuple<string, DateTime>(x.ContactName, x.TimeReceived)));
        MessageTimes.AddRange(Player.CellPhone.TextList.OrderByDescending(x => x.TimeReceived).Take(15).Select(x => new Tuple<string, DateTime>(x.ContactName, x.TimeReceived)));

        foreach (Tuple<string, DateTime> dateTime in MessageTimes.OrderByDescending(x => x.Item2).Take(15))
        {
            PhoneResponse pr = Player.CellPhone.PhoneResponseList.Where(x => x.TimeReceived == dateTime.Item2 && x.ContactName == dateTime.Item1).FirstOrDefault();
            if (pr != null)
            {
                string TimeReceived = pr.TimeReceived.ToString("HH:mm");
                string DescriptionText = "";
                DescriptionText += $"~n~Received At: {TimeReceived}";
                DescriptionText += $"~n~{pr.Message}";
                string ListEntryItem = $"{pr.ContactName} {TimeReceived}";
                string DescriptionHeaderText = $"{pr.ContactName}";
                GameLocation routeLocation = PhoneMessageLocationResolver.FindLocation(pr.Message);
                if (routeLocation != null)
                {
                    DescriptionText += $"~n~~n~Select to set GPS to ~o~{routeLocation.Name}~s~";
                }
                TabItem tItem = new TabTextItem(ListEntryItem, DescriptionHeaderText, DescriptionText);
                if (routeLocation != null)
                {
                    tItem.Activated += (s, e) => PhoneMessageLocationResolver.AddGPSRoute(routeLocation);
                }
                items.Add(tItem);
                addedItems = true;
            }
            PhoneText text = Player.CellPhone.TextList.Where(x => x.TimeReceived == dateTime.Item2 && x.ContactName == dateTime.Item1).FirstOrDefault();
            if (text != null)
            {
                string TimeReceived = text.HourSent.ToString("00") + ":" + text.MinuteSent.ToString("00");// string.Format("{0:D2}h:{1:D2}m",text.HourSent,text.MinuteSent);
                string DescriptionText = "";
                DescriptionText += $"~n~Received At: {TimeReceived}";  //+ gr.ToStringBare();
                DescriptionText += $"~n~{text.Message}";
                string ListEntryItem = $"{text.ContactName}{(!text.IsRead ? " *" : "")} {TimeReceived}";
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
                items.Add(tItem);
                addedItems = true;
            }
        }
        if (addedItems)
        {
            TabView.AddTab(new TabSubmenuItem("Recent", items));
        }
        else
        {
            TabView.AddTab(new TabItem("Recent"));
        }
    }
}

public class PhoneMessageLocationResolver
{
    private static readonly string[] FormattingTokens = new string[]
    {
        "~r~",
        "~b~",
        "~g~",
        "~y~",
        "~p~",
        "~q~",
        "~o~",
        "~c~",
        "~m~",
        "~u~",
        "~s~",
        "~w~",
        "~h~",
    };

    private IGangRelateable Player;
    private IPlacesOfInterest PlacesOfInterest;
    private IEntityProvideable World;

    public PhoneMessageLocationResolver(IGangRelateable player, IPlacesOfInterest placesOfInterest, IEntityProvideable world)
    {
        Player = player;
        PlacesOfInterest = placesOfInterest;
        World = world;
    }

    public GameLocation FindLocation(string message)
    {
        string normalizedMessage = Normalize(message);
        if (string.IsNullOrWhiteSpace(normalizedMessage) || PlacesOfInterest == null)
        {
            return null;
        }

        List<GameLocation> locations = PlacesOfInterest.AllLocations();
        if (locations == null)
        {
            return null;
        }

        bool isMPMapLoaded = World != null && World.IsMPMapLoaded;
        return locations
            .Where(x => x != null && x.IsEnabled && x.IsCorrectMap(isMPMapLoaded) && x.IsSameState(Player?.CurrentLocation?.CurrentZone?.GameState))
            .Select(x => GetMatch(normalizedMessage, x))
            .Where(x => x != null)
            .OrderBy(x => x.Index)
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.TermLength)
            .ThenBy(x => GetDistanceToPlayer(x.Location))
            .FirstOrDefault()?.Location;
    }

    public void AddGPSRoute(GameLocation location)
    {
        if (location == null || Player?.GPSManager == null)
        {
            return;
        }
        Player.GPSManager.AddGPSRoute(location.Name, location.EntrancePosition, true);
    }

    private PhoneMessageLocationMatch GetMatch(string normalizedMessage, GameLocation location)
    {
        PhoneMessageLocationMatch bestMatch = null;
        bestMatch = GetBestMatch(bestMatch, CreateMatch(normalizedMessage, location, location.FullStreetAddress, 3));
        bestMatch = GetBestMatch(bestMatch, CreateMatch(normalizedMessage, location, location.StreetAddress, 2));

        if (bestMatch != null && ContainsTerm(normalizedMessage, location.Name))
        {
            bestMatch.Score++;
        }
        return bestMatch;
    }

    private PhoneMessageLocationMatch CreateMatch(string normalizedMessage, GameLocation location, string term, int score)
    {
        string normalizedTerm = Normalize(term);
        if (string.IsNullOrWhiteSpace(normalizedTerm) || normalizedTerm.Length < 6)
        {
            return null;
        }

        int index = normalizedMessage.IndexOf(normalizedTerm, StringComparison.OrdinalIgnoreCase);
        if (index == -1)
        {
            return null;
        }

        return new PhoneMessageLocationMatch(location, index, score, normalizedTerm.Length);
    }

    private bool ContainsTerm(string normalizedMessage, string term)
    {
        string normalizedTerm = Normalize(term);
        return !string.IsNullOrWhiteSpace(normalizedTerm)
            && normalizedTerm.Length >= 6
            && normalizedMessage.IndexOf(normalizedTerm, StringComparison.OrdinalIgnoreCase) != -1;
    }

    private PhoneMessageLocationMatch GetBestMatch(PhoneMessageLocationMatch current, PhoneMessageLocationMatch candidate)
    {
        if (candidate == null)
        {
            return current;
        }
        if (current == null)
        {
            return candidate;
        }
        if (candidate.Index != current.Index)
        {
            return candidate.Index < current.Index ? candidate : current;
        }
        if (candidate.Score != current.Score)
        {
            return candidate.Score > current.Score ? candidate : current;
        }
        return candidate.TermLength > current.TermLength ? candidate : current;
    }

    private float GetDistanceToPlayer(GameLocation location)
    {
        if (location == null || Player?.Character == null)
        {
            return float.MaxValue;
        }
        return Player.Character.DistanceTo2D(location.EntrancePosition);
    }

    private string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        string normalized = text.Replace("~n~", " ");
        foreach (string token in FormattingTokens)
        {
            normalized = normalized.Replace(token, "");
        }
        return normalized.Trim();
    }

    private class PhoneMessageLocationMatch
    {
        public PhoneMessageLocationMatch(GameLocation location, int index, int score, int termLength)
        {
            Location = location;
            Index = index;
            Score = score;
            TermLength = termLength;
        }

        public GameLocation Location { get; private set; }
        public int Index { get; private set; }
        public int Score { get; set; }
        public int TermLength { get; private set; }
    }
}


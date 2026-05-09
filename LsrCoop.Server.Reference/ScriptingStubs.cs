using System.Collections.Generic;
using RageCoop.Core;
using RageCoop.Core.Scripting;

namespace RageCoop.Server.Scripting
{
    public class ServerScript
    {
        public virtual void OnStart()
        {
        }

        public virtual void OnStop()
        {
        }

        public API API { get; }
        public ServerResource CurrentResource { get; }
        public ResourceFile CurrentFile { get; }
        public Logger Logger { get; }
    }

    public class API
    {
        public ServerEvents Events;

        public Dictionary<int, Client> GetAllClients()
        {
            return new Dictionary<int, Client>();
        }

        public void RegisterCustomEventHandler(int hash, System.Action<CustomEventReceivedArgs> handler)
        {
        }

        public void RegisterCustomEventHandler(string name, System.Action<CustomEventReceivedArgs> handler)
        {
        }

        public void SendCustomEvent(List<Client> targets, int eventHash, object[] args)
        {
        }
    }

    public class ServerResource
    {
        public string Name { get; }
        public string DataFolder { get; }
        public List<ServerScript> Scripts { get; }
        public Dictionary<string, ResourceFile> Files { get; }
        public Logger Logger;
    }

    public class ServerEvents
    {
        public event System.Action<Client> OnPlayerReady;
        public event System.Action<Client> OnPlayerDisconnected;
    }

    public class CustomEventReceivedArgs : System.EventArgs
    {
        public Client Client { get; set; }
        public int Hash { get; set; }
        public object[] Args { get; set; }
    }
}

namespace RageCoop.Server
{
    public class Client
    {
        public string Username { get; set; }

        public void SendCustomEvent(int eventHash, object[] args)
        {
        }
    }
}

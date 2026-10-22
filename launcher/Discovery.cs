using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Lanppart3Launcher
{
    // A server heard on the network.
    public sealed class ServerInfo
    {
        public string Uid;         // machine/server: one server heard through several networks (LAN, Radmin)
        public string Address;     // ip:port
        public string Name;
        public GameMode Mode;
        public bool Roulette;
        public int Lives;
        public int Players;
        public int Capacity;
        public string Phase;
        public DateTime Seen;
        public bool Manual;        // added by IP

        public string ModeText => Mode == GameMode.BattleRoyale ? "Battle royale" : Mode == GameMode.Knockdown ? "K.O." : "Duel";
        public string RulesText => Roulette ? "Roulette" : "Vies : " + Lives;
        public string PhaseText => Phase == "waiting" ? "En attente" : Phase == "playing" ? "En jeu" : Phase == "ended" ? "Fin de partie" :
                                   string.IsNullOrEmpty(Phase) ? "?" : "Démarrage";
    }

    // Lanppart3 servers announce themselves once a second with a UDP broadcast on every network interface,
    // Radmin VPN's included (its adapter is a 26.0.0.0/8 network): players' launchers and games listen for it.
    //   LANPPART3|1|uid|port|mode|roulette|lives|players|capacity|phase|name
    public static class Discovery
    {
        public const int Port = 47777;
        const string Magic = "LANPPART3";

        public static byte[] Beacon(ServerInstance s)
        {
            var st = s.Settings;
            var name = (st.Name ?? "").Replace("|", "/");
            var uid = (Environment.MachineName + "/" + s.Id).Replace("|", "/");
            var msg = string.Join("|", Magic, "1", uid, st.Port, (int)st.Mode, st.Roulette ? 1 : 0, st.Lives, s.PlayerCount,
                                  ServerSettings.Capacity(st.Mode), s.Phase, name);
            return Encoding.UTF8.GetBytes(msg);
        }

        public static ServerInfo Parse(byte[] data, IPAddress from)
        {
            var f = Encoding.UTF8.GetString(data).Split(new[] { '|' }, 11);
            if (f.Length < 11 || f[0] != Magic) return null;
            int.TryParse(f[3], out var port);
            int.TryParse(f[4], out var mode);
            int.TryParse(f[6], out var lives);
            int.TryParse(f[7], out var players);
            int.TryParse(f[8], out var capacity);
            return new ServerInfo
            {
                Uid = f[2], Address = from + ":" + port, Mode = (GameMode)mode, Roulette = f[5] == "1", Lives = lives,
                Players = players, Capacity = capacity, Phase = f[9], Name = f[10], Seen = DateTime.Now,
            };
        }

        // Broadcast address of every IPv4 interface that is up (plus the limited broadcast).
        public static List<IPAddress> BroadcastAddresses()
        {
            var list = new List<IPAddress> { IPAddress.Broadcast };
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                    var addr = new IPAddress(b);
                    if (!list.Contains(addr)) list.Add(addr);
                }
            }
            return list;
        }

        // This PC's IPv4 addresses (to show players where to connect by hand).
        public static List<string> LocalAddresses() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString()).Distinct().ToList();
    }

    public sealed class BeaconSender : IDisposable
    {
        readonly UdpClient udp = new UdpClient { EnableBroadcast = true };
        List<IPAddress> targets = Discovery.BroadcastAddresses();
        DateTime targetsAt = DateTime.Now;

        public void Send(IEnumerable<ServerInstance> servers)
        {
            if (DateTime.Now - targetsAt > TimeSpan.FromSeconds(30))
            {
                targets = Discovery.BroadcastAddresses();  // VPN connected / disconnected meanwhile
                targetsAt = DateTime.Now;
            }
            foreach (var s in servers)
            {
                var data = Discovery.Beacon(s);
                foreach (var t in targets)
                {
                    try { udp.Send(data, data.Length, new IPEndPoint(t, Discovery.Port)); }
                    catch (SocketException) { }
                }
            }
        }

        public void Dispose() => udp.Dispose();
    }

    public sealed class BeaconListener : IDisposable
    {
        readonly UdpClient udp;
        readonly Thread thread;
        readonly object gate = new object();
        readonly Dictionary<string, ServerInfo> heard = new Dictionary<string, ServerInfo>();
        volatile bool stopping;

        public BeaconListener()
        {
            udp = new UdpClient { ExclusiveAddressUse = false };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Discovery.Port));
            thread = new Thread(Run) { IsBackground = true, Name = "Lanppart3 discovery" };
            thread.Start();
        }

        void Run()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (!stopping)
            {
                try
                {
                    var data = udp.Receive(ref from);
                    var info = Discovery.Parse(data, from.Address);
                    if (info != null) lock (gate) heard[info.Address] = info;
                }
                catch (SocketException) { if (stopping) return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        // Servers heard in the last few seconds, once each: a server heard through several networks is listed
        // under the address on one of this PC's own networks (the LAN rather than a VPN it can't reach).
        public List<ServerInfo> Current()
        {
            List<ServerInfo> all;
            lock (gate)
            {
                foreach (var stale in heard.Where(kv => DateTime.Now - kv.Value.Seen > TimeSpan.FromSeconds(5)).Select(kv => kv.Key).ToList())
                    heard.Remove(stale);
                all = heard.Values.ToList();
            }
            var local = Discovery.LocalAddresses().Select(Subnet).ToList();
            return all.GroupBy(s => s.Uid ?? s.Address)
                      .Select(g => g.OrderByDescending(s => local.Contains(Subnet(s.Address.Split(':')[0]))).First())
                      .OrderBy(s => s.Name).ToList();
        }

        static string Subnet(string ip)
        {
            int last = ip.LastIndexOf('.');
            return last > 0 ? ip.Substring(0, last) : ip;
        }

        public void Dispose()
        {
            stopping = true;
            udp.Close();
        }
    }
}

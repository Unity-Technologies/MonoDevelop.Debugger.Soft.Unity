// 
// PlayerConnection.cs
//   
// Authors:
//       Kim Steen Riber <kim@unity3d.com>
//       Mantas Puida <mantas@unity3d.com>
// 
// Copyright (c) 2010 Unity Technologies
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
// 
// 

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Linq;

namespace MonoDevelop.Debugger.Soft.Unity
{
    /// <summary>
    /// Discovery subset of native PlayerConnection class.
    /// </summary>
    public class PlayerConnection
    {
        static readonly int[] PLAYER_MULTICAST_PORTS = { 54997, 34997, 57997, 58997 };
        const string PLAYER_MULTICAST_GROUP = "225.0.0.222";
        const int MAX_LAST_SEEN_ITERATIONS = 3;

        List<Socket> m_MulticastSockets;
        Dictionary<string, int> m_AvailablePlayers = new Dictionary<string, int>();

        public IEnumerable<string> AvailablePlayers
        {
            get { return m_AvailablePlayers.Where(p => 0 < p.Value).Select(p => p.Key); }
        }

        public struct PlayerInfo
        {
            public IPEndPoint m_IPEndPoint;
            public UInt32 m_Flags;
            public UInt32 m_Guid;
            public UInt32 m_EditorGuid;
            public Int32 m_Version;
            public string m_Id;
            public bool m_AllowDebugging;
            public UInt32 m_DebuggerPort;

            public override string ToString()
            {
                return $"PlayerInfo {m_IPEndPoint.Address} {m_IPEndPoint.Port} {m_Flags} {m_Guid} {m_EditorGuid}" +
                    $" {m_Version} {m_Id}:{m_DebuggerPort} {(m_AllowDebugging ? 1 : 0)}";
            }

            public static Dictionary<string, string> ParsePlayerString(string playerString) {
                // Remove trailing null character
                playerString = playerString.TrimEnd('\0');

                // Split string into parts inside [] brackets and after that
                int partStart = 0;
                int partLen = 0;
                List<string> strings = new List<string>();
                for (int i = 0; i < playerString.Length; i++) {
                    var c = playerString[i];
                    if (c == '[') {
                        partLen = i - partStart;
                        if (partLen > 0) {
                            strings.Add(playerString.Substring(partStart, partLen));
                        }
                        partStart = i + 1;
                    }
                    else if (c == ']') {
                        partLen = i - partStart;
                        if (partLen > 0) {
                            strings.Add(playerString.Substring(partStart, partLen));
                        }
                        partStart = i + 1;
                    }
                }

                partLen = playerString.Length - partStart;
                if (partLen > 0) {
                    strings.Add(playerString.Substring(partStart, partLen));
                }

                // Group key/value pairs
                Dictionary<string, string> dict = new Dictionary<string, string>();
                while (strings.Count >= 2) {
                    var key = strings[0].Trim().ToLower();
                    var value = strings[1].Trim();
                    strings.RemoveRange(0, 2);
                    dict[key] = value;
                }

                return dict;
            }

            public static PlayerInfo Parse(string playerString) {
                var res = new PlayerInfo();

                try {
                    var playerSettings = ParsePlayerString(playerString);

                    var ip = playerSettings["ip"];
                    res.m_IPEndPoint = new IPEndPoint(IPAddress.Parse(ip), UInt16.Parse(playerSettings["port"]));
                    res.m_Flags = uint.Parse(playerSettings["flags"]);
                    res.m_Guid = uint.Parse(playerSettings["guid"]);
                    res.m_EditorGuid = uint.Parse(playerSettings["editorid"]);
                    res.m_Version = int.Parse(playerSettings["version"]);
                    res.m_Id = playerSettings["id"];
                    res.m_AllowDebugging = 0 != int.Parse(playerSettings["debug"]);
                    if (playerSettings.ContainsKey("debuggerport"))
                        res.m_DebuggerPort = uint.Parse(playerSettings["debuggerport"]);

                    Console.WriteLine(res.ToString());
                }
                catch (Exception)
                {
                    UnityDebug.Log.Write("Unable to parse player string");
                    throw;
                }

                return res;
            }
        }

        public PlayerConnection()
        {
            m_MulticastSockets = new List<Socket>();
            var nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nics)
            {
                if (adapter.Supports(NetworkInterfaceComponent.IPv4) == false)
                {
                    continue;
                }

                //Fetching adapter index
                IPInterfaceProperties adapterProperties = adapter.GetIPProperties();
                IPv4InterfaceProperties p = adapterProperties.GetIPv4Properties();

                foreach (var port in PLAYER_MULTICAST_PORTS)
                {
                    var multicastSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    try { multicastSocket.ExclusiveAddressUse = false; }
                    catch (SocketException)
                    {
                        // This option is not supported on some OSs
                    }

                    multicastSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    var ipep = new IPEndPoint(IPAddress.Any, port);
                    multicastSocket.Bind(ipep);

                    var ip = IPAddress.Parse(PLAYER_MULTICAST_GROUP);
                    multicastSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                        new MulticastOption(ip, p.Index));
                    UnityDebug.Log.Write($"Setting up multicast option: {ip}: {port}");
                    m_MulticastSockets.Add(multicastSocket);
                }
            }
        }

        public void Poll()
        {
            // Update last-seen
            foreach (var player in m_AvailablePlayers.Keys.ToList())
            {
                --m_AvailablePlayers[player];
            }

            foreach (Socket socket in m_MulticastSockets)
            {
                while (socket != null && socket.Available > 0)
                {
                    var buffer = new byte[1024];

                    var num = socket.Receive(buffer);
                    var str = System.Text.Encoding.ASCII.GetString(buffer, 0, num);

                    RegisterPlayer(str);
                }
            }
        }

        void RegisterPlayer(string playerString)
        {
            m_AvailablePlayers[playerString] = MAX_LAST_SEEN_ITERATIONS;
        }
    }
}

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
		public static readonly int[] PLAYER_MULTICAST_PORTS = new[]{ 54997, 34997, 57997, 58997 };
		public const string PLAYER_MULTICAST_GROUP = "225.0.0.222";
		public const int MAX_LAST_SEEN_ITERATIONS = 3;
		
		private List<Socket> m_MulticastSockets = null;
		private Dictionary<string,int> m_AvailablePlayers = new Dictionary<string,int>();
		
		public IEnumerable<string> AvailablePlayers {
			get {
				return m_AvailablePlayers.Where (p => (0 < p.Value)).Select (p => p.Key);
			}
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
			
			public override string ToString ()
			{
				return string.Format ("PlayerInfo {0} {1} {2} {3} {4} {5} {6}:{7} {8}", m_IPEndPoint.Address, m_IPEndPoint.Port,
									  m_Flags, m_Guid, m_EditorGuid, m_Version, m_Id, m_DebuggerPort, m_AllowDebugging? 1: 0);
			}
			
			public static PlayerInfo Parse(string playerString)
			{
				PlayerInfo res = new PlayerInfo();
				
				try {
					// "[IP] %s [Port] %u [Flags] %u [Guid] %u [EditorId] %u [Version] %d [Id] %s(:d) [Debug] %d"
					Regex r = new Regex("\\[IP\\] (?<ip>.*) \\[Port\\] (?<port>.*) \\[Flags\\] (?<flags>.*)" +
										" \\[Guid\\] (?<guid>.*) \\[EditorId\\] (?<editorid>.*) \\[Version\\] (?<version>.*)" +
										" \\[Id\\] (?<id>[^:]+)(:(?<debuggerPort>\\d+))? \\[Debug\\] (?<debug>.*)");
					
					MatchCollection matches = r.Matches(playerString);
					
					if (matches.Count != 1)
					{
						throw new Exception(string.Format("Player string not recognised {0}", playerString));
					}
					
					string ip = matches[0].Groups["ip"].Value;
					
					res.m_IPEndPoint = new IPEndPoint(IPAddress.Parse(ip), UInt16.Parse (matches[0].Groups["port"].Value));
					res.m_Flags = UInt32.Parse(matches[0].Groups["flags"].Value);
					res.m_Guid = UInt32.Parse(matches[0].Groups["guid"].Value);
					res.m_EditorGuid = UInt32.Parse(matches[0].Groups["guid"].Value);
					res.m_Version = Int32.Parse (matches[0].Groups["version"].Value);
					res.m_Id = matches[0].Groups["id"].Value;
					res.m_AllowDebugging= (0 != int.Parse (matches[0].Groups["debug"].Value));
					if (matches[0].Groups["debuggerPort"].Success)
						res.m_DebuggerPort = UInt32.Parse (matches[0].Groups["debuggerPort"].Value);
					
					System.Console.WriteLine(res.ToString());
				} catch (Exception e) {
					throw new ArgumentException ("Unable to parse player string", e);
				}
				
				return res;
			}
		}
		
		public PlayerConnection ()
		{
			m_MulticastSockets = new List<Socket> ();
			NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface adapter in nics)
			{
				if (adapter.Supports(NetworkInterfaceComponent.IPv4) == false)
				{
					continue;
				}

				//Fetching adapter index
				IPInterfaceProperties adapterProperties = adapter.GetIPProperties();
				IPv4InterfaceProperties p = adapterProperties.GetIPv4Properties();

				foreach (int port in PLAYER_MULTICAST_PORTS)
				{
					try
					{
						var multicastSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
						try { multicastSocket.ExclusiveAddressUse = false; }
						catch (SocketException)
						{
							// This option is not supported on some OSs
						}
						multicastSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
						IPEndPoint ipep = new IPEndPoint(IPAddress.Any, port);
						multicastSocket.Bind(ipep);

						IPAddress ip = IPAddress.Parse(PLAYER_MULTICAST_GROUP);
						multicastSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
											new MulticastOption(ip, p.Index));
						m_MulticastSockets.Add(multicastSocket);
					}
					catch
					{
						throw;
					}
				}
			}
		}
		
		public void Poll ()
		{
			// Update last-seen
			foreach (string player in m_AvailablePlayers.Keys.ToList ()) {
				--m_AvailablePlayers[player];
			}

			foreach (Socket socket in m_MulticastSockets)
			{
				while (socket != null && socket.Available > 0)
				{
					byte[] buffer = new byte[1024];

					int num = socket.Receive(buffer);
					string str = System.Text.Encoding.ASCII.GetString(buffer, 0, num);

					RegisterPlayer(str);
				}
			}
		}
		
		protected void RegisterPlayer(string playerString)
		{
			m_AvailablePlayers[playerString] = MAX_LAST_SEEN_ITERATIONS;
		}
	}
}


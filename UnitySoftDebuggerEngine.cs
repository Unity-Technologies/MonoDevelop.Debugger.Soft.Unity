// 
// UnitySoftDebuggerEngine.cs
//   based on MoonlightSoftDebuggerEngine.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
//       Lucas Meijer <lucas@unity3d.com>
// 
// Copyright (c) 2009 Novell, Inc. (http://www.novell.com)
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

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;

using MonoDevelop.Debugger;
using MonoDevelop.Core;
using MonoDevelop.Core.Execution;
using Mono.Debugging.Soft;
using Mono.Debugging.Client;
using MonoDevelop.Debugger.Soft;

namespace MonoDevelop.Debugger.Soft.Unity
{
	public class UnitySoftDebuggerEngine: DebuggerEngineBackend
	{
		UnitySoftDebuggerSession session;
		static PlayerConnection unityPlayerConnection;

		List<ProcessInfo> usbProcesses = new List<ProcessInfo>();
		bool usbProcessesFinished = true;
		object usbLock = new object();

		List<ProcessInfo> unityProcesses = new List<ProcessInfo> ();
		bool unityProcessesFinished = true;

		internal static Dictionary<uint, PlayerConnection.PlayerInfo> UnityPlayers {
			get;
			private set;
		}

		internal static ConnectorRegistry ConnectorRegistry { get; private set; }


		static UnitySoftDebuggerEngine ()
		{
			UnityPlayers = new Dictionary<uint, PlayerConnection.PlayerInfo> ();
			ConnectorRegistry = new ConnectorRegistry();
			
			bool run = true;
		
			MonoDevelop.Ide.IdeApp.Exiting += (sender, args) => run = false;

			try {
			// HACK: Poll Unity players
			unityPlayerConnection = new PlayerConnection ();
			ThreadPool.QueueUserWorkItem (delegate {
				while (run) {
					lock (unityPlayerConnection) {
						unityPlayerConnection.Poll ();
					}
					Thread.Sleep (1000);
				}
			});
			} catch (Exception e)
			{
				MonoDevelop.Core.LoggingService.LogError ("Error launching player connection discovery service: Unity player discovery will be unavailable", e);
			}
		}

		public override bool CanDebugCommand (ExecutionCommand cmd)
		{
			return cmd is UnityExecutionCommand;
		}

		public override bool IsDefaultDebugger (ExecutionCommand cmd)
		{
			return cmd is UnityExecutionCommand;
		}

		public override DebuggerStartInfo CreateDebuggerStartInfo (ExecutionCommand command)
		{
			return null;
		}
			
		public override DebuggerSession CreateSession ()
		{
			session = new UnitySoftDebuggerSession (ConnectorRegistry);
			session.TargetExited += delegate{ session = null; };
			return session;
		}
		
		public override ProcessInfo[] GetAttachableProcesses ()
		{
			int index = 1;
			List<ProcessInfo> processes = new List<ProcessInfo> ();

			StringComparison comparison = StringComparison.OrdinalIgnoreCase;
			
			if (null != unityPlayerConnection) {
				if(Monitor.TryEnter (unityPlayerConnection)) {
					try {
						foreach (string player in unityPlayerConnection.AvailablePlayers) {
							try {
								PlayerConnection.PlayerInfo info = PlayerConnection.PlayerInfo.Parse (player);
								if (info.m_AllowDebugging) {
									UnityPlayers[info.m_Guid] = info;
									processes.Add (new ProcessInfo (info.m_Guid, info.m_Id));
									++index;
								}
							} catch {
								// Don't care; continue
							}
						}
					}
					finally {
						Monitor.Exit (unityPlayerConnection);
					}
				}
			}

			if (unityProcessesFinished) 
			{
				unityProcessesFinished = false;

				ThreadPool.QueueUserWorkItem (delegate {

					Process[] systemProcesses = Process.GetProcesses();
					var unityThreadProcesses = new List<ProcessInfo>();

					if(systemProcesses != null)
					{
						foreach (Process p in systemProcesses) {
							try {
								if ((p.ProcessName.StartsWith ("unity", comparison) ||
									p.ProcessName.Contains ("Unity.app")) &&
									!p.ProcessName.Contains ("UnityShader") &&
									!p.ProcessName.Contains ("UnityHelper") &&
									!p.ProcessName.Contains ("Unity Helper")) {
									unityThreadProcesses.Add (new ProcessInfo (p.Id, string.Format ("{0} ({1})", "Unity Editor", p.ProcessName)));
								}
							} catch {
								// Don't care; continue
							}
						}

						unityProcesses = unityThreadProcesses;
						unityProcessesFinished = true;
					}
				});
			}

			processes.AddRange (unityProcesses);

			if (usbProcessesFinished)
			{
				usbProcessesFinished = false;

				ThreadPool.QueueUserWorkItem (delegate {
					// Direct USB devices
					lock(usbLock)
					{
						var usbThreadProcesses = new List<ProcessInfo>();

						try
						{
							iOSDevices.GetUSBDevices (ConnectorRegistry, usbThreadProcesses);
						}
						catch(NotSupportedException)
						{
							LoggingService.LogInfo("iOS over USB not supported on this platform");
						}
						catch(Exception e)
						{
							LoggingService.LogError("iOS USB Error: " + e);
						}
						usbProcesses = usbThreadProcesses;
						usbProcessesFinished = true;
					}
				});
			}

			processes.AddRange (usbProcesses);

			return processes.ToArray ();
		}

		public string Name {
			get {
				return "Mono Soft Debugger for Unity";
			}
		}
	}

	class UnityExecutionCommand : ExecutionCommand
	{

	};

	// Allows to define how to setup and tear down connection for debugger to connect to the
	// debugee. For example to setup TCP tunneling over USB.
	public interface IUnityDbgConnector
	{
		SoftDebuggerStartInfo SetupConnection();
		void OnDisconnect();
	}


	// Manages a map from process id to IUnityDbgConnector, so that services can supply a list of
	// debugees and how to connect to them, and UnitySoftDebuggerSession can have a way for
	// establishing a connection to them.
	public class ConnectorRegistry
	{
		// This is used to map process id <-> unique string id. MonoDevelop attachment is built
		// around process ids.
		object processIdLock = new object();
		uint nextProcessId = 1000000;
		Dictionary<uint, string> processIdToUniqueId = new Dictionary<uint, string>();
		Dictionary<string, uint> uniqueIdToProcessId = new Dictionary<string, uint>();

		public Dictionary<uint, IUnityDbgConnector> Connectors { get; private set; }


		public uint GetProcessIdForUniqueId(string uid)
		{
			lock (processIdLock)
			{
				uint processId;
				if (uniqueIdToProcessId.TryGetValue(uid, out processId))
					return processId;

				processId = nextProcessId++;
				processIdToUniqueId.Add(processId, uid);
				uniqueIdToProcessId.Add(uid, processId);

				return processId;
			}
		}


		public string GetUniqueIdFromProcessId(uint processId)
		{
			lock (processIdLock) {
				string uid;
				if (processIdToUniqueId.TryGetValue(processId, out uid))
					return uid;

				return null;
			}
		}


		public ConnectorRegistry()
		{
			Connectors = new Dictionary<uint, IUnityDbgConnector>();
		}
	}
}

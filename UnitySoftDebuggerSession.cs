// 
// UnityDebuggerSession.cs
//   based on IPhoneDebuggerSession.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
//       Lucas Meijer <lucas@unity3d.com>
//       Levi Bard <levi@unity3d.com>
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
using Mono.Debugger;
using Mono.Debugging;
using Mono.Debugging.Soft;
using Mono.Debugger.Soft;
using Mono.Debugging.Client;
using System.Threading;
using System.Diagnostics;
using System.IO;
using MonoDevelop.Core;
using System.Net.Sockets;
using System.Net;
using System.Collections;
using System.Collections.Generic;

namespace MonoDevelop.Debugger.Soft.Unity
{
	/// <summary>
	/// Debugger session for Unity scripting code
	/// </summary>
	public class UnitySoftDebuggerSession : SoftDebuggerSession
	{
		ConnectorRegistry connectorRegistry;
		// Connector that was used to make connection for current session.
		IUnityDbgConnector currentConnector;
		
		public UnitySoftDebuggerSession (ConnectorRegistry connectorRegistry)
		{
			this.connectorRegistry = connectorRegistry;

			Adaptor.BusyStateChanged += (object sender, BusyStateEventArgs e) => SetBusyState (e);
		}

		protected override string GetConnectingMessage (DebuggerStartInfo dsi)
		{
			Ide.DispatchService.GuiDispatch (() =>
				Ide.IdeApp.Workbench.CurrentLayout = "Debug"
			);
			return base.GetConnectingMessage (dsi);
		}
		
		protected override void OnAttachToProcess (long processId)
		{
			if (connectorRegistry.Connectors.ContainsKey((uint)processId)) {
				currentConnector = connectorRegistry.Connectors[(uint)processId];
				StartConnecting(currentConnector.SetupConnection(), 3, 1000);
				return;
			} else if (UnitySoftDebuggerEngine.UnityPlayers.ContainsKey ((uint)processId)) {
				PlayerConnection.PlayerInfo player = UnitySoftDebuggerEngine.UnityPlayers[(uint)processId];
				int port = (0 == player.m_DebuggerPort
					? (int)(56000 + (processId % 1000))
					: (int)player.m_DebuggerPort);
				try {
					StartConnecting (new SoftDebuggerStartInfo (new SoftDebuggerConnectArgs (player.m_Id, player.m_IPEndPoint.Address, (int)port)), 3, 1000);
				} catch (Exception ex) {
					throw new Exception (string.Format ("Unable to attach to {0}:{1}", player.m_IPEndPoint.Address, port), ex);
				}
				return;
			}

			long defaultPort = 56000 + (processId % 1000);
			StartConnecting(new SoftDebuggerStartInfo(new SoftDebuggerConnectArgs(null, IPAddress.Loopback, (int)defaultPort)), 3, 1000);
		}

		protected override void EndSession ()
		{
			Detach ();
			base.EndSession ();
		}

		protected override void OnExit ()
		{
			Detach ();
			base.OnExit ();
		}

		protected override void OnDetach()
		{
			try {
				Ide.DispatchService.GuiDispatch(() =>
					Ide.IdeApp.Workbench.CurrentLayout = UnityProjectServiceExtension.EditLayout
				);

				VirtualMachine.Detach();
				base.EndSession();
			} catch (ObjectDisposedException) {
			} catch (VMDisconnectedException) {
			} catch (NullReferenceException) {
			}

			if (currentConnector != null) {
				currentConnector.OnDisconnect();
				currentConnector = null;
			}
		}
	}
}

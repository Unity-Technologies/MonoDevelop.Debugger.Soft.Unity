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
using Mono.Debugging.Soft;
using Mono.Debugger.Soft;
using Mono.Debugging.Client;

namespace MonoDevelop.Debugger.Soft.Unity
{
	/// <summary>
	/// Debugger session for Unity scripting code
	/// </summary>
	public class UnitySoftDebuggerSession : SoftDebuggerSession
	{
		// Connector that was used to make connection for current session.
		IUnityDbgConnector currentConnector;
		
		public UnitySoftDebuggerSession ()
		{
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
			StartConnecting (GetUnitySoftDebuggerStartInfo (processId), 3, 1000);
		}

		public SoftDebuggerStartInfo GetUnitySoftDebuggerStartInfo(long processId)
		{
			var attachInfo = UnityProcessDiscovery.GetUnityAttachInfo (processId, ref currentConnector);
			return new SoftDebuggerStartInfo (new SoftDebuggerConnectArgs (attachInfo.AppName, attachInfo.Address, attachInfo.Port));
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

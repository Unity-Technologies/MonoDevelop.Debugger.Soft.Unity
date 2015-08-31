// 
// UnityProjectServiceExtension.cs 
//   
// Author:
//       Levi Bard <levi@unity3d.com>
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

using System.Linq;
using System.Collections.Generic;

using MonoDevelop.Ide;
using MonoDevelop.Projects;
using MonoDevelop.Core.Execution;
using MonoDevelop.Core;

namespace MonoDevelop.Debugger.Soft.Unity
{
	/// <summary>
	/// ProjectServiceExtension to allow Unity projects to be executed under the soft debugger
	/// </summary>
	public class UnityProjectServiceExtension: ProjectServiceExtension
	{
		internal static string EditLayout = "Solution";
		private DebuggerEngine unityDebuggerEngine = null;
		bool processesPolled = false;
		UnityExecutionCommand executionCommand = new UnityExecutionCommand();

		DebuggerEngine UnityDebuggerEngine
		{
			get 
			{
				if (unityDebuggerEngine == null)
					unityDebuggerEngine = DebuggingService.GetDebuggerEngines ().FirstOrDefault (e => e.Id == "MonoDevelop.Debugger.Soft.Unity");

				return unityDebuggerEngine;
			}
		}

		public UnityProjectServiceExtension()
		{
			MonoDevelop.Ide.IdeApp.FocusIn += delegate {
				if(UnityDebuggerEngine != null)
					UnityDebuggerEngine.GetAttachableProcesses();
			};
		}

		/// <summary>
		/// Detects whether any of the given projects reference UnityEngine
		/// </summary>
		private static bool ReferencesUnity (IEnumerable<Project> projects)
		{
			return null != projects.FirstOrDefault (project => 
				(project is DotNetProject) && 
				null != ((DotNetProject)project).References.FirstOrDefault (reference =>
				       reference.Reference.Contains ("UnityEngine")
				)
			);
		}
		
		#region ProjectServiceExtension overrides
		
		private bool CanExecuteProject (Project project) {
			return null != project &&  ReferencesUnity (new Project[]{ project });
		}
		
		/// <summary>
		/// Flags Unity projects for debugging with this addin
		/// </summary>
		protected override bool CanExecute (SolutionEntityItem item, ExecutionContext context, ConfigurationSelector configuration)
		{
			// HACK: Poll for attachable processes so the players processes are available when trying to attach for the first time.
			if (!processesPolled) {
				DispatchService.ThreadDispatch (delegate {					
					// Hack: Poll twice, this increases the chances of iOS USB being ready
					UnityDebuggerEngine.GetAttachableProcesses ();
					System.Threading.Thread.Sleep(250);
					UnityDebuggerEngine.GetAttachableProcesses ();
				});
				processesPolled = true;
			}

			if (context.ExecutionHandler != null)
				context.ExecutionHandler.CanExecute (executionCommand);

			if (CanExecuteProject (item as Project))
				return true;

			return base.CanExecute (item, context, configuration);
		}

		private void ShowAttachToProcessDialog()
		{
			DispatchService.GuiDispatch (delegate {
				var dlg = new AttachToProcessDialog ();
				try {
					if (MessageService.RunCustomDialog (dlg) == (int) Gtk.ResponseType.Ok)
						IdeApp.ProjectOperations.AttachToProcess (dlg.SelectedDebugger, dlg.SelectedProcess);
				}
				finally {
					dlg.Destroy ();
				}
			});
		}
		
		/// <summary>
		/// Launch Unity project
		/// </summary>
		public override void Execute (IProgressMonitor monitor, IBuildTarget item, ExecutionContext context, ConfigurationSelector configuration)
		{
			var project = item as Project;
			var target = context.ExecutionTarget as UnityExecutionTarget;

			if (!CanExecuteProject (project) || target == null) 
			{
				base.Execute (monitor, item, context, configuration);
				return;
			}
			
			if (target.Id.StartsWith("Unity.Instance")) 
			{
				var processes = UnityDebuggerEngine.GetAttachableProcesses ();
				var unityEngineProcesses = processes.Where (p => p.Name.Contains (target.ProcessName)).ToArray ();

				if (unityEngineProcesses.Length == 0) 
				{
					MessageService.ShowError (target.Name + " not found");
					LoggingService.LogError (target.Name + " not found");
				} 
				else if (unityEngineProcesses.Length == 1) 
				{
					DispatchService.GuiDispatch (delegate {
						IdeApp.ProjectOperations.AttachToProcess (unityDebuggerEngine, unityEngineProcesses [0]); 
					});
				} 
				else 
				{
					ShowAttachToProcessDialog ();
				}

			} 
			else if (target.Id == "Unity.AttachToProcess") 
			{
				ShowAttachToProcessDialog ();
			} 
			else
			{
				MessageService.ShowError ("UnityProjectServiceExtension: Unsupported target.Id: " + target.Id);
				MonoDevelop.Core.LoggingService.LogError ("UnityProjectServiceExtension: Unsupported target.Id: " + target.Id);
				base.Execute (monitor, item, context, configuration);
			}
		}

		class UnityExecutionTarget : ExecutionTarget
		{
			string name;
			string id;
			string processName;

			public UnityExecutionTarget(string name, string id, string processName)
			{
				this.name = name;
				this.id = id + (processName == null ? "" : "." + processName);
				this.processName = processName;
			}

			public override string Name { get { return name; } }
			public override string Id { get { return id; } }
			public string ProcessName { get { return processName; } }
		}

		protected override IEnumerable<ExecutionTarget> GetExecutionTargets (SolutionEntityItem item, ConfigurationSelector configuration)
		{
			var list = new List<ExecutionTarget> ();

			if (CanExecuteProject (item as Project)) 
			{
				list.Add (new UnityExecutionTarget ("Unity Editor", "Unity.Instance", "Unity Editor"));

				if (Platform.IsMac) 
				{
					list.Add (new UnityExecutionTarget ("OSX Player", "Unity.Instance", "OSXPlayer"));
					list.Add (new UnityExecutionTarget ("OSX WebPlayer", "Unity.Instance", "OSXWebPlayer"));
				}

				if (Platform.IsWindows) 
				{
					list.Add (new UnityExecutionTarget ("Windows Player", "Unity.Instance", "WindowsPlayer"));
					list.Add (new UnityExecutionTarget ("Windows WebPlayer", "Unity.Instance", "WindowsWebPlayer"));
				}

				if (Platform.IsLinux) 
				{
					list.Add (new UnityExecutionTarget ("Linux Player", "Unity.Instance", "LinuxPlayer"));
					list.Add (new UnityExecutionTarget ("Linux WebPlayer", "Unity.Instance", "LinuxWebPlayer"));
				}

				list.Add (new UnityExecutionTarget ("iOS Player", "Unity.Instance", "iPhonePlayer"));

				try
				{
					if(iOSDevices.Supported && iOSDevices.Initialized)
						list.Add (new UnityExecutionTarget ("iOS Player (USB)", "Unity.Instance", "Unity iOS USB"));
				}
				catch {}

				list.Add (new UnityExecutionTarget ("Android Player", "Unity.Instance", "AndroidPlayer"));

				list.Add (new UnityExecutionTarget ("Attach To Process", "Unity.AttachToProcess", null));

			}
			return list;
		}
		
		public override bool GetNeedsBuilding (IBuildTarget item, ConfigurationSelector configuration)
		{
			if (item is WorkspaceItem){ return GetNeedsBuilding ((WorkspaceItem)item, configuration); }
			if (item is Project && ReferencesUnity (new Project[]{ (Project)item }) && !Util.UnityBuild) {
				return false;
			}
			return base.GetNeedsBuilding (item, configuration);
		}
		
		#endregion
	}
}


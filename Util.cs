// 
// Util.cs 
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

using System;
using System.IO;

using MonoDevelop.Ide;
using MonoDevelop.Core;

namespace MonoDevelop.Debugger.Soft.Unity
{
	/// <summary>
	/// Static utility class
	/// </summary>
	public static class Util
	{
		// Default Unity editor installer locations
		static readonly string unityOSX = "/Applications/Unity/Unity.app/Contents/MacOS/Unity"; 
		static readonly string unityWinX86 = @"C:\Program Files (x86)\Unity\Editor\Unity.exe";
		static readonly string unityWin = @"C:\Program Files\Unity\Editor\Unity.exe";
		
		// Keys for PropertyService
		public static readonly string UnityLocationProperty = "MonoDevelop.Debugger.Soft.Unity.UnityLocation";
		public static readonly string UnityBuildProperty = "MonoDevelop.Debugger.Soft.Unity.BuildUnity";
		
		/// <summary>
		/// Configured path to Unity editor
		/// </summary>
		public static string UnityLocation {
			get{ return PropertyService.Get (UnityLocationProperty, FindUnity ()); }
			set{ PropertyService.Set (UnityLocationProperty, value); }
		}

		public static string UnityEditorDataFolder {
			get {
				var path = Path.GetDirectoryName(UnityLocation);
				if (path == null)
					return "";

				if (Platform.IsMac)
					return Path.GetDirectoryName(path);
				else if (Platform.IsWindows)
					return Path.Combine(path, "Data");
				else
					throw new Exception("Platform not supported");
			}
		}
		
		/// <summary>
		/// Whether to try to build Unity projects
		/// </summary>
		public static bool UnityBuild {
			get{ return PropertyService.Get (UnityBuildProperty, true); }
			set{ PropertyService.Set (UnityBuildProperty, value); }
		}
		
		/// <summary>
		/// Get the best-guess default Unity editor path for the current platform
		/// </summary>
		public static string FindUnity ()
		{
			string unityLocation = string.Empty;
			
			if (Platform.IsMac) {
				unityLocation = unityOSX;
			} else if (Platform.IsWindows) {
				unityLocation = (File.Exists (unityWinX86)? unityWinX86: unityWin);
			} 

			// If Unity isn't installed to its default location then another strategy is needed.
			// Next best option is to guess based on where MonoDevelop is running, as it's
			// installed side-by-side with Unity.
			if(string.IsNullOrEmpty(unityLocation) || !File.Exists (unityLocation))
			{
				string mdLocation = System.Reflection.Assembly.GetEntryAssembly().Location;

				string sxsPath = string.Empty;
				if(Platform.IsMac && mdLocation.Contains ("MonoDevelop.app")) {
					// Go back up to where the application bundle is and look for a Unity.app bundle alongside it
					sxsPath = mdLocation.Substring(0, mdLocation.LastIndexOf("MonoDevelop.app")) + "Unity.app/Contents/MacOS/Unity";
				} else if(Platform.IsWindows) {
					// Strip the "MonoDevelop/bin/MonoDevelop.exe" from the end of the path
					sxsPath = mdLocation.Substring (0, mdLocation.Length - "MonoDevelop/bin/MonoDevelop.exe".Length) + @"Editor\Unity.exe";
				}

				if(!String.IsNullOrEmpty(sxsPath) && File.Exists (sxsPath))
					unityLocation = sxsPath;
			}
			
			return unityLocation;
		}
	}
}


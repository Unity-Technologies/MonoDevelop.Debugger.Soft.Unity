using MonoDevelop.Components;
using MonoDevelop.Ide.WelcomePage;

namespace MonoDevelop.Ide.Unity
{
	[Mono.Addins.Extension]	
	public class WelcomePageProvider : IWelcomePageProvider
	{
		public Control CreateWidget ()
		{
			return new UnityWelcomePage ();
		}
	}
}


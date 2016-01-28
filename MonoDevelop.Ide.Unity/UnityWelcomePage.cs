using MonoDevelop.Core;
using MonoDevelop.Ide.WelcomePage;
using Gtk;

namespace MonoDevelop.Ide.Unity
{
	public class UnityWelcomePage: WelcomePageWidget
	{
		protected override void BuildContent (Container parent)
		{
			LogoImage = Xwt.Drawing.Image.FromResource ("WelcomePage_Logo.png");
			TopBorderImage = Xwt.Drawing.Image.FromResource ("WelcomePage_TopBorderRepeat.png");

			var mainAlignment = new Gtk.Alignment (0.5f, 0.5f, 0f, 1f);

			var mainCol = new WelcomePageColumn ();
			mainCol.MinWidth = 600;
			mainAlignment.Add (mainCol);

			mainCol.PackStart (new WelcomePageRecentProjectsList (GettextCatalog.GetString ("Solutions")), true, true, 20);

			parent.Add (mainAlignment);
		}
	}
	
}

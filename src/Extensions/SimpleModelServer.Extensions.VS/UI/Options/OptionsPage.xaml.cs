using System.Windows.Controls;
using SimpleModelServer.UI;

namespace SimpleModelServer.Extensions.VS.UI.Options
{
	/// <summary>
	/// Options Page Control wrapper that hosts the reusable SettingsPanel.
	/// This allows the settings UI to be shared across multiple applications.
	/// </summary>
	public partial class OptionsPageControl : UserControl
	{
		public OptionsPageControl()
		{
			InitializeComponent();
		}
	}
}
